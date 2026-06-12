import { create } from 'zustand';
import type {
  TrainingPlan,
  TurnChoice,
  AdditionalCounts,
  EventCountTemplate,
  SupportCard,
  MemoryBonus,
  MemoryAttributeBonus,
  MemoryPreset,
  EventCountPreset,
  Character,
  StatusValues,
  WeekSchedule,
} from '../types/models';
import {
  emptyAdditionalCounts,
  emptyMemoryBonus,
  isEmptyAllMemoryBonuses,
} from '../types/models';
import type { PlanType, RoleType, ActionType } from '../types/enums';
import type { CalculationResult, DeckResult } from '../types/results';
import type { CardInventoryEntry } from '../types/inventory';
import { useAppStore } from './appStore';
import { selectMultiplePatterns, selectMultiplePatternsHif } from '../services/cardScoring';
import { calculate } from '../services/statusCalculation';
import { applyCharacterToggles } from '../services/characterBonus';
import { trackEvent, startTimer, endTimer, incrementCounter, trackFunnelStep } from '../utils/analytics';

const SELECTED_CHARACTER_KEY = 'selectedCharacterId';
// 旧: 全キャラ共通の単一トグル。現在はキャラ毎マップに移行（下のマイグレーション参照）
const LEGACY_UNCAP3_BONUS_KEY = 'uncap3CharacterBonusEnabled';
// 3凸・STEP4 のON/OFFはキャラ毎に保持（id -> bool のマップを localStorage に保存）
const UNCAP3_BONUS_MAP_KEY = 'uncap3BonusByChar';
const STEP4_BONUS_MAP_KEY = 'step4BonusByChar';
const MEMORY_PRESETS_KEY = 'memoryPresets';
const EVENT_COUNT_PRESETS_KEY = 'eventCountPresets';

/** id -> bool のトグルマップを localStorage から読み込む。 */
function loadBoolMap(key: string): Record<string, boolean> {
  if (typeof window === 'undefined') return {};
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      const out: Record<string, boolean> = {};
      for (const [k, v] of Object.entries(parsed)) {
        if (typeof v === 'boolean') out[k] = v;
      }
      return out;
    }
  } catch {
    /* 破損時は空 */
  }
  return {};
}

function saveBoolMap(key: string, map: Record<string, boolean>) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(key, JSON.stringify(map));
  } catch (e) {
    console.warn('トグルマップの保存に失敗:', e);
  }
}

/** 3凸は既定OFF。マップに無ければ false。 */
function isUncap3EnabledFor(map: Record<string, boolean>, id: string | null): boolean {
  if (!id) return false;
  return map[id] ?? false;
}

/** STEP4は既定ON。マップに無ければ true。 */
function isStep4EnabledFor(map: Record<string, boolean>, id: string | null): boolean {
  if (!id) return true;
  return map[id] ?? true;
}

/** 旧・全キャラ共通の3凸トグルを、当時選択中だったキャラのマップ値へ一度だけ移行する。 */
function migrateLegacyUncap3Toggle() {
  if (typeof window === 'undefined') return;
  if (localStorage.getItem(UNCAP3_BONUS_MAP_KEY) == null) {
    const legacy = localStorage.getItem(LEGACY_UNCAP3_BONUS_KEY);
    const selId = localStorage.getItem(SELECTED_CHARACTER_KEY);
    if (legacy === '1' && selId) {
      saveBoolMap(UNCAP3_BONUS_MAP_KEY, { [selId]: true });
    }
  }
  localStorage.removeItem(LEGACY_UNCAP3_BONUS_KEY);
}
migrateLegacyUncap3Toggle();
/** 持ち込みメモリー プリセットの保存可能件数上限。 */
export const MAX_MEMORY_PRESETS = 5;
/** イベント回数プリセットの保存可能件数上限。 */
export const MAX_EVENT_COUNT_PRESETS = 10;

function loadMemoryPresetsFromStorage(): MemoryPreset[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = localStorage.getItem(MEMORY_PRESETS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as MemoryPreset[]) : [];
  } catch {
    return [];
  }
}

function persistMemoryPresets(presets: MemoryPreset[]) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(MEMORY_PRESETS_KEY, JSON.stringify(presets));
  } catch (e) {
    console.warn('メモリープリセットの保存に失敗:', e);
  }
}

function loadEventCountPresetsFromStorage(): EventCountPreset[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = localStorage.getItem(EVENT_COUNT_PRESETS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as EventCountPreset[]) : [];
  } catch {
    return [];
  }
}

function persistEventCountPresets(presets: EventCountPreset[]) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(EVENT_COUNT_PRESETS_KEY, JSON.stringify(presets));
  } catch (e) {
    console.warn('イベント回数プリセットの保存に失敗:', e);
  }
}

/** 日程（スケジュール）を主入力にするシナリオの planId 集合。HIF は専用の hifStore で扱うため含めない。 */
export const SCHEDULE_PLAN_IDS = new Set<string>(['hatsu_legend', 'nia']);

/**
 * 日程方式の1週分の選択。HIF と違い公開レッスン(sub_stat)・試験配分は無いので action のみ。
 */
export interface ScheduleChoice {
  action: ActionType;
}

/** 日程プリセット (個別調整した結果を名前付きで保存・読込) */
export interface SchedulePreset {
  name: string;
  scheduleChoices: Record<number, ScheduleChoice>;
}

const SCHEDULE_CHOICE_PRESETS_KEY = 'scheduleChoicePresets';
/** 日程プリセットの保存可能件数上限（プランごと）。 */
export const MAX_SCHEDULE_PRESETS = 10;

/** localStorage から planId→プリセット配列のマップを読み込む。 */
function loadSchedulePresetsByPlan(): Record<string, SchedulePreset[]> {
  if (typeof window === 'undefined') return {};
  try {
    const raw = localStorage.getItem(SCHEDULE_CHOICE_PRESETS_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, SchedulePreset[]>;
    }
  } catch {
    /* 破損時は空 */
  }
  return {};
}

function persistSchedulePresetsByPlan(map: Record<string, SchedulePreset[]>) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(SCHEDULE_CHOICE_PRESETS_KEY, JSON.stringify(map));
  } catch (e) {
    console.warn('スケジュールプリセットの保存に失敗:', e);
  }
}

interface CalcState {
  selectedPlanId: string;
  selectedPlanType: PlanType;
  voRole: RoleType;
  daRole: RoleType;
  viRole: RoleType;
  voSpCount: number;
  daSpCount: number;
  viSpCount: number;
  additionalCounts: AdditionalCounts;
  selectedTemplateName: string | null;
  ownedOnly: boolean;
  contestMode: boolean;
  requiredCardIds: string[];
  /** 除外カード（編成候補から外す。枚数制限なし・セッション限定） */
  excludedCardIds: string[];
  selectedCharacterId: string | null;
  /** 選択中キャラの3凸トグル（uncap3BonusByChar から導出。既存読み出し互換のため保持） */
  uncap3BonusEnabled: boolean;
  /** 選択中キャラのSTEP4トグル（step4BonusByChar から導出） */
  step4BonusEnabled: boolean;
  /** 3凸トグルのキャラ毎の保持値（id -> bool、既定OFF） */
  uncap3BonusByChar: Record<string, boolean>;
  /** STEP4トグルのキャラ毎の保持値（id -> bool、既定ON） */
  step4BonusByChar: Record<string, boolean>;
  /** 持ち込みメモリー（最大4枚・セッション限定。永続化なし） */
  memoryBonuses: MemoryBonus[];
  /** 保存済みプリセット（localStorage に永続化、上限 MAX_MEMORY_PRESETS 件） */
  memoryPresets: MemoryPreset[];
  /** イベント回数の保存済みプリセット（localStorage に永続化、上限 MAX_EVENT_COUNT_PRESETS 件） */
  eventCountPresets: EventCountPreset[];
  deckResults: DeckResult[];
  selectedPatternIndex: number;
  calculationResult: CalculationResult | null;
  // キャラ補正を抜いた結果（キャラ未選択時は null）
  calculationResultWithoutCharacter: CalculationResult | null;
  errorMessage: string | null;

