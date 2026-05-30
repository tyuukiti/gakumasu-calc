import { create } from 'zustand';
import type { ActionType } from '../types/enums';
import type { CalculationResult, DeckResult } from '../types/results';
import type {
  TrainingPlan,
  TurnChoice,
  WeekSchedule,
  SupportCard,
  StatusValues,
} from '../types/models';
import { isEmptyAllMemoryBonuses } from '../types/models';
import { useAppStore } from './appStore';
import { useCalcStore } from './calcStore';
import { selectMultiplePatternsHif } from '../services/cardScoring';
import { calculate } from '../services/statusCalculation';
import { trackEvent } from '../utils/analytics';
import {
  type HifBonusLevels,
  defaultHifBonusLevels,
  getVoFlatBonus, getDaFlatBonus, getViFlatBonus,
  getVoParaBonus, getDaParaBonus, getViParaBonus,
  getFinalCapBonus,
} from '../types/hifBonus';

/**
 * HIFモードでユーザが各日に行う選択。
 * - 公開レッスン日: メイン属性 (action) とサブ属性 (sub_stat) を両方持つ
 * - 授業日: action のみ (vo_class / da_class / vi_class)
 * - その他選択日: action のみ (outing / consultation / activity_supply / special_training)
 * - 固定日 (audition): scheduleChoices に登録されない
 */
export type HifChoice =
  | { action: 'vo_lesson' | 'da_lesson' | 'vi_lesson'; sub_stat: 'vo' | 'da' | 'vi' }
  | { action: 'vo_class' | 'da_class' | 'vi_class' }
  | { action: 'outing' | 'consultation' | 'activity_supply' | 'special_training' };

/** 試験日の配分値振り分け（基礎値はYAMLから読み込み、配分値はユーザがVo/Da/Viに分配） */
export interface ExamAllocation {
  vo: number;
  da: number;
  vi: number;
}

/** 試験配分のプリセット種別 */
export type ExamAllocationPreset = 'vo_all' | 'da_all' | 'vi_all' | 'equal';

/** 公開レッスン日の一括デフォルト（メイン/サブ） */
export interface BulkLessonDefault {
  mainStat: 'vo' | 'da' | 'vi';
  subStat: 'vo' | 'da' | 'vi';
}

/** スケジュール調整のプリセット (個別調整した結果を保存・読み込み) */
export interface HifSchedulePreset {
  name: string;
  scheduleChoices: Record<number, HifChoice>;
  examAllocations: Record<number, ExamAllocation>;
}

const HIF_SCHEDULE_PRESETS_KEY = 'hifSchedulePresets';
/** プリセット保存可能件数上限 */
export const MAX_HIF_SCHEDULE_PRESETS = 10;

const HIF_BONUS_LEVELS_KEY = 'hifBonusLevels';
const HIF_OVERFLOW_PENALTY_KEY = 'hifOverflowPenalty';

/** overflow罰則の閾値の許容範囲 */
export const HIF_OVERFLOW_PENALTY_THRESHOLD_MIN = 50;
export const HIF_OVERFLOW_PENALTY_THRESHOLD_MAX = 500;
export const HIF_OVERFLOW_PENALTY_THRESHOLD_DEFAULT = 100;

interface HifOverflowPenaltySettings {
  enabled: boolean;
  threshold: number;
}

function defaultOverflowPenaltySettings(): HifOverflowPenaltySettings {
  return { enabled: false, threshold: HIF_OVERFLOW_PENALTY_THRESHOLD_DEFAULT };
}

function loadOverflowPenaltyFromStorage(): HifOverflowPenaltySettings {
  if (typeof window === 'undefined') return defaultOverflowPenaltySettings();
  try {
    const raw = localStorage.getItem(HIF_OVERFLOW_PENALTY_KEY);
    if (!raw) return defaultOverflowPenaltySettings();
    const parsed = JSON.parse(raw);
    const def = defaultOverflowPenaltySettings();
    const enabled = typeof parsed.enabled === 'boolean' ? parsed.enabled : def.enabled;
    const rawTh = Number(parsed.threshold);
    const threshold = Number.isFinite(rawTh)
      ? Math.min(HIF_OVERFLOW_PENALTY_THRESHOLD_MAX, Math.max(HIF_OVERFLOW_PENALTY_THRESHOLD_MIN, Math.floor(rawTh)))
      : def.threshold;
    return { enabled, threshold };
  } catch {
    return defaultOverflowPenaltySettings();
  }
}

