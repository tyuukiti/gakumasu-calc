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

/**
 * アビリティまとめ (行動別) の1エントリ。
 * 選択6枚の flat 効果 (trigger !== 'equip') を (行動トリガー × 属性) で合算したもの。
 * 「どの行動を取るとパラメが伸びるか」の比較用。値は各カード個別内訳と同じ生寄与 (cap前・キャラパラボ前)。
 */
export interface AbilitySummaryEntry {
  /** トリガーキー (例: 'class_end') */
  trigger: string;
  /** トリガー表示名 (例: '授業終了') */
  trigger_name: string;
  /** 属性 ('vo' | 'da' | 'vi' | 'all') */
  stat: string;
  /** 1発動あたりの合計上昇値 X = Σ(各カードの per-fire 値) */
  per_fire: number;
  /** per-fire 値のカード別内訳 (降順)。表示の (a+b+c) 用 */
  parts: number[];
  /** 発動回数 (N) */
  fires: number;
  /** 合計寄与 (権威値) = Σ(各カードの per-fire × 実効発動回数) */
  total: number;
}

export interface DeckResult {
  label: string;
  selected_cards: CardScore[];
  total_value: number;
  /** アビリティまとめ (行動別)。total 降順。行動トリガーが1件も無ければ空配列 */
  ability_summary: AbilitySummaryEntry[];
}