  // internal state for re-applying patterns
  _lastMainStats: string[];
  _lastLessonWeekCount: number;
  /** 日程方式プランで選出/再計算に使う、ユーザ確定済みの TurnChoice。 */
  _lastTurnChoices: TurnChoice[];

  /** 日程方式: planId → week → 選択。calcStore は両タブ共有のため planId キーで保持する。 */
  scheduleChoices: Record<string, Record<number, ScheduleChoice>>;
  /** 一括レッスン属性（全レッスン週に適用する単一属性。メイン1/2の概念は廃止）。 */
  scheduleBulkLessonStat: 'vo' | 'da' | 'vi';
  /** 一括授業属性。 */
  scheduleBulkClassStat: 'vo' | 'da' | 'vi';
  /** 日程プリセット（planId → プリセット配列、localStorage 永続化）。 */
  schedulePresetsByPlan: Record<string, SchedulePreset[]>;
  /** NIAオーディション: week → 選択した種別名。未設定の週は先頭(最強)種別を使う。 */
  niaAuditionTierByWeek: Record<number, string>;

  setSelectedPlanId: (id: string) => void;
  setSelectedPlanType: (type: PlanType) => void;
  setRole: (stat: 'vo' | 'da' | 'vi', role: RoleType) => void;
  setSpCount: (stat: 'vo' | 'da' | 'vi', count: number) => void;
  setAdditionalCount: (key: string, value: number) => void;
  applyTemplate: (template: EventCountTemplate) => void;
  setOwnedOnly: (v: boolean) => void;
  setContestMode: (v: boolean) => void;
  addRequiredCard: (cardId: string) => void;
  removeRequiredCard: (cardId: string) => void;
  addExcludedCard: (cardId: string) => void;
  removeExcludedCard: (cardId: string) => void;
  setSelectedCharacter: (id: string | null) => void;
  setUncap3BonusEnabled: (v: boolean) => void;
  setStep4BonusEnabled: (v: boolean) => void;
  setMemoryBonus: (index: number, stat: 'vo' | 'da' | 'vi', patch: Partial<MemoryAttributeBonus>) => void;
  clearMemoryBonuses: () => void;
  /** 現在のメモリー値を名前付きで保存。同名は上書き、上限超過は無視。 */
  saveMemoryPreset: (name: string) => void;
  /** プリセット名を指定して読み込み（4枠を上書き、自動再計算）。 */
  loadMemoryPreset: (name: string) => void;
  deleteMemoryPreset: (name: string) => void;
  /** 現在のイベント回数入力を名前付きで保存。同名は上書き、上限超過は無視。 */
  saveEventCountPreset: (name: string) => void;
  /** プリセット名を指定して読み込み（イベント回数を上書き、自動再計算）。 */
  loadEventCountPreset: (name: string) => void;
  deleteEventCountPreset: (name: string) => void;
  executeCalculate: () => void;
  selectPattern: (index: number) => void;

  // --- 日程方式 (初レジェンド / NIA) ---
  /** 1週分の選択を設定。 */
  setScheduleChoice: (planId: string, week: number, choice: ScheduleChoice) => void;
  /** 未設定の週を現行の自動配分でシード（既存の選択は上書きしない）。 */
  seedScheduleDefaults: (planId: string) => void;
  setScheduleBulkLessonStat: (stat: 'vo' | 'da' | 'vi') => void;
  setScheduleBulkClassStat: (stat: 'vo' | 'da' | 'vi') => void;
  /** 全レッスン週に選択属性を一括適用。 */
  applyScheduleBulkLesson: (planId: string) => void;
  /** 全授業週に bulkClassStat を一括適用。 */
  applyScheduleBulkClass: (planId: string) => void;
  /** 現在の日程を名前付きで保存（同名上書き、空は保存しない、上限あり）。 */
  saveSchedulePreset: (planId: string, name: string) => void;
  /** プリセットを読み込み現在の日程に反映（要・計算実行）。 */
  loadSchedulePreset: (planId: string, name: string) => void;
  deleteSchedulePreset: (planId: string, name: string) => void;
  /** NIAオーディションの種別を選択（結果があれば再適用して即反映）。 */
  setNiaAuditionTier: (week: number, tierName: string) => void;
}

function getCandidateCards(
  allCards: SupportCard[],
  inventory: CardInventoryEntry[],
  ownedOnly: boolean,
  contestMode: boolean,
): SupportCard[] {
  let cards = allCards;
  if (ownedOnly) {
    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    cards = cards.filter((c) => ownedIds.has(c.id));
  }
  if (contestMode) {
    cards = cards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item');
  }
  return cards;
}

function buildUncapLevels(
  allCards: SupportCard[],
  inventory: CardInventoryEntry[],
  ownedOnly: boolean,
): Record<string, number> {
  if (ownedOnly) {
    const levels: Record<string, number> = {};
    for (const e of inventory) {
      levels[e.card_id] = e.uncap;
    }
    return levels;
  }
  const levels: Record<string, number> = {};
  for (const c of allCards) {
    levels[c.id] = 4;
  }
  return levels;
}

interface CardSelectionContext {
  candidateCards: SupportCard[];
  uncapLevels: Record<string, number>;
  rentalPool?: SupportCard[];
  requiredCardIds?: string[];
}

/**
 * 候補カード・凸レベル・レンタルプール・必須カードを構築（所持/コンテスト/除外/必須フィルタ込み）。
 * 日程方式の executeCalculate 分岐で使う。ロール経路は従来どおりインラインのまま。
 */
function prepareCardSelectionContext(
  state: CalcState,
  allCards: SupportCard[],
  inventory: CardInventoryEntry[],
): { ctx?: CardSelectionContext; error?: string } {
  let candidateCards = getCandidateCards(allCards, inventory, state.ownedOnly, state.contestMode);
  const uncapLevels = buildUncapLevels(allCards, inventory, state.ownedOnly);

  let rentalPool: SupportCard[] | undefined;
  if (state.ownedOnly) {
    rentalPool = state.contestMode
      ? allCards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item')
      : allCards;
  }

  if (state.excludedCardIds.length > 0) {
    const excludedSet = new Set(state.excludedCardIds);
    candidateCards = candidateCards.filter((c) => !excludedSet.has(c.id));
    if (rentalPool != null) rentalPool = rentalPool.filter((c) => !excludedSet.has(c.id));
  }

  const requiredCardIds = state.requiredCardIds.length > 0 ? state.requiredCardIds : undefined;
  if (requiredCardIds != null) {
    const requiredIdSet = new Set(requiredCardIds);
    const candidateIdSet = new Set(candidateCards.map((c) => c.id));
    if (state.ownedOnly) {
      const ownedIdSet = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
      for (const card of allCards) {
        if (requiredIdSet.has(card.id) && ownedIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
          candidateCards.push(card);
        }
      }
      if (rentalPool != null) {
        const rentalIdSet = new Set(rentalPool.map((c) => c.id));
        for (const card of allCards) {
          if (requiredIdSet.has(card.id) && !rentalIdSet.has(card.id)) rentalPool.push(card);
        }
      }
    } else {
      for (const card of allCards) {
        if (requiredIdSet.has(card.id) && !candidateIdSet.has(card.id)) candidateCards.push(card);
      }
    }
  }

  if (requiredCardIds != null && state.ownedOnly) {
    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    const notOwnedCount = requiredCardIds.filter((id) => !ownedIds.has(id)).length;
    if (notOwnedCount > 1) {
      return { error: '未所持の必須カードは最大1枚です（レンタル枠使用）' };
    }
  }

  return { ctx: { candidateCards, uncapLevels, rentalPool, requiredCardIds } };
}