function persistOverflowPenalty(s: HifOverflowPenaltySettings) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(HIF_OVERFLOW_PENALTY_KEY, JSON.stringify(s));
  } catch (e) {
    console.warn('HIF overflow罰則設定 保存失敗:', e);
  }
}

function loadBonusLevelsFromStorage(): HifBonusLevels {
  if (typeof window === 'undefined') return defaultHifBonusLevels();
  try {
    const raw = localStorage.getItem(HIF_BONUS_LEVELS_KEY);
    if (!raw) return defaultHifBonusLevels();
    const parsed = JSON.parse(raw);
    // フィールド不足はデフォルト補完
    return { ...defaultHifBonusLevels(), ...parsed };
  } catch {
    return defaultHifBonusLevels();
  }
}

function persistBonusLevels(levels: HifBonusLevels) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(HIF_BONUS_LEVELS_KEY, JSON.stringify(levels));
  } catch (e) {
    console.warn('HIFボーナスレベル保存失敗:', e);
  }
}

function loadSchedulePresetsFromStorage(): HifSchedulePreset[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = localStorage.getItem(HIF_SCHEDULE_PRESETS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as HifSchedulePreset[]) : [];
  } catch {
    return [];
  }
}

function persistSchedulePresets(presets: HifSchedulePreset[]) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(HIF_SCHEDULE_PRESETS_KEY, JSON.stringify(presets));
  } catch (e) {
    console.warn('HIFスケジュールプリセット保存失敗:', e);
  }
}

interface HifState {
  scheduleChoices: Record<number, HifChoice>;
  /** 試験日ごとのユーザ配分（基礎値はYAMLの hif_exam_base が別途加算される） */
  examAllocations: Record<number, ExamAllocation>;
  deckResults: DeckResult[];
  selectedPatternIndex: number;
  calculationResult: CalculationResult | null;
  calculationResultWithoutCharacter: CalculationResult | null;
  errorMessage: string | null;

  _lastMainStats: string[];
  _lastPlan: TrainingPlan | null;
  _lastTurnChoices: TurnChoice[];

  /** 一括設定で使う公開レッスンのデフォルトメイン/サブ */
  bulkLessonDefault: BulkLessonDefault;
  /** 一括設定で使う授業のデフォルト属性 */
  bulkClassStat: 'vo' | 'da' | 'vi';

  /** スケジュール調整のプリセット (localStorage 永続化) */
  schedulePresets: HifSchedulePreset[];

  /** HIFボーナス (パネル方式の永続強化) のレベル設定 */
  bonusLevels: HifBonusLevels;

  /** MAX大幅超過時の再抽選 (× 2 overflow罰則) */
  overflowPenalty: HifOverflowPenaltySettings;

  setScheduleChoice: (week: number, choice: HifChoice) => void;
  setExamAllocation: (week: number, stat: 'vo' | 'da' | 'vi', value: number) => void;
  setBulkLessonDefault: (def: BulkLessonDefault) => void;
  setBulkClassStat: (stat: 'vo' | 'da' | 'vi') => void;
  /** 全公開レッスン日に bulkLessonDefault を適用 */
  applyBulkLessonChoice: () => void;
  /** 全授業日に bulkClassStat を適用 */
  applyBulkClassChoice: () => void;
  /** 全試験日に配分プリセットを適用 */
  applyExamAllocationPreset: (preset: ExamAllocationPreset) => void;
  /** 現在のスケジュール選択をプリセットとして保存 (同名は上書き) */
  saveSchedulePreset: (name: string) => void;
  /** プリセットを読み込んで現在のスケジュールに反映 */
  loadSchedulePreset: (name: string) => void;
  /** プリセットを削除 */
  deleteSchedulePreset: (name: string) => void;
  /** HIFボーナスレベルを更新 (1パネル単位) */
  setBonusLevel: (key: keyof HifBonusLevels, level: number) => void;
  /** HIFボーナスレベルを一括リセット (全パネル MAX) */
  resetBonusLevels: () => void;
  /** overflow罰則オプションのON/OFF切替 */
  setOverflowPenaltyEnabled: (enabled: boolean) => void;
  /** overflow罰則の閾値を更新 */
  setOverflowPenaltyThreshold: (threshold: number) => void;
  resetScheduleChoices: () => void;
  executeCalculate: () => void;
  selectPattern: (index: number) => void;
}

