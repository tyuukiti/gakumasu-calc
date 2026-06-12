import type { ActionType } from './enums';

// --- StatusValues ---

export interface StatusValues {
  vo: number;
  da: number;
  vi: number;
}

// --- SupportCard ---

export interface SupportCardFile {
  support_cards: SupportCard[];
}

export interface SupportCard {
  id: string;
  name: string;
  rarity: string;
  type: string;
  plan: string;
  tag?: string;
  effects: CardEffect[];
}

export interface CardEffect {
  trigger: string;
  stat: string;
  values: number[];
  value_type: string;
  max_count?: number;
  condition?: string;
  description?: string;
  source?: string;
  event_param?: boolean;
  /** value_type: 'trigger_count_bonus' 用 — 加算対象のトリガー名 (例: 'p_drink_acquire') */
  trigger_target?: string;
  /** value_type: 'trigger_count_bonus' 用 — スケール元のトリガー名 (例: 'da_sp_end'). 省略時は絶対値扱い */
  scales_with?: string;
}

export interface LessonBonusPercent {
  vo_percent: number;
  da_percent: number;
  vi_percent: number;
}

// --- TrainingPlan ---

export interface TrainingPlanFile {
  plan: TrainingPlan;
}

export interface TrainingPlan {
  id: string;
  name: string;
  description: string;
  total_weeks: number;
  status_limit: number;
  base_status: StatusValues;
  schedule: WeekSchedule[];
  activity_supply?: ActivitySupplyConfig;
}

export interface WeekSchedule {
  week: number;
  type: string;
  available_actions: string[];
  lessons: LessonConfig[];
  classes: LessonConfig[];
  event_name?: string;
  status_gain?: StatusValues;
  outing_effect?: StatusValues;
  class_effect?: StatusValues;
  consultation_effect?: StatusValues;
  special_training_effect?: StatusValues;
  /** HIFモードの公開レッスン日でサブ属性に加算されるサブ値。実行時にユーザ選択でメイン属性以外の1属性へ加算される */
  hif_sub_value?: number;
  /** HIFモードの試験日で全属性に同値加算される基礎値 */
  hif_exam_base?: number;
  /** HIFモードの試験日でユーザが Vo/Da/Vi に振り分ける配分値の合計 */
  hif_exam_distributed?: number;
  /** N.I.Aオーディションの種別ごとの理論値量（キャラの審査基準・流行で属性へ振り分け） */
  nia_audition_tiers?: NiaAuditionTier[];
}

/** N.I.Aオーディション1種別ぶんの、流行1/2/3別 取得パラメータ量（審査基準で行を選ぶ） */
export interface NiaTrendAmounts {
  /** 流行1 */
  t1: number;
  /** 流行2 */
  t2: number;
  /** 流行3 */
  t3: number;
}

export interface NiaAuditionTier {
  /** 種別名（例: FINALE / QUARTET / メロBang! 等） */
  name: string;
  /** 審査基準=バランスのときの流行1/2/3量 */
  balance: NiaTrendAmounts;
  /** 審査基準=突出のときの流行1/2/3量 */
  concentrate: NiaTrendAmounts;
}

export interface LessonConfig {
  type: string;
  sp_bonus: StatusValues;
}

export interface ActivitySupplyConfig {
  available_weeks: number[];
  options: SupplyOption[];
}

export interface SupplyOption {
  id: string;
  name: string;
  effect: StatusValues;
}

// --- TurnChoice ---

export interface TurnChoice {
  week: number;
  chosen_action: ActionType;
  supply_option_id?: string;
}

// --- AdditionalCounts ---

export interface AdditionalCounts extends Record<string, number> {
  p_drink_acquire: number;
  p_item_acquire: number;
  skill_acquire: number;
  skill_ssr_acquire: number;
  skill_enhance: number;
  skill_delete: number;
  skill_custom: number;
  skill_change: number;
  active_enhance: number;
  active_delete: number;
  mental_acquire: number;
  mental_enhance: number;
  mental_delete: number;
  active_acquire: number;
  genki_acquire: number;
  good_condition_acquire: number;
  good_impression_acquire: number;
  conserve_acquire: number;
  concentrate_acquire: number;
  motivation_acquire: number;
  fullpower_acquire: number;
  aggressive_acquire: number;
  consultation_drink: number;
}

export function emptyAdditionalCounts(): AdditionalCounts {
  return {
    p_drink_acquire: 0, p_item_acquire: 0,
    skill_acquire: 0,
    skill_ssr_acquire: 0, skill_enhance: 0, skill_delete: 0,
    skill_custom: 0, skill_change: 0,
    active_enhance: 0, active_delete: 0,
    mental_acquire: 0, mental_enhance: 0, mental_delete: 0, active_acquire: 0,
    genki_acquire: 0, good_condition_acquire: 0,
    good_impression_acquire: 0, conserve_acquire: 0,
    concentrate_acquire: 0, motivation_acquire: 0,
    fullpower_acquire: 0, aggressive_acquire: 0,
    consultation_drink: 0,
  };
}