/**
 * レッスン週へのメイン属性配分を決める純粋関数。中間試験前は main1:main2=1:1 で交互、
 * 試験後は 2:1 (main1多め)。単一メインは全レッスンをそれに割り当て。固定週はスキップ。
 * 戻り値は week → レッスンアクション（レッスン週のみ）。
 * autoAssignTurnChoices / 日程シード / 一括配分が共通で使う単一ソース。
 */
function distributeLessons(plan: TrainingPlan, mainStats: string[]): Record<number, ActionType> {
  const out: Record<number, ActionType> = {};
  const lessonWeeks: { week: number; available_actions: string[] }[] = [];
  for (const week of plan.schedule) {
    const isFixed = week.type === 'fixed_event' || week.type === 'exam' || week.type === 'audition';
    if (isFixed) continue;
    const hasLesson = week.available_actions.some((a) =>
      a === 'vo_lesson' || a === 'da_lesson' || a === 'vi_lesson',
    );
    if (hasLesson) lessonWeeks.push({ week: week.week, available_actions: week.available_actions });
  }

  // Find mid-exam week
  const midExamWeek = plan.schedule.find(
    (w) => (w.type === 'fixed_event' || w.type === 'exam') && w.event_name === '中間試験',
  )?.week ?? 10;

  if (mainStats.length >= 2) {
    const main1Action: ActionType = `${mainStats[0]}_lesson` as ActionType;
    const main2Action: ActionType = `${mainStats[1]}_lesson` as ActionType;

    const sortedLessons = [...lessonWeeks].sort((a, b) => a.week - b.week);
    const beforeMid = sortedLessons.filter((w) => w.week < midExamWeek);
    const afterMid = sortedLessons.filter((w) => w.week > midExamWeek);

    // Before mid: alternate main1 / main2
    let toggle = false;
    for (const w of beforeMid) {
      const action = toggle ? main2Action : main1Action;
      const fallback = toggle ? main1Action : main2Action;
      if (w.available_actions.includes(action)) out[w.week] = action;
      else if (w.available_actions.includes(fallback)) out[w.week] = fallback;
      toggle = !toggle;
    }

    // After mid: 2:1 ratio (main1, main2, main1, main1, main2, main1...)
    let afterCount = 0;
    for (const w of afterMid) {
      const action = (afterCount % 3 === 1) ? main2Action : main1Action;
      const fallback = action === main2Action ? main1Action : main2Action;
      if (w.available_actions.includes(action)) out[w.week] = action;
      else if (w.available_actions.includes(fallback)) out[w.week] = fallback;
      afterCount++;
    }
  } else if (mainStats.length === 1) {
    const onlyAction: ActionType = `${mainStats[0]}_lesson` as ActionType;
    for (const w of lessonWeeks) {
      if (w.available_actions.includes(onlyAction)) out[w.week] = onlyAction;
    }
  }

  return out;
}

/** TurnChoice 配列から mainStats を自動推論（レッスン日の出現数 desc 上位2属性、vo>da>vi タイブレーク）。 */
function inferMainStats(turnChoices: TurnChoice[]): [string, string] {
  const counts: Record<string, number> = { vo: 0, da: 0, vi: 0 };
  for (const tc of turnChoices) {
    const a = tc.chosen_action as string;
    if (a === 'vo_lesson') counts.vo++;
    else if (a === 'da_lesson') counts.da++;
    else if (a === 'vi_lesson') counts.vi++;
  }
  const order: Array<'vo' | 'da' | 'vi'> = ['vo', 'da', 'vi'];
  const sorted = order
    .map((s) => ({ s, c: counts[s] }))
    .sort((a, b) => (b.c - a.c) || (order.indexOf(a.s) - order.indexOf(b.s)));
  return [sorted[0].s, sorted[1].s];
}

/**
 * ユーザの日程選択（scheduleChoices[planId]）から計算エンジンに渡す TurnChoice[] を構築。
 * 固定イベント(fixed_event/exam/audition)と選択肢なしの週はスキップ。
 */
function buildTurnChoicesFromSchedule(
  plan: TrainingPlan,
  sched: Record<number, ScheduleChoice>,
): TurnChoice[] {
  const turnChoices: TurnChoice[] = [];
  for (const w of plan.schedule) {
    if (w.type === 'audition' || w.type === 'fixed_event' || w.type === 'exam') continue;
    if (w.available_actions.length === 0) continue;
    const choice = sched[w.week];
    if (!choice) continue;
    turnChoices.push({ week: w.week, chosen_action: choice.action });
  }
  return turnChoices;
}

/**
 * N.I.Aオーディション1週ぶんの獲得ステータスを、キャラの審査基準・流行から算出。
 * 1種別クリアで「流行1値→流行1属性 / 流行2値→流行2属性 / 流行3値→流行3属性」を同時加算。
 * キャラ未選択・流行データ無し・種別データ無しなら null（＝獲得0）。
 */
export function computeNiaAuditionGain(
  week: WeekSchedule,
  character: Character | null | undefined,
  tierName?: string,
): StatusValues | null {
  const tiers = week.nia_audition_tiers;
  if (!tiers || tiers.length === 0) return null;
  if (!character || !character.nia_trend || character.nia_trend.length < 3) return null;
  const tier = tiers.find((t) => t.name === (tierName ?? tiers[0].name)) ?? tiers[0];
  const amounts = character.nia_criteria === 'concentrate' ? tier.concentrate : tier.balance;
  const ranks = [amounts.t1, amounts.t2, amounts.t3];
  const gain: StatusValues = { vo: 0, da: 0, vi: 0 };
  for (let i = 0; i < 3; i++) {
    const attr = character.nia_trend[i];
    if (attr === 'vo' || attr === 'da' || attr === 'vi') gain[attr] += ranks[i];
  }
  return gain;
}

/**
 * N.I.Aのオーディション週へ、選択キャラ・種別から算出した status_gain を流し込んだプランを返す。
 * オーディション種別を持たない週（初レジェンド等）・キャラ未選択時は素のプランをそのまま返す。
 */
