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
  /** trigger_count_bonus 由来で「他カードへ寄与する」推定総量 (情報表示用、total_value には含まれない場合あり) */
  team_bonus_total: number;
  /** trigger_count_bonus 由来の寄与内訳 (UI で全件並べる用) */
  team_bonus_contributors: TeamBonusContributor[];
  breakdowns: EffectBreakdown[];
  is_rental: boolean;
  is_required: boolean;
  /** このカードが計算に使われた凸数 (0-4)。レンタルは4凸借用、所持のみOFFの未所持カードは4。 */
  uncap_level: number;
}

export interface TeamBonusContributor {
  card_name: string;
  value: number;
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