export function additionalCountsToRecord(counts: AdditionalCounts): Record<string, number> {
  return { ...counts };
}

// --- EventCountTemplate ---

export interface EventCountTemplate {
  name: string;
  plan_id: string;
  counts: Record<string, number>;
  week_actions?: Record<number, ActionType>;
}

export interface EventCountTemplateFile {
  templates: EventCountTemplate[];
}

/** イベント回数のユーザ保存プリセット（現在の入力値を名前付きで保持） */
export interface EventCountPreset {
  name: string;
  counts: AdditionalCounts;
}

// --- Character ---

export interface StatBonusPercent {
  vo: number;
  da: number;
  vi: number;
}

/** 一部キャラに開放されるSTEP4の追加分（基礎ステータス・パラボの両方を上乗せ） */
export interface Step4Bonus {
  base_status_bonus: StatusValues;
  para_bonus: StatBonusPercent;
}

export interface Character {
  id: string;
  name: string;
  color: string;
  initial: string;
  base_status_bonus: StatusValues;
  para_bonus: StatBonusPercent;
  /** 3凸時に追加されるレッスンボーナス（任意） */
  uncap3_bonus?: StatBonusPercent;
  /** STEP4で追加される基礎ステータス＋パラボ（任意・ONで加算） */
  step4_bonus?: Step4Bonus;
  /** N.I.Aオーディションの審査基準（balance=バランス / concentrate=突出）。種別表の行選択に使う */
  nia_criteria?: 'balance' | 'concentrate';
  /** N.I.Aの流行1/2/3 が対応する属性 [流行1, 流行2, 流行3]（'vo'|'da'|'vi'）。未設定＝流行不明で獲得0 */
  nia_trend?: string[];
}

export interface CharacterFile {
  characters: Character[];
}

// --- MemoryBonus (持ち込みメモリー) ---

/** メモリーボーナス種別: 'flat'=実数値加算 / 'para'=レッスンパラメーターボーナス% */
export type MemoryBonusType = 'flat' | 'para';

export interface MemoryAttributeBonus {
  value: number;
  type: MemoryBonusType;
}

/** 持ち込みメモリー1枚分。Vo/Da/Vi 各属性ごとに「実数値」または「パラボ%」を1値持つ */
export interface MemoryBonus {
  vo: MemoryAttributeBonus;
  da: MemoryAttributeBonus;
  vi: MemoryAttributeBonus;
}

export function emptyMemoryAttributeBonus(): MemoryAttributeBonus {
  return { value: 0, type: 'flat' };
}

export function emptyMemoryBonus(): MemoryBonus {
  return {
    vo: emptyMemoryAttributeBonus(),
    da: emptyMemoryAttributeBonus(),
    vi: emptyMemoryAttributeBonus(),
  };
}

export function isEmptyMemoryBonus(m: MemoryBonus): boolean {
  return m.vo.value === 0 && m.da.value === 0 && m.vi.value === 0;
}

export function isEmptyAllMemoryBonuses(list: MemoryBonus[] | undefined | null): boolean {
  if (!list) return true;
  return list.every(isEmptyMemoryBonus);
}

/** flat 種別のみを属性別に合計して StatusValues として返す（floor 適用） */
export function sumMemoryFlat(list: MemoryBonus[] | undefined | null): StatusValues {
  if (!list) return { vo: 0, da: 0, vi: 0 };
  let vo = 0, da = 0, vi = 0;
  for (const m of list) {
    if (m.vo.type === 'flat') vo += m.vo.value;
    if (m.da.type === 'flat') da += m.da.value;
    if (m.vi.type === 'flat') vi += m.vi.value;
  }
  return { vo: Math.floor(vo), da: Math.floor(da), vi: Math.floor(vi) };
}

/** para 種別のみを属性別に合計して % 値（vo/da/vi）として返す */
export function sumMemoryParaBonus(list: MemoryBonus[] | undefined | null): StatBonusPercent {
  if (!list) return { vo: 0, da: 0, vi: 0 };
  let vo = 0, da = 0, vi = 0;
  for (const m of list) {
    if (m.vo.type === 'para') vo += m.vo.value;
    if (m.da.type === 'para') da += m.da.value;
    if (m.vi.type === 'para') vi += m.vi.value;
  }
  return { vo, da, vi };
}

/** 持ち込みメモリーのプリセット（4枚分のセット） */
export interface MemoryPreset {
  name: string;
  bonuses: MemoryBonus[];
}
