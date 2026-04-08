import type { StatusValues, SupportCard } from './models';

export interface CalculationResult {
  final_status: StatusValues;
  base_status: StatusValues;
  support_card_bonus: StatusValues;
  accumulated_gain: StatusValues;
  week_details: WeekBreakdown[];
}

export interface WeekBreakdown {
  week: number;
  action_name: string;
  gain: StatusValues;
}

export interface CardScore {
  card: SupportCard;
  total_value: number;
  raw_vo: number;
  raw_da: number;
  raw_vi: number;
  breakdowns: EffectBreakdown[];
  is_rental: boolean;
  is_required: boolean;
}

export interface EffectBreakdown {
  reason: string;
  stat: string;
  value: number;
}

export interface DeckResult {
  label: string;
  selected_cards: CardScore[];
  total_value: number;
}