function buildNiaAuditionPlan(
  plan: TrainingPlan,
  character: Character | null | undefined,
  tierByWeek: Record<number, string>,
): TrainingPlan {
  let changed = false;
  const newSchedule = plan.schedule.map((w) => {
    if (!w.nia_audition_tiers || w.nia_audition_tiers.length === 0) return w;
    const gain = computeNiaAuditionGain(w, character, tierByWeek[w.week]);
    if (!gain) return w; // キャラ未選択/流行なし → 0 のまま
    changed = true;
    return { ...w, status_gain: gain };
  });
  return changed ? { ...plan, schedule: newSchedule } : plan;
}

function autoAssignTurnChoices(
  plan: TrainingPlan,
  mainStats: string[],
  template?: EventCountTemplate | null,
): TurnChoice[] {
  const subStat = ['vo', 'da', 'vi'].find((s) => !mainStats.includes(s)) ?? 'vi';
  const subClassAction: ActionType = `${subStat}_class` as ActionType;

  const choices: TurnChoice[] = [];

  // レッスン週: 配分は distributeLessons に集約（シード/一括設定と同一ソース）
  const lessonAssignment = distributeLessons(plan, mainStats);
  for (const [weekStr, action] of Object.entries(lessonAssignment)) {
    choices.push({ week: Number(weekStr), chosen_action: action });
  }

  // 非レッスン週を収集（固定イベントは TurnChoice 不要）
  const otherWeeks: { week: number; available_actions: string[]; type: string }[] = [];
  for (const week of plan.schedule) {
    const isFixed = week.type === 'fixed_event' || week.type === 'exam' || week.type === 'audition';
    if (isFixed) continue;
    const hasLesson = week.available_actions.some((a) =>
      a === 'vo_lesson' || a === 'da_lesson' || a === 'vi_lesson',
    );
    if (hasLesson) continue;
    otherWeeks.push({ week: week.week, available_actions: week.available_actions, type: week.type });
  }

  // Non-lesson weeks: template override > class (sub) > activity_supply > outing > consultation > special_training
  for (const w of otherWeeks) {
    const override = template?.week_actions?.[w.week];
    if (override && w.available_actions.includes(override)) {
      choices.push({ week: w.week, chosen_action: override });
      continue;
    }

    const hasClass = w.available_actions.some((a) =>
      a === 'vo_class' || a === 'da_class' || a === 'vi_class',
    );

    if (hasClass && w.available_actions.includes(subClassAction)) {
      choices.push({ week: w.week, chosen_action: subClassAction });
    } else if (w.available_actions.includes('activity_supply')) {
      choices.push({ week: w.week, chosen_action: 'activity_supply' });
    } else if (w.available_actions.includes('outing')) {
      choices.push({ week: w.week, chosen_action: 'outing' });
    } else if (w.available_actions.includes('consultation')) {
      choices.push({ week: w.week, chosen_action: 'consultation' });
    } else if (w.available_actions.includes('special_training')) {
      choices.push({ week: w.week, chosen_action: 'special_training' });
    } else if (hasClass) {
      // Sub class not available, pick a main class
      const mainClassAction: ActionType = `${mainStats[0] ?? 'vo'}_class` as ActionType;
      if (w.available_actions.includes(mainClassAction)) {
        choices.push({ week: w.week, chosen_action: mainClassAction });
      } else if (w.available_actions.length > 0) {
        choices.push({ week: w.week, chosen_action: w.available_actions[0] as ActionType });
      }
    } else if (w.available_actions.length > 0) {
      choices.push({ week: w.week, chosen_action: w.available_actions[0] as ActionType });
    }
  }

  return choices;
}

function applySelectedPatternImpl(
  state: CalcState,
  index: number,
): Partial<CalcState> {
  if (index < 0 || index >= state.deckResults.length) {
    return { selectedPatternIndex: index };
  }

  const { plans, templates } = useAppStore.getState();
  const plan = plans.find((p) => p.id === state.selectedPlanId);
  if (!plan) return { selectedPatternIndex: index, errorMessage: 'プランが見つかりません' };

  const pattern = state.deckResults[index];
  const mainStats = state._lastMainStats;

  // 日程方式のプランはユーザが明示した日程をそのまま使う（autoAssign で上書きしない）。
  // それ以外はロール由来の自動配分（選択中テンプレの week_actions を尊重）。
  let turnChoices: TurnChoice[];
  if (SCHEDULE_PLAN_IDS.has(plan.id)) {
    turnChoices = state._lastTurnChoices;
  } else {
    const template = state.selectedTemplateName
      ? templates.find((t) => t.name === state.selectedTemplateName && t.plan_id === plan.id) ?? null
      : null;
    turnChoices = autoAssignTurnChoices(plan, mainStats, template);
  }

  // Build uncap levels
  const { cards: allCards, inventory } = useAppStore.getState();
  const uncapLevels = buildUncapLevels(allCards, inventory, state.ownedOnly);

  // Rental cards are 4 uncap
  for (const cs of pattern.selected_cards) {
    if (cs.is_rental) {
      uncapLevels[cs.card.id] = 4;
    }
  }

  const selectedCards = pattern.selected_cards.map((cs) => cs.card);

  const character = state.selectedCharacterId
    ? useAppStore.getState().characters.find((c) => c.id === state.selectedCharacterId) ?? null
    : null;

  // 3凸（OFFで減算）・STEP4（ONで加算）のトグルを反映した一時オブジェクトを生成
  const effectiveChar = applyCharacterToggles(
    character,
    state.uncap3BonusEnabled,
    state.step4BonusEnabled,
  );

  const memoryBonuses = state.memoryBonuses;
  const hasAnyMemory = !isEmptyAllMemoryBonuses(memoryBonuses);

  // 日程方式(NIA)はキャラの審査基準・流行でオーディション獲得を動的付与する。
  // キャラ未選択や流行データ無しなら素のプラン（オーディション=0）。補正なし結果は素のプランを使う。
  const effPlan = SCHEDULE_PLAN_IDS.has(plan.id)
    ? buildNiaAuditionPlan(plan, character, state.niaAuditionTierByWeek)
    : plan;

  const result = calculate(
    effPlan,
    selectedCards,
    turnChoices,
    uncapLevels,
    state.additionalCounts,
    effectiveChar,
    memoryBonuses,
  );

  // キャラ補正・メモリー補正のいずれかが有効なら「補正なし結果」を別途算出し差分表示に使う
  const resultWithoutCharacter = (character || hasAnyMemory)
    ? calculate(plan, selectedCards, turnChoices, uncapLevels, state.additionalCounts, null, null)
    : null;

  return {
    selectedPatternIndex: index,
    calculationResult: result,
    calculationResultWithoutCharacter: resultWithoutCharacter,
    errorMessage: null,
  };
}

const initialSelectedCharacterId =
  typeof window !== 'undefined' ? localStorage.getItem(SELECTED_CHARACTER_KEY) : null;
const initialUncap3BonusByChar = loadBoolMap(UNCAP3_BONUS_MAP_KEY);
const initialStep4BonusByChar = loadBoolMap(STEP4_BONUS_MAP_KEY);

