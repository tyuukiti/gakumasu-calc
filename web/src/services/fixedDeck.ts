/**
 * 確定済みの編成 (共有された6枚) を最適化なしで DeckResult にする。
 *
 * selectOptimalDeckOnce のデッキ確定処理 (recomputeBreakdownsDeckAware → recalculateWithCap →
 * buildAbilitySummary) と同じ手順で各カードの寄与・内訳・アビリティまとめを作るので、
 * 共有元が見ていた選択デッキの表示と同じ値になる。到達ステータスは別途 calculate() で求める。
 */
import type { SupportCard, TrainingPlan, TurnChoice, AdditionalCounts } from '../types/models';
import { additionalCountsToRecord } from '../types/models';
import type { CardScore, DeckResult } from '../types/results';
import {
  countTriggers,
  estimateBaseStats,
  calculateLessonStatTotals,
  calculateCardContribution,
  computeTriggerBonusInfo,
  recomputeBreakdownsDeckAware,
  recalculateWithCap,
  buildAbilitySummary,
} from './cardScoring';
import type { SharedDeckCard } from './shareState';

export interface FixedDeckCard {
  card: SupportCard;
  /** 計算に使う凸数 (0-4)。レンタルは常に 4 として扱う */
  uncap: number;
  isRental: boolean;
  isRequired: boolean;
}

/** 共有された編成のカード ID を現在のカードデータに解決する。1枚でも見つからなければ null。 */
export function resolveSharedDeck(deck: SharedDeckCard[], cards: SupportCard[]): FixedDeckCard[] | null {
  const byId = new Map(cards.map((c) => [c.id, c]));
  const out: FixedDeckCard[] = [];
  for (const d of deck) {
    const card = byId.get(d.id);
    if (!card) return null;
    out.push({ card, uncap: d.rental ? 4 : d.uncap, isRental: d.rental, isRequired: d.required });
  }
  return out;
}

/** 編成の凸数マップ (card id → uncap)。レンタルは 4。 */
export function fixedDeckUncapLevels(deck: FixedDeckCard[]): Record<string, number> {
  const levels: Record<string, number> = {};
  for (const d of deck) levels[d.card.id] = d.isRental ? 4 : d.uncap;
  return levels;
}

/**
 * 固定編成の DeckResult を構築する。selected_cards の並びは与えられた順 (共有元の表示順) を保つ。
 * total_value は呼び出し側が calculate() の cap 後合計で上書きする (executeCalculate と同じ扱い)。
 */
export function buildFixedDeckResult(
  plan: TrainingPlan,
  deck: FixedDeckCard[],
  lessonAllocation: Record<string, number>,
  mainStats: string[],
  additionalCounts: AdditionalCounts | undefined,
  turnChoices: TurnChoice[],
  label: string,
): DeckResult {
  const statCap = plan.status_limit;
  const triggerCounts = countTriggers(plan, lessonAllocation, mainStats, turnChoices);
  if (additionalCounts != null) {
    for (const [key, value] of Object.entries(additionalCountsToRecord(additionalCounts))) {
      if (value > 0) triggerCounts[key] = (triggerCounts[key] ?? 0) + value;
    }
  }
  const baseStats = estimateBaseStats(plan, lessonAllocation, turnChoices);
  const lessonStatTotals = calculateLessonStatTotals(plan, lessonAllocation, turnChoices);
  const uncapLevels = fixedDeckUncapLevels(deck);
  const deckCards = deck.map((d) => d.card);
  const triggerBonusInfo = computeTriggerBonusInfo(deckCards, uncapLevels);

  const selected: CardScore[] = deck.map((d) => ({
    ...calculateCardContribution(
      d.card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    ),
    is_rental: d.isRental,
    is_required: d.isRequired,
  }));

  const adjustedCounts = recomputeBreakdownsDeckAware(
    selected,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
  );
  recalculateWithCap(selected, baseStats, statCap);

  return {
    label,
    selected_cards: selected,
    total_value: selected.reduce((sum, c) => sum + c.total_value, 0),
    ability_summary: buildAbilitySummary(selected, adjustedCounts, uncapLevels),
  };
}