/** TurnChoice 配列から mainStats を自動推論（レッスン日の出現数 desc 上位2属性） */
function inferMainStats(turnChoices: TurnChoice[]): [string, string] {
  const counts: Record<string, number> = { vo: 0, da: 0, vi: 0 };
  for (const tc of turnChoices) {
    const a = tc.chosen_action as string;
    if (a === 'vo_lesson') counts.vo++;
    else if (a === 'da_lesson') counts.da++;
    else if (a === 'vi_lesson') counts.vi++;
  }
  // タイブレーク: vo > da > vi
  const order: Array<'vo' | 'da' | 'vi'> = ['vo', 'da', 'vi'];
  const sorted = order
    .map((s) => ({ s, c: counts[s] }))
    .sort((a, b) => (b.c - a.c) || (order.indexOf(a.s) - order.indexOf(b.s)));
  return [sorted[0].s, sorted[1].s];
}

/**
 * HIFプラン + ユーザのスケジュール選択から、計算エンジンに渡す TrainingPlan と TurnChoice[] を構築。
 * 公開レッスン日は ユーザのメイン/サブ選択を sp_bonus に反映する（既存スキーマ流用）。
 */
function buildPlanAndChoices(
  hifPlan: TrainingPlan,
  choices: Record<number, HifChoice>,
  examAllocations: Record<number, ExamAllocation>,
): { plan: TrainingPlan; turnChoices: TurnChoice[] } {
  const newSchedule: WeekSchedule[] = hifPlan.schedule.map((w) => {
    if (w.type === 'public_lesson') {
      const choice = choices[w.week] as Extract<HifChoice, { sub_stat: 'vo' | 'da' | 'vi' }> | undefined;
      if (!choice) {
        return { ...w, lessons: [...w.lessons] };
      }
      const mainStat = choice.action.split('_')[0] as 'vo' | 'da' | 'vi';
      const subStat = choice.sub_stat;
      const mainValue =
        (w.lessons.find((l) => l.type === mainStat)?.sp_bonus[mainStat] ?? 0) as number;
      const subValue = w.hif_sub_value ?? 0;
      const newLessons = w.lessons.map((l) => {
        if (l.type !== mainStat) return l;
        const sp: StatusValues = { vo: 0, da: 0, vi: 0 };
        sp[mainStat] = mainValue;
        sp[subStat] = (sp[subStat] ?? 0) + subValue;
        return { ...l, sp_bonus: sp };
      });
      return { ...w, lessons: newLessons };
    }

    // 試験日: 基礎値(全属性同値) + ユーザ配分値 を status_gain に反映
    if (w.type === 'audition' && (w.hif_exam_base != null || w.hif_exam_distributed != null)) {
      const base = w.hif_exam_base ?? 0;
      const alloc = examAllocations[w.week] ?? { vo: 0, da: 0, vi: 0 };
      const status_gain: StatusValues = {
        vo: base + Math.max(0, Math.floor(alloc.vo)),
        da: base + Math.max(0, Math.floor(alloc.da)),
        vi: base + Math.max(0, Math.floor(alloc.vi)),
      };
      return { ...w, status_gain };
    }

    return w;
  });

  const newPlan: TrainingPlan = { ...hifPlan, schedule: newSchedule };

  const turnChoices: TurnChoice[] = [];
  for (const w of newSchedule) {
    if (w.type === 'audition' || w.type === 'fixed_event' || w.type === 'exam') continue;
    const choice = choices[w.week];
    if (!choice) continue;
    turnChoices.push({ week: w.week, chosen_action: choice.action as ActionType });
  }

  return { plan: newPlan, turnChoices };
}