export const useCalcStore = create<CalcState>((set, get) => ({
  selectedPlanId: '',
  selectedPlanType: 'sense',
  voRole: 'サブ',
  daRole: 'サブ',
  viRole: 'サブ',
  voSpCount: 0,
  daSpCount: 0,
  viSpCount: 0,
  additionalCounts: emptyAdditionalCounts(),
  selectedTemplateName: null,
  ownedOnly: false,
  contestMode: false,
  requiredCardIds: [],
  excludedCardIds: [],
  selectedCharacterId: initialSelectedCharacterId,
  // キャラ毎マップから選択中キャラの値を導出。3凸=既定OFF / STEP4=既定ON
  uncap3BonusByChar: initialUncap3BonusByChar,
  step4BonusByChar: initialStep4BonusByChar,
  uncap3BonusEnabled: isUncap3EnabledFor(initialUncap3BonusByChar, initialSelectedCharacterId),
  step4BonusEnabled: isStep4EnabledFor(initialStep4BonusByChar, initialSelectedCharacterId),
  // 持ち込みメモリー (最大4枚) はセッション限定。localStorage には保存しない
  memoryBonuses: [emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()],
  // プリセットは localStorage に永続化（メモリー値自体とは別）
  memoryPresets: loadMemoryPresetsFromStorage(),
  eventCountPresets: loadEventCountPresetsFromStorage(),
  deckResults: [],
  selectedPatternIndex: 0,
  calculationResult: null,
  calculationResultWithoutCharacter: null,
  errorMessage: null,
  _lastMainStats: [],
  _lastLessonWeekCount: 0,
  _lastTurnChoices: [],

  scheduleChoices: {},
  scheduleBulkLessonStat: 'vo',
  scheduleBulkClassStat: 'vo',
  schedulePresetsByPlan: loadSchedulePresetsByPlan(),
  niaAuditionTierByWeek: {},

  setSelectedPlanId: (id) =>
    set({
      selectedPlanId: id,
      selectedTemplateName: null,
      deckResults: [],
      calculationResult: null,
      calculationResultWithoutCharacter: null,
      errorMessage: null,
      selectedPatternIndex: 0,
    }),

  setSelectedPlanType: (type) => set({ selectedPlanType: type }),

  setRole: (stat, role) => {
    switch (stat) {
      case 'vo':
        set({ voRole: role });
        break;
      case 'da':
        set({ daRole: role });
        break;
      case 'vi':
        set({ viRole: role });
        break;
    }
  },

  setSpCount: (stat, count) => {
    const val = Math.max(0, count);
    switch (stat) {
      case 'vo':
        set({ voSpCount: val });
        break;
      case 'da':
        set({ daSpCount: val });
        break;
      case 'vi':
        set({ viSpCount: val });
        break;
    }
  },

  setAdditionalCount: (key, value) => {
    const state = get();
    set({
      additionalCounts: {
        ...state.additionalCounts,
        [key]: Math.max(0, value),
      },
    });
  },

  applyTemplate: (template) => {
    const counts = emptyAdditionalCounts();
    for (const [key, value] of Object.entries(template.counts)) {
      if (key in counts) {
        (counts as Record<string, number>)[key] = value;
      }
    }

    // 日程方式: テンプレートの week_actions をスケジュールへ反映（活動支給軸/相談削除軸の切替）
    const state = get();
    const planId = state.selectedPlanId;
    const extra: Partial<CalcState> = {};
    if (SCHEDULE_PLAN_IDS.has(planId) && template.plan_id === planId && template.week_actions) {
      const plan = useAppStore.getState().plans.find((p) => p.id === planId);
      if (plan) {
        const next: Record<number, ScheduleChoice> = { ...(state.scheduleChoices[planId] ?? {}) };
        for (const [weekStr, action] of Object.entries(template.week_actions)) {
          const week = Number(weekStr);
          const w = plan.schedule.find((x) => x.week === week);
          if (w && w.available_actions.includes(action)) {
            next[week] = { action };
          }
        }
        extra.scheduleChoices = { ...state.scheduleChoices, [planId]: next };
        // 直後の再適用が新日程を使えるようスナップショットも更新
        extra._lastTurnChoices = buildTurnChoicesFromSchedule(plan, next);
      }
    }

    set({ additionalCounts: counts, selectedTemplateName: template.name, ...extra });

    // Re-apply pattern to refresh turn choices using the new template's week_actions
    const after = get();
    if (after.calculationResult && after.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(after, after.selectedPatternIndex);
      set(updates as Partial<CalcState>);
    }
  },

  setOwnedOnly: (v) => set({ ownedOnly: v }),
  setContestMode: (v) => set({ contestMode: v }),

  addRequiredCard: (cardId) => {
    const state = get();
    if (state.requiredCardIds.length >= 4) return;
    if (state.requiredCardIds.includes(cardId)) return;
    set({
      requiredCardIds: [...state.requiredCardIds, cardId],
      // 必須と除外は相互排他: 必須に追加したら除外から外す
      excludedCardIds: state.excludedCardIds.filter((id) => id !== cardId),
    });
  },

  removeRequiredCard: (cardId) => {
    const state = get();
    set({ requiredCardIds: state.requiredCardIds.filter((id) => id !== cardId) });
  },

  addExcludedCard: (cardId) => {
    const state = get();
    if (state.excludedCardIds.includes(cardId)) return;
    set({
      excludedCardIds: [...state.excludedCardIds, cardId],
      // 必須と除外は相互排他: 除外に追加したら必須から外す
      requiredCardIds: state.requiredCardIds.filter((id) => id !== cardId),
    });
  },

  removeExcludedCard: (cardId) => {
    const state = get();
    set({ excludedCardIds: state.excludedCardIds.filter((id) => id !== cardId) });
  },

  setSelectedCharacter: (id) => {
    if (id) localStorage.setItem(SELECTED_CHARACTER_KEY, id);
    else localStorage.removeItem(SELECTED_CHARACTER_KEY);

    const state = get();
    // 選択キャラに応じてトグルの導出値を切り替える（3凸=既定OFF / STEP4=既定ON）
    const uncap3BonusEnabled = isUncap3EnabledFor(state.uncap3BonusByChar, id);
    const step4BonusEnabled = isStep4EnabledFor(state.step4BonusByChar, id);
    set({ selectedCharacterId: id, uncap3BonusEnabled, step4BonusEnabled });

    // 計算済みなら現在の選択パターンで再計算
    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, selectedCharacterId: id, uncap3BonusEnabled, step4BonusEnabled },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  setUncap3BonusEnabled: (v) => {
    // 選択中キャラのトグルとして保持（3凸=既定OFF）
    const state = get();
    const id = state.selectedCharacterId;
    const map = { ...state.uncap3BonusByChar };
    if (id) {
      if (v) map[id] = true;
      else delete map[id]; // 既定OFFなのでOFFはマップから除去
      saveBoolMap(UNCAP3_BONUS_MAP_KEY, map);
    }
    set({ uncap3BonusEnabled: v, uncap3BonusByChar: map });

    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, uncap3BonusEnabled: v, uncap3BonusByChar: map },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  setStep4BonusEnabled: (v) => {
    // 選択中キャラのトグルとして保持（STEP4=既定ON）
    const state = get();
    const id = state.selectedCharacterId;
    const map = { ...state.step4BonusByChar };
    if (id) {
      if (v) delete map[id]; // 既定ONなのでONはマップから除去
      else map[id] = false;
      saveBoolMap(STEP4_BONUS_MAP_KEY, map);
    }
    set({ step4BonusEnabled: v, step4BonusByChar: map });

    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, step4BonusEnabled: v, step4BonusByChar: map },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  setMemoryBonus: (index, stat, patch) => {
    const state = get();
    if (index < 0 || index >= state.memoryBonuses.length) return;
    const newList = state.memoryBonuses.map((m, i) =>
      i === index ? { ...m, [stat]: { ...m[stat], ...patch } } : m,
    );
    set({ memoryBonuses: newList });

    // 既存の setSelectedCharacter / setUncap3BonusEnabled と同じ再計算パターン
    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, memoryBonuses: newList },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  clearMemoryBonuses: () => {
    const newList = [emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()];
    set({ memoryBonuses: newList });

    const state = get();
    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, memoryBonuses: newList },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  saveMemoryPreset: (name) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const state = get();
    // 4 要素を確保した独立コピーを作る（参照共有を避ける）
    const snapshot: MemoryBonus[] = state.memoryBonuses.slice(0, 4).map((m) => ({
      vo: { ...m.vo },
      da: { ...m.da },
      vi: { ...m.vi },
    }));
    while (snapshot.length < 4) snapshot.push(emptyMemoryBonus());

    const existing = state.memoryPresets.findIndex((p) => p.name === trimmed);
    let newPresets: MemoryPreset[];
    if (existing >= 0) {
      // 同名は上書き
      newPresets = state.memoryPresets.map((p, i) =>
        i === existing ? { name: trimmed, bonuses: snapshot } : p,
      );
    } else {
      if (state.memoryPresets.length >= MAX_MEMORY_PRESETS) return;
      newPresets = [...state.memoryPresets, { name: trimmed, bonuses: snapshot }];
    }
    persistMemoryPresets(newPresets);
    set({ memoryPresets: newPresets });
  },

  loadMemoryPreset: (name) => {
    const state = get();
    const preset = state.memoryPresets.find((p) => p.name === name);
    if (!preset) return;
    // 4 要素を確保（プリセット側が短い場合は空で埋める）
    const newList: MemoryBonus[] = [];
    for (let i = 0; i < 4; i++) {
      const src = preset.bonuses[i];
      newList.push(
        src
          ? { vo: { ...src.vo }, da: { ...src.da }, vi: { ...src.vi } }
          : emptyMemoryBonus(),
      );
    }
    set({ memoryBonuses: newList });

    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, memoryBonuses: newList },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  deleteMemoryPreset: (name) => {
    const state = get();
    const newPresets = state.memoryPresets.filter((p) => p.name !== name);
    if (newPresets.length === state.memoryPresets.length) return;
    persistMemoryPresets(newPresets);
    set({ memoryPresets: newPresets });
  },

  saveEventCountPreset: (name) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const state = get();
    // 現在の入力値の独立コピー（既知キーのみ保持）
    const snapshot = emptyAdditionalCounts();
    for (const key of Object.keys(snapshot)) {
      (snapshot as Record<string, number>)[key] =
        (state.additionalCounts as Record<string, number>)[key] ?? 0;
    }

    const existing = state.eventCountPresets.findIndex((p) => p.name === trimmed);
    let newPresets: EventCountPreset[];
    if (existing >= 0) {
      // 同名は上書き
      newPresets = state.eventCountPresets.map((p, i) =>
        i === existing ? { name: trimmed, counts: snapshot } : p,
      );
    } else {
      if (state.eventCountPresets.length >= MAX_EVENT_COUNT_PRESETS) return;
      newPresets = [...state.eventCountPresets, { name: trimmed, counts: snapshot }];
    }
    persistEventCountPresets(newPresets);
    set({ eventCountPresets: newPresets });
  },

  loadEventCountPreset: (name) => {
    const state = get();
    const preset = state.eventCountPresets.find((p) => p.name === name);
    if (!preset) return;
    // 既知キーのみ反映した独立コピーを作る
    const counts = emptyAdditionalCounts();
    for (const [key, value] of Object.entries(preset.counts)) {
      if (key in counts) {
        (counts as Record<string, number>)[key] = value;
      }
    }
    set({ additionalCounts: counts });

    // 既存テンプレートの week_actions は維持したまま、現在の選択パターンで再計算
    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, additionalCounts: counts },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  deleteEventCountPreset: (name) => {
    const state = get();
    const newPresets = state.eventCountPresets.filter((p) => p.name !== name);
    if (newPresets.length === state.eventCountPresets.length) return;
    persistEventCountPresets(newPresets);
    set({ eventCountPresets: newPresets });
  },

  setScheduleChoice: (planId, week, choice) => {
    const state = get();
    const existing = state.scheduleChoices[planId] ?? {};
    set({
      scheduleChoices: {
        ...state.scheduleChoices,
        [planId]: { ...existing, [week]: choice },
      },
    });
  },

  seedScheduleDefaults: (planId) => {
    const state = get();
    const { plans, templates } = useAppStore.getState();
    const plan = plans.find((p) => p.id === planId);
    if (!plan) return;
    const template = state.selectedTemplateName
      ? templates.find((t) => t.name === state.selectedTemplateName && t.plan_id === planId) ?? null
      : null;
    // 単一属性シード: 全レッスンを bulkLessonStat、非レッスン週は優先度デフォルト（メイン1/2廃止）
    const autoChoices = autoAssignTurnChoices(
      plan,
      [state.scheduleBulkLessonStat],
      template,
    );
    const existing = state.scheduleChoices[planId] ?? {};
    const next: Record<number, ScheduleChoice> = { ...existing };
    let changed = false;
    for (const tc of autoChoices) {
      // 未設定の週だけ埋める（ユーザ編集を上書きしない）
      if (next[tc.week] === undefined) {
        next[tc.week] = { action: tc.chosen_action };
        changed = true;
      }
    }
    if (changed) {
      set({ scheduleChoices: { ...state.scheduleChoices, [planId]: next } });
    }
  },

  setScheduleBulkLessonStat: (stat) => set({ scheduleBulkLessonStat: stat }),

  setScheduleBulkClassStat: (stat) => set({ scheduleBulkClassStat: stat }),

  applyScheduleBulkLesson: (planId) => {
    const state = get();
    const plan = useAppStore.getState().plans.find((p) => p.id === planId);
    if (!plan) return;
    // 全レッスン週に選択属性を適用（distributeLessons の単一メイン分岐＝全週その属性）
    const assignment = distributeLessons(plan, [state.scheduleBulkLessonStat]);
    const existing = state.scheduleChoices[planId] ?? {};
    const next: Record<number, ScheduleChoice> = { ...existing };
    for (const [weekStr, action] of Object.entries(assignment)) {
      next[Number(weekStr)] = { action };
    }
    set({ scheduleChoices: { ...state.scheduleChoices, [planId]: next } });
  },

  applyScheduleBulkClass: (planId) => {
    const state = get();
    const plan = useAppStore.getState().plans.find((p) => p.id === planId);
    if (!plan) return;
    const action = `${state.scheduleBulkClassStat}_class` as ActionType;
    const existing = state.scheduleChoices[planId] ?? {};
    const next: Record<number, ScheduleChoice> = { ...existing };
    for (const w of plan.schedule) {
      const acts = w.available_actions;
      // 授業を含む週 (休む等が混在する週もあるため some 判定)
      if (acts.some((a) => a.endsWith('_class')) && acts.includes(action)) {
        next[w.week] = { action };
      }
    }
    set({ scheduleChoices: { ...state.scheduleChoices, [planId]: next } });
  },

  saveSchedulePreset: (planId, name) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const state = get();
    const sched = state.scheduleChoices[planId] ?? {};
    if (Object.keys(sched).length === 0) return; // 空は保存しない
    const snapshot: Record<number, ScheduleChoice> = {};
    for (const [k, v] of Object.entries(sched)) snapshot[Number(k)] = { ...v };
    const planPresets = state.schedulePresetsByPlan[planId] ?? [];
    const idx = planPresets.findIndex((p) => p.name === trimmed);
    let nextPlanPresets: SchedulePreset[];
    if (idx >= 0) {
      nextPlanPresets = planPresets.map((p, i) =>
        i === idx ? { name: trimmed, scheduleChoices: snapshot } : p,
      );
    } else {
      if (planPresets.length >= MAX_SCHEDULE_PRESETS) return;
      nextPlanPresets = [...planPresets, { name: trimmed, scheduleChoices: snapshot }];
    }
    const nextMap = { ...state.schedulePresetsByPlan, [planId]: nextPlanPresets };
    persistSchedulePresetsByPlan(nextMap);
    set({ schedulePresetsByPlan: nextMap });
  },

  loadSchedulePreset: (planId, name) => {
    const state = get();
    const preset = (state.schedulePresetsByPlan[planId] ?? []).find((p) => p.name === name);
    if (!preset) return;
    const choices: Record<number, ScheduleChoice> = {};
    for (const [k, v] of Object.entries(preset.scheduleChoices)) choices[Number(k)] = { ...v };
    set({ scheduleChoices: { ...state.scheduleChoices, [planId]: choices } });
  },

  deleteSchedulePreset: (planId, name) => {
    const state = get();
    const planPresets = state.schedulePresetsByPlan[planId] ?? [];
    const next = planPresets.filter((p) => p.name !== name);
    if (next.length === planPresets.length) return;
    const nextMap = { ...state.schedulePresetsByPlan, [planId]: next };
    persistSchedulePresetsByPlan(nextMap);
    set({ schedulePresetsByPlan: nextMap });
  },

  setNiaAuditionTier: (week, tierName) => {
    const state = get();
    const next = { ...state.niaAuditionTierByWeek, [week]: tierName };
    set({ niaAuditionTierByWeek: next });
    // 結果が出ていれば、同じデッキのまま新種別でオーディション獲得を再計算して即反映
    if (state.calculationResult && state.deckResults.length > 0) {
      const updates = applySelectedPatternImpl(
        { ...state, niaAuditionTierByWeek: next },
        state.selectedPatternIndex,
      );
      set(updates as Partial<CalcState>);
    }
  },

  executeCalculate: () => {
    try {
      const state = get();
      const { cards: allCards, plans, inventory } = useAppStore.getState();

      const plan = plans.find((p) => p.id === state.selectedPlanId);
      if (!plan) {
        set({ errorMessage: '育成プランを選択してください' });
        trackEvent('calculation_error', { error_message: '育成プランを選択してください' });
        return;
      }

      // ===== 日程方式 (初レジェンド / NIA): ユーザの日程を主入力にして HIF スコアラーで選出 =====
      if (SCHEDULE_PLAN_IDS.has(plan.id)) {
        const sched = state.scheduleChoices[plan.id] ?? {};
        const turnChoices = buildTurnChoicesFromSchedule(plan, sched);
        if (turnChoices.length === 0) {
          set({ errorMessage: 'スケジュールが未設定です' });
          trackEvent('calculation_error', { error_message: 'スケジュールが未設定です' });
          return;
        }

        // 休むはプロデュース中4回まで (初レジェンド仕様)
        const restCount = turnChoices.filter((tc) => tc.chosen_action === 'rest').length;
        if (restCount > 4) {
          set({ errorMessage: `休むはプロデュース中4回までです（現在 ${restCount} 回）` });
          trackEvent('calculation_error', { error_message: '休む回数超過' });
          return;
        }

        const [main1, main2] = inferMainStats(turnChoices);
        const mainStats: string[] = [main1, main2];

        const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
        for (const tc of turnChoices) {
          if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
          else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
          else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
        }

        const spCounts: Record<string, number> = {};
        if (state.voSpCount > 0) spCounts['vo'] = state.voSpCount;
        if (state.daSpCount > 0) spCounts['da'] = state.daSpCount;
        if (state.viSpCount > 0) spCounts['vi'] = state.viSpCount;

        const prepared = prepareCardSelectionContext(state, allCards, inventory);
        if (prepared.error || !prepared.ctx) {
          set({ errorMessage: prepared.error ?? '候補カードの構築に失敗しました' });
          trackEvent('calculation_error', { error_message: prepared.error ?? 'card context' });
          return;
        }
        const { candidateCards, uncapLevels, rentalPool, requiredCardIds } = prepared.ctx;

        // キャラ補正・メモリーを選出にも渡し、表示結果と一致させる
        const character = state.selectedCharacterId
          ? useAppStore.getState().characters.find((c) => c.id === state.selectedCharacterId) ?? null
          : null;
        const effectiveChar = applyCharacterToggles(
          character,
          state.uncap3BonusEnabled,
          state.step4BonusEnabled,
        );

        // NIA: キャラの審査基準・流行でオーディション獲得を付与した有効プラン（未選択/流行なしは素のプラン＝0）
        const effPlan = buildNiaAuditionPlan(plan, character, state.niaAuditionTierByWeek);

        startTimer('calculation');

        const patterns = selectMultiplePatternsHif(
          effPlan,
          candidateCards,
          mainStats,
          lessonAllocation,
          spCounts,
          state.selectedPlanType,
          state.additionalCounts,
          uncapLevels,
          rentalPool,
          requiredCardIds,
          effectiveChar,
          state.memoryBonuses,
          turnChoices,
          undefined, // overflow罰則は HIF 専用 (これらのプランでは未使用)
        );

        // 選出はキャラ補正込みのキャップ後合計で比較 (total_value はキャラ非考慮のため)
        const cap = plan.status_limit;
        let bestIndex = 0;
        let bestTotal = -Infinity;
        for (let i = 0; i < patterns.length; i++) {
          const p = patterns[i];
          const cards = p.selected_cards.map((cs) => cs.card);
          const uc: Record<string, number> = { ...uncapLevels };
          for (const cs of p.selected_cards) {
            if (cs.is_rental) uc[cs.card.id] = 4;
          }
          const fs = calculate(
            effPlan,
            cards,
            turnChoices,
            uc,
            state.additionalCounts,
            effectiveChar,
            state.memoryBonuses,
          ).final_status;
          const cappedTotal = Math.min(fs.vo, cap) + Math.min(fs.da, cap) + Math.min(fs.vi, cap);
          p.total_value = cappedTotal;
          if (cappedTotal > bestTotal) {
            bestTotal = cappedTotal;
            bestIndex = i;
          }
        }

        if (patterns.length === 0) {
          set({ errorMessage: '有効な編成パターンが見つかりませんでした' });
          trackEvent('calculation_error', { error_message: '有効な編成パターンが見つかりませんでした' });
          return;
        }

        const calcTimeMs = endTimer('calculation');
        const sessionCalcCount = incrementCounter('calculation');

        set({
          deckResults: patterns,
          _lastMainStats: mainStats,
          _lastTurnChoices: turnChoices,
          errorMessage: null,
        });
        const updates = applySelectedPatternImpl(
          { ...get(), deckResults: patterns, _lastMainStats: mainStats, _lastTurnChoices: turnChoices },
          bestIndex,
        );
        set(updates as Partial<CalcState>);

        const finalResult = updates.calculationResult;
        trackEvent('calculation_executed', {
          plan_id: plan.id,
          plan_type: state.selectedPlanType,
          main_stats: mainStats.join(','),
          schedule_mode: true,
          schedule_filled: turnChoices.length,
          lesson_allocation: `${lessonAllocation.vo}/${lessonAllocation.da}/${lessonAllocation.vi}`,
          owned_only: state.ownedOnly,
          contest_mode: state.contestMode,
          patterns_count: patterns.length,
          calc_time_ms: calcTimeMs,
          session_calc_count: sessionCalcCount,
          result_total: finalResult
            ? finalResult.final_status.vo + finalResult.final_status.da + finalResult.final_status.vi
            : 0,
          best_pattern_label: patterns[bestIndex]?.label ?? '',
          candidate_cards_count: candidateCards.length,
        });
        trackFunnelStep('calculator', 3, 'calculation_done');
        return;
      }

      // Build mainStats
      const mainStats: string[] = [];
      if (state.voRole === 'メイン1') mainStats.push('vo');
      if (state.daRole === 'メイン1') mainStats.push('da');
      if (state.viRole === 'メイン1') mainStats.push('vi');
      if (state.voRole === 'メイン2') mainStats.push('vo');
      if (state.daRole === 'メイン2') mainStats.push('da');
      if (state.viRole === 'メイン2') mainStats.push('vi');

      // Find sub stat
      const subStat = ['vo', 'da', 'vi'].find((s) => !mainStats.includes(s));
      if (!subStat || mainStats.length !== 2) {
        set({ errorMessage: 'メイン1とメイン2に異なる属性を1つずつ設定してください' });
        trackEvent('calculation_error', { error_message: 'メイン1とメイン2に異なる属性を1つずつ設定してください' });
        return;
      }

      const lessonWeekCount = plan.schedule.filter((w) => (w.lessons?.length ?? 0) > 0).length;

      // SP counts
      const spCounts: Record<string, number> = {};
      if (state.voSpCount > 0) spCounts['vo'] = state.voSpCount;
      if (state.daSpCount > 0) spCounts['da'] = state.daSpCount;
      if (state.viSpCount > 0) spCounts['vi'] = state.viSpCount;

      // Candidate cards
      let candidateCards = getCandidateCards(allCards, inventory, state.ownedOnly, state.contestMode);
      const uncapLevels = buildUncapLevels(allCards, inventory, state.ownedOnly);

      // Rental pool: if ownedOnly, all cards are rental candidates (contest mode filter applied)
      let rentalPool: SupportCard[] | undefined;
      if (state.ownedOnly) {
        rentalPool = state.contestMode
          ? allCards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item')
          : allCards;
      }

      // 除外カードを候補・レンタルプールから除去（必須カードは相互排他のため除外集合に含まれない）
      if (state.excludedCardIds.length > 0) {
        const excludedSet = new Set(state.excludedCardIds);
        candidateCards = candidateCards.filter((c) => !excludedSet.has(c.id));
        if (rentalPool != null) {
          rentalPool = rentalPool.filter((c) => !excludedSet.has(c.id));
        }
      }

      // 必須カードはコンテストモード等のフィルタを回避して候補に含める
      const requiredCardIds = state.requiredCardIds.length > 0 ? state.requiredCardIds : undefined;
      if (requiredCardIds != null) {
        const requiredIdSet = new Set(requiredCardIds);
        const candidateIdSet = new Set(candidateCards.map((c) => c.id));

        if (state.ownedOnly) {
          // 所持済み必須カードを candidateCards に追加
          const ownedIdSet = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && ownedIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }

          // 全必須カードを rentalPool に追加（未所持必須カードの検索用）
          if (rentalPool != null) {
            const rentalIdSet = new Set(rentalPool.map((c) => c.id));
            for (const card of allCards) {
              if (requiredIdSet.has(card.id) && !rentalIdSet.has(card.id)) {
                rentalPool.push(card);
              }
            }
          }
        } else {
          // 全カード4凸モード: 必須カードを candidateCards に追加
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }
        }
      }

      // 必須カードバリデーション
      if (requiredCardIds != null && state.ownedOnly) {
        const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
        const notOwnedCount = requiredCardIds.filter((id) => !ownedIds.has(id)).length;
        if (notOwnedCount > 1) {
          set({ errorMessage: '未所持の必須カードは最大1枚です（レンタル枠使用）' });
          trackEvent('calculation_error', { error_message: '未所持の必須カードは最大1枚です' });
          return;
        }
      }

      startTimer('calculation');

      const patterns = selectMultiplePatterns(
        plan,
        candidateCards,
        mainStats,
        subStat,
        lessonWeekCount,
        spCounts,
        state.selectedPlanType,
        state.additionalCounts,
        uncapLevels,
        rentalPool,
        requiredCardIds,
      );

      // Find best pattern
      let bestIndex = 0;
      let bestTotal = -Infinity;
      for (let i = 0; i < patterns.length; i++) {
        if (patterns[i].total_value > bestTotal) {
          bestTotal = patterns[i].total_value;
          bestIndex = i;
        }
      }

      set({
        deckResults: patterns,
        _lastMainStats: mainStats,
        _lastLessonWeekCount: lessonWeekCount,
        errorMessage: null,
      });

      // Apply best pattern
      if (patterns.length > 0) {
        const calcTimeMs = endTimer('calculation');
        const sessionCalcCount = incrementCounter('calculation');

        const updates = applySelectedPatternImpl(
          { ...get(), deckResults: patterns, _lastMainStats: mainStats, _lastLessonWeekCount: lessonWeekCount },
          bestIndex,
        );
        set(updates as Partial<CalcState>);

        // 計算結果の詳細トラッキング
        const finalResult = updates.calculationResult;
        trackEvent('calculation_executed', {
          plan_id: state.selectedPlanId,
          plan_type: state.selectedPlanType,
          main_stats: mainStats.join(','),
          sub_stat: subStat,
          owned_only: state.ownedOnly,
          contest_mode: state.contestMode,
          patterns_count: patterns.length,
          calc_time_ms: calcTimeMs,
          session_calc_count: sessionCalcCount,
          result_vo: finalResult?.final_status.vo ?? 0,
          result_da: finalResult?.final_status.da ?? 0,
          result_vi: finalResult?.final_status.vi ?? 0,
          result_total: finalResult
            ? finalResult.final_status.vo + finalResult.final_status.da + finalResult.final_status.vi
            : 0,
          best_pattern_label: patterns[bestIndex]?.label ?? '',
          candidate_cards_count: candidateCards.length,
        });
        trackFunnelStep('calculator', 3, 'calculation_done');
      } else {
        set({ errorMessage: '有効な編成パターンが見つかりませんでした' });
        trackEvent('calculation_error', { error_message: '有効な編成パターンが見つかりませんでした' });
      }
    } catch (e) {
      set({ errorMessage: `計算エラー: ${(e as Error).message}` });
      trackEvent('calculation_error', { error_message: (e as Error).message });
    }
  },

  selectPattern: (index) => {
    const state = get();
    const pattern = state.deckResults[index];
    if (pattern) {
      trackEvent('pattern_selected', {
        pattern_index: index,
        pattern_label: pattern.label,
        pattern_total_value: pattern.total_value,
      });
    }
    const updates = applySelectedPatternImpl(state, index);
    set(updates as Partial<CalcState>);
  },
}));
