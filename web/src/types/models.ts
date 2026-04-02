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
  skill_ssr_acquire: number;
  skill_enhance: number;
  skill_delete: number;
  skill_custom: number;
  skill_change: number;
  active_enhance: number;
  active_delete: number;
  mental_acquire: number;
  mental_enhance: number;
  active_acquire: number;
  genki_acquire: number;
  good_condition_acquire: number;
  good_impression_acquire: number;
  conserve_acquire: number;
  consultation_drink: number;
}

export function emptyAdditionalCounts(): AdditionalCounts {
  return {
    p_drink_acquire: 0, p_item_acquire: 0,
    skill_ssr_acquire: 0, skill_enhance: 0, skill_delete: 0,
    skill_custom: 0, skill_change: 0,
    active_enhance: 0, active_delete: 0,
    mental_acquire: 0, mental_enhance: 0, active_acquire: 0,
    genki_acquire: 0, good_condition_acquire: 0,
    good_impression_acquire: 0, conserve_acquire: 0,
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
}

export interface EventCountTemplateFile {
  templates: EventCountTemplate[];
}