function getCandidateCards(
  allCards: SupportCard[],
  ownedOnly: boolean,
  contestMode: boolean,
): SupportCard[] {
  const inventory = useAppStore.getState().inventory;
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

function buildUncapLevels(allCards: SupportCard[], ownedOnly: boolean): Record<string, number> {
  const inventory = useAppStore.getState().inventory;
  if (ownedOnly) {
    const levels: Record<string, number> = {};
    for (const e of inventory) levels[e.card_id] = e.uncap;
    return levels;
  }
  const levels: Record<string, number> = {};
  for (const c of allCards) levels[c.id] = 4;
  return levels;
}

/**
 * 通常のキャラに HIFボーナス (Vo/Da/Vi 上昇パネル) を合算した Character を返す。
 * デッキ選出と最終表示で同じキャラを使うために共通化。
 * HIFボーナスが全て0かつキャラ未選択なら null を返す。
 */
function buildHifEffectiveCharacter(
  bl: HifBonusLevels,
  selectedCharacterId: string | null,
  uncap3BonusEnabled: boolean,
): { character: ReturnType<typeof useAppStore.getState>['characters'][number] | null; hasAnyHifBonus: boolean } {
  const app = useAppStore.getState();
  const character = selectedCharacterId
    ? app.characters.find((c) => c.id === selectedCharacterId) ?? null
    : null;
  const effectiveCharBase =
    character && !uncap3BonusEnabled && character.uncap3_bonus
      ? {
          ...character,
          para_bonus: {
            vo: character.para_bonus.vo - character.uncap3_bonus.vo,
            da: character.para_bonus.da - character.uncap3_bonus.da,
            vi: character.para_bonus.vi - character.uncap3_bonus.vi,
          },
        }
      : character;

  const bonusVoFlat = getVoFlatBonus(bl.voUpLevel);
  const bonusDaFlat = getDaFlatBonus(bl.daUpLevel);
  const bonusViFlat = getViFlatBonus(bl.viUpLevel);
  const bonusVoPara = getVoParaBonus(bl.voUpLevel);
  const bonusDaPara = getDaParaBonus(bl.daUpLevel);
  const bonusViPara = getViParaBonus(bl.viUpLevel);
  const hasAnyHifBonus =
    bonusVoFlat > 0 || bonusDaFlat > 0 || bonusViFlat > 0 ||
    bonusVoPara > 0 || bonusDaPara > 0 || bonusViPara > 0;

  if (!hasAnyHifBonus) return { character: effectiveCharBase, hasAnyHifBonus };

  return {
    character: {
      id: effectiveCharBase?.id ?? '__hif_bonus__',
      name: effectiveCharBase?.name ?? 'HIF Bonus',
      color: effectiveCharBase?.color ?? '#000000',
      initial: effectiveCharBase?.initial ?? '',
      base_status_bonus: {
        vo: (effectiveCharBase?.base_status_bonus.vo ?? 0) + bonusVoFlat,
        da: (effectiveCharBase?.base_status_bonus.da ?? 0) + bonusDaFlat,
        vi: (effectiveCharBase?.base_status_bonus.vi ?? 0) + bonusViFlat,
      },
      para_bonus: {
        vo: (effectiveCharBase?.para_bonus.vo ?? 0) + bonusVoPara,
        da: (effectiveCharBase?.para_bonus.da ?? 0) + bonusDaPara,
        vi: (effectiveCharBase?.para_bonus.vi ?? 0) + bonusViPara,
      },
      uncap3_bonus: effectiveCharBase?.uncap3_bonus,
    },
    hasAnyHifBonus,
  };
}

function applySelectedPatternImpl(
  state: HifState,
  index: number,
): Partial<HifState> {
  if (index < 0 || index >= state.deckResults.length || !state._lastPlan) {
    return { selectedPatternIndex: index };
  }

  const pattern = state.deckResults[index];
  const calc = useCalcStore.getState();
  const app = useAppStore.getState();

  const uncapLevels = buildUncapLevels(app.cards, calc.ownedOnly);
  for (const cs of pattern.selected_cards) {
    if (cs.is_rental) uncapLevels[cs.card.id] = 4;
  }

  const selectedCards = pattern.selected_cards.map((cs) => cs.card);

  // HIFモード: デッキ選出時と同じ「HIFボーナス込みキャラ」で再計算しないと、
  // 選出が想定する補正値と表示値がズレて、Lv5よりLv0の方が高く出るなどの不整合になる
  const { character: effectiveChar, hasAnyHifBonus } = buildHifEffectiveCharacter(
    state.bonusLevels,
    calc.selectedCharacterId,
    calc.uncap3BonusEnabled,
  );

  const memoryBonuses = calc.memoryBonuses;
  const hasAnyMemory = !isEmptyAllMemoryBonuses(memoryBonuses);
  const hasSelectedCharacter = !!calc.selectedCharacterId;

  const result = calculate(
    state._lastPlan,
    selectedCards,
    state._lastTurnChoices,
    uncapLevels,
    calc.additionalCounts,
    effectiveChar,
    memoryBonuses,
  );

  const resultWithoutCharacter = (hasSelectedCharacter || hasAnyMemory || hasAnyHifBonus)
    ? calculate(state._lastPlan, selectedCards, state._lastTurnChoices, uncapLevels, calc.additionalCounts, null, null)
    : null;

  return {
    selectedPatternIndex: index,
    calculationResult: result,
    calculationResultWithoutCharacter: resultWithoutCharacter,
    errorMessage: null,
  };
}

export const useHifStore = create<HifState>((set, get) => ({
  scheduleChoices: {},
  examAllocations: {},
  bulkLessonDefault: { mainStat: 'vo', subStat: 'da' },
  bulkClassStat: 'vo',
  schedulePresets: loadSchedulePresetsFromStorage(),
  bonusLevels: loadBonusLevelsFromStorage(),
  overflowPenalty: loadOverflowPenaltyFromStorage(),
  deckResults: [],
  selectedPatternIndex: 0,
  calculationResult: null,
  calculationResultWithoutCharacter: null,
  errorMessage: null,
  _lastMainStats: [],
  _lastPlan: null,
  _lastTurnChoices: [],

  setScheduleChoice: (week, choice) =>
    set((s) => ({ scheduleChoices: { ...s.scheduleChoices, [week]: choice } })),

  setExamAllocation: (week, stat, value) =>
    set((s) => {
      const current = s.examAllocations[week] ?? { vo: 0, da: 0, vi: 0 };
      const next: ExamAllocation = { ...current, [stat]: Math.max(0, value) };
      return { examAllocations: { ...s.examAllocations, [week]: next } };
    }),

  setBulkLessonDefault: (def) => set({ bulkLessonDefault: def }),
  setBulkClassStat: (stat) => set({ bulkClassStat: stat }),

  applyBulkLessonChoice: () => {
    const { bulkLessonDefault } = get();
    if (bulkLessonDefault.mainStat === bulkLessonDefault.subStat) return;
    const hifPlan = useAppStore.getState().plans.find((p) => p.id === 'hif');
    if (!hifPlan) return;
    const action = `${bulkLessonDefault.mainStat}_lesson` as 'vo_lesson' | 'da_lesson' | 'vi_lesson';
    const newChoices: Record<number, HifChoice> = { ...get().scheduleChoices };
    for (const w of hifPlan.schedule) {
      if (w.type === 'public_lesson') {
        newChoices[w.week] = { action, sub_stat: bulkLessonDefault.subStat };
      }
    }
    set({ scheduleChoices: newChoices });
    trackEvent('hif_bulk_apply_used', {
      kind: 'lesson',
      main_stat: bulkLessonDefault.mainStat,
      sub_stat: bulkLessonDefault.subStat,
    });
  },

  applyBulkClassChoice: () => {
    const { bulkClassStat } = get();
    const hifPlan = useAppStore.getState().plans.find((p) => p.id === 'hif');
    if (!hifPlan) return;
    const action = `${bulkClassStat}_class` as 'vo_class' | 'da_class' | 'vi_class';
    const newChoices: Record<number, HifChoice> = { ...get().scheduleChoices };
    for (const w of hifPlan.schedule) {
      // 授業日: available_actions が全て _class で終わる週
      const acts = w.available_actions;
      if (acts.length > 0 && acts.every((a) => a.endsWith('_class')) && acts.includes(action)) {
        newChoices[w.week] = { action };
      }
    }
    set({ scheduleChoices: newChoices });
    trackEvent('hif_bulk_apply_used', { kind: 'class', stat: bulkClassStat });
  },

  applyExamAllocationPreset: (preset) => {
    const hifPlan = useAppStore.getState().plans.find((p) => p.id === 'hif');
    if (!hifPlan) return;
    const newAllocations: Record<number, ExamAllocation> = { ...get().examAllocations };
    for (const w of hifPlan.schedule) {
      const d = w.hif_exam_distributed ?? 0;
      if (w.type !== 'audition' || d <= 0) continue;
      if (preset === 'vo_all') newAllocations[w.week] = { vo: d, da: 0, vi: 0 };
      else if (preset === 'da_all') newAllocations[w.week] = { vo: 0, da: d, vi: 0 };
      else if (preset === 'vi_all') newAllocations[w.week] = { vo: 0, da: 0, vi: d };
      else if (preset === 'equal') {
        const q = Math.floor(d / 3);
        const r = d - q * 3;
        newAllocations[w.week] = { vo: q + r, da: q, vi: q };
      }
    }
    set({ examAllocations: newAllocations });
    trackEvent('hif_exam_preset_applied', { preset });
  },

  saveSchedulePreset: (name) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const state = get();
    // ディープコピー
    const snapshotChoices: Record<number, HifChoice> = {};
    for (const [k, v] of Object.entries(state.scheduleChoices)) {
      snapshotChoices[Number(k)] = { ...v } as HifChoice;
    }
    const snapshotAllocations: Record<number, ExamAllocation> = {};
    for (const [k, v] of Object.entries(state.examAllocations)) {
      snapshotAllocations[Number(k)] = { ...v };
    }
    const existingIndex = state.schedulePresets.findIndex((p) => p.name === trimmed);
    let next: HifSchedulePreset[];
    if (existingIndex >= 0) {
      next = state.schedulePresets.map((p, i) =>
        i === existingIndex
          ? { name: trimmed, scheduleChoices: snapshotChoices, examAllocations: snapshotAllocations }
          : p,
      );
    } else {
      if (state.schedulePresets.length >= MAX_HIF_SCHEDULE_PRESETS) return;
      next = [
        ...state.schedulePresets,
        { name: trimmed, scheduleChoices: snapshotChoices, examAllocations: snapshotAllocations },
      ];
    }
    persistSchedulePresets(next);
    set({ schedulePresets: next });
    trackEvent('hif_schedule_preset_saved', { preset_count: next.length });
  },

  loadSchedulePreset: (name) => {
    const state = get();
    const preset = state.schedulePresets.find((p) => p.name === name);
    if (!preset) return;
    // ディープコピーして反映
    const choices: Record<number, HifChoice> = {};
    for (const [k, v] of Object.entries(preset.scheduleChoices)) {
      choices[Number(k)] = { ...v } as HifChoice;
    }
    const allocs: Record<number, ExamAllocation> = {};
    for (const [k, v] of Object.entries(preset.examAllocations)) {
      allocs[Number(k)] = { ...v };
    }
    set({ scheduleChoices: choices, examAllocations: allocs });
    trackEvent('hif_schedule_preset_loaded');
  },

  deleteSchedulePreset: (name) => {
    const state = get();
    const next = state.schedulePresets.filter((p) => p.name !== name);
    if (next.length === state.schedulePresets.length) return;
    persistSchedulePresets(next);
    set({ schedulePresets: next });
    trackEvent('hif_schedule_preset_deleted');
  },

  setBonusLevel: (key, level) => {
    const state = get();
    const next = { ...state.bonusLevels, [key]: Math.max(0, level) };
    persistBonusLevels(next);
    set({ bonusLevels: next });
  },

  resetBonusLevels: () => {
    const next = defaultHifBonusLevels();
    persistBonusLevels(next);
    set({ bonusLevels: next });
  },

  setOverflowPenaltyEnabled: (enabled) => {
    const state = get();
    const next = { ...state.overflowPenalty, enabled };
    persistOverflowPenalty(next);
    set({ overflowPenalty: next });
  },

  setOverflowPenaltyThreshold: (threshold) => {
    const state = get();
    const clamped = Math.min(
      HIF_OVERFLOW_PENALTY_THRESHOLD_MAX,
      Math.max(HIF_OVERFLOW_PENALTY_THRESHOLD_MIN, Math.floor(threshold)),
    );
    const next = { ...state.overflowPenalty, threshold: clamped };
    persistOverflowPenalty(next);
    set({ overflowPenalty: next });
  },

  resetScheduleChoices: () => set({ scheduleChoices: {}, examAllocations: {} }),

  executeCalculate: () => {
    try {
      const state = get();
      const { plans, cards: allCards, inventory } = useAppStore.getState();
      const calc = useCalcStore.getState();

      const hifPlan = plans.find((p) => p.id === 'hif');
      if (!hifPlan) {
        set({ errorMessage: 'HIFプランが読み込まれていません' });
        return;
      }

      // 動的TrainingPlan + TurnChoice 構築
      // eslint-disable-next-line prefer-const
      let { plan, turnChoices } = buildPlanAndChoices(hifPlan, state.scheduleChoices, state.examAllocations);

      if (turnChoices.length === 0) {
        set({ errorMessage: 'スケジュールが未設定です' });
        return;
      }

      // mainStats 自動推論 (PostOptimize 用、HIFパターンでも保護対象計算に使う)
      const [main1, main2] = inferMainStats(turnChoices);
      const mainStats: string[] = [main1, main2];

      // ユーザのスケジュールから実際のレッスン配分を集計
      const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
      for (const tc of turnChoices) {
        if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
        else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
        else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
      }

      const spCounts: Record<string, number> = {};
      if (calc.voSpCount > 0) spCounts['vo'] = calc.voSpCount;
      if (calc.daSpCount > 0) spCounts['da'] = calc.daSpCount;
      if (calc.viSpCount > 0) spCounts['vi'] = calc.viSpCount;

      const candidateCards = getCandidateCards(allCards, calc.ownedOnly, calc.contestMode);
      const uncapLevels = buildUncapLevels(allCards, calc.ownedOnly);

      let rentalPool: SupportCard[] | undefined;
      if (calc.ownedOnly) {
        rentalPool = calc.contestMode
          ? allCards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item')
          : allCards;
      }

      const requiredCardIds = calc.requiredCardIds.length > 0 ? calc.requiredCardIds : undefined;
      if (requiredCardIds != null) {
        const requiredIdSet = new Set(requiredCardIds);
        const candidateIdSet = new Set(candidateCards.map((c) => c.id));

        if (calc.ownedOnly) {
          const ownedIdSet = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && ownedIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }
          if (rentalPool != null) {
            const rentalIdSet = new Set(rentalPool.map((c) => c.id));
            for (const card of allCards) {
              if (requiredIdSet.has(card.id) && !rentalIdSet.has(card.id)) {
                rentalPool.push(card);
              }
            }
          }
        } else {
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }
        }
      }

      if (requiredCardIds != null && calc.ownedOnly) {
        const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
        const notOwnedCount = requiredCardIds.filter((id) => !ownedIds.has(id)).length;
        if (notOwnedCount > 1) {
          set({ errorMessage: '未所持の必須カードは最大1枚です（レンタル枠使用）' });
          return;
        }
      }

      // 計算結果を最終表示と一致させるため、キャラ補正・メモリーをパターン選出にも渡す
      const character = calc.selectedCharacterId
        ? useAppStore.getState().characters.find((c) => c.id === calc.selectedCharacterId) ?? null
        : null;
      const effectiveCharBase =
        character && !calc.uncap3BonusEnabled && character.uncap3_bonus
          ? {
              ...character,
              para_bonus: {
                vo: character.para_bonus.vo - character.uncap3_bonus.vo,
                da: character.para_bonus.da - character.uncap3_bonus.da,
                vi: character.para_bonus.vi - character.uncap3_bonus.vi,
              },
            }
          : character;

      // HIFボーナス (Vo/Da/Vi 上昇パネル) をキャラ補正に合算
      const bl = state.bonusLevels;
      const bonusVoFlat = getVoFlatBonus(bl.voUpLevel);
      const bonusDaFlat = getDaFlatBonus(bl.daUpLevel);
      const bonusViFlat = getViFlatBonus(bl.viUpLevel);
      const bonusVoPara = getVoParaBonus(bl.voUpLevel);
      const bonusDaPara = getDaParaBonus(bl.daUpLevel);
      const bonusViPara = getViParaBonus(bl.viUpLevel);
      // 本戦上限増加で plan.status_limit に加算 (v1 簡略: 全期間に同じ cap を適用)
      const finalCapBonus = getFinalCapBonus(bl.finalStatLimitLevel);

      const effectiveChar = (effectiveCharBase || bonusVoFlat || bonusDaFlat || bonusViFlat
                              || bonusVoPara || bonusDaPara || bonusViPara)
        ? {
            id: effectiveCharBase?.id ?? '__hif_bonus__',
            name: effectiveCharBase?.name ?? 'HIF Bonus',
            color: effectiveCharBase?.color ?? '#000000',
            initial: effectiveCharBase?.initial ?? '',
            base_status_bonus: {
              vo: (effectiveCharBase?.base_status_bonus.vo ?? 0) + bonusVoFlat,
              da: (effectiveCharBase?.base_status_bonus.da ?? 0) + bonusDaFlat,
              vi: (effectiveCharBase?.base_status_bonus.vi ?? 0) + bonusViFlat,
            },
            para_bonus: {
              vo: (effectiveCharBase?.para_bonus.vo ?? 0) + bonusVoPara,
              da: (effectiveCharBase?.para_bonus.da ?? 0) + bonusDaPara,
              vi: (effectiveCharBase?.para_bonus.vi ?? 0) + bonusViPara,
            },
            uncap3_bonus: effectiveCharBase?.uncap3_bonus,
          }
        : null;

      // status_limit に本戦上限増加を加算した動的プランを使用
      if (finalCapBonus > 0) {
        plan = { ...plan, status_limit: plan.status_limit + finalCapBonus };
      }

      // MAX大幅超過時の再抽選オプション (ON のときだけ × 2 overflow罰則を有効化)
      const overflowPenaltyConfig = state.overflowPenalty.enabled
        ? { threshold: state.overflowPenalty.threshold }
        : undefined;

      const patterns = selectMultiplePatternsHif(
        plan,
        candidateCards,
        mainStats,
        lessonAllocation,
        spCounts,
        calc.selectedPlanType,
        calc.additionalCounts,
        uncapLevels,
        rentalPool,
        requiredCardIds,
        effectiveChar,
        calc.memoryBonuses,
        turnChoices, // HIF はユーザが明示的に選んだターン選択を postOptimize でも使う
        overflowPenaltyConfig,
      );

      // パターン選出はキャラ補正込みのキャップ後合計で比較
      // (total_value はキャラなしのカード寄与合計で、キャラの偏りを反映しないため)
      const cap = plan.status_limit;
      let bestIndex = 0;
      let bestEffectiveTotal = -Infinity;
      for (let i = 0; i < patterns.length; i++) {
        const p = patterns[i];
        const cards = p.selected_cards.map((cs) => cs.card);
        const uc: Record<string, number> = { ...uncapLevels };
        for (const cs of p.selected_cards) {
          if (cs.is_rental) uc[cs.card.id] = 4;
        }
        const fs = calculate(
          plan,
          cards,
          turnChoices,
          uc,
          calc.additionalCounts,
          effectiveChar,
          calc.memoryBonuses,
        ).final_status;
        const cappedTotal = Math.min(fs.vo, cap) + Math.min(fs.da, cap) + Math.min(fs.vi, cap);
        // ベースのフォールバック表示には total_value も更新しておく (cap 後合計)
        p.total_value = cappedTotal;
        // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則をパターン選択にも適用
        let effectiveScore = cappedTotal;
        if (overflowPenaltyConfig) {
          const overflow = Math.max(0, fs.vo - cap) + Math.max(0, fs.da - cap) + Math.max(0, fs.vi - cap);
          if (overflow > overflowPenaltyConfig.threshold) {
            effectiveScore -= overflow * 2;
          }
        }
        if (effectiveScore > bestEffectiveTotal) {
          bestEffectiveTotal = effectiveScore;
          bestIndex = i;
        }
      }

      set({
        deckResults: patterns,
        _lastMainStats: mainStats,
        _lastPlan: plan,
        _lastTurnChoices: turnChoices,
        errorMessage: null,
      });

      if (patterns.length > 0) {
        const updates = applySelectedPatternImpl(
          {
            ...get(),
            deckResults: patterns,
            _lastMainStats: mainStats,
            _lastPlan: plan,
            _lastTurnChoices: turnChoices,
          },
          bestIndex,
        );
        set(updates as Partial<HifState>);

        trackEvent('hif_calculation_executed', {
          main_stats: mainStats.join(','),
          patterns_count: patterns.length,
          schedule_filled: turnChoices.length,
          lesson_allocation: `${lessonAllocation.vo}/${lessonAllocation.da}/${lessonAllocation.vi}`,
          // HIFボーナス Lv（パネルの利用度合いを把握）
          bonus_vo_up_lv: bl.voUpLevel,
          bonus_da_up_lv: bl.daUpLevel,
          bonus_vi_up_lv: bl.viUpLevel,
          bonus_final_cap_lv: bl.finalStatLimitLevel,
          bonus_total_lv: bl.voUpLevel + bl.daUpLevel + bl.viUpLevel,
          // キャラ・メモリー併用の有無
          has_character: !!calc.selectedCharacterId,
          owned_only: calc.ownedOnly,
          contest_mode: calc.contestMode,
        });
      } else {
        set({ errorMessage: '有効な編成パターンが見つかりませんでした' });
      }
    } catch (e) {
      set({ errorMessage: `計算エラー: ${(e as Error).message}` });
    }
  },

  selectPattern: (index) => {
    const state = get();
    const updates = applySelectedPatternImpl(state, index);
    set(updates as Partial<HifState>);
    const pattern = state.deckResults[index];
    if (pattern) {
      trackEvent('hif_pattern_selected', {
        pattern_label: pattern.label,
        pattern_index: index,
      });
    }
  },
}));

export function getActionCategory(action: ActionType): 'lesson' | 'class' | 'other' {
  if (action === 'vo_lesson' || action === 'da_lesson' || action === 'vi_lesson') return 'lesson';
  if (action === 'vo_class' || action === 'da_class' || action === 'vi_class') return 'class';
  return 'other';
}

export function lessonActionToStat(action: 'vo_lesson' | 'da_lesson' | 'vi_lesson'): 'vo' | 'da' | 'vi' {
  return action.split('_')[0] as 'vo' | 'da' | 'vi';
}
