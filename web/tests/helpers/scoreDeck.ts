import { calculate } from '../../src/services/statusCalculation';
import { buildTurnChoices } from '../../src/services/cardScoring';
import type {
  SupportCard,
  TrainingPlan,
  TurnChoice,
  AdditionalCounts,
  Character,
  MemoryBonus,
} from '../../src/types/models';
import type { CalculationResult } from '../../src/types/results';

export interface ScoreOptions {
  uncapLevels?: Record<string, number>;
  additionalCounts?: AdditionalCounts;
  character?: Character | null;
  memoryBonuses?: MemoryBonus[] | null;
  /**
   * ターン選択の明示指定 (HIF など)。省略時は最適化器が内部評価に使うのと同じ
   * {@link buildTurnChoices} で生成する。
   */
  turnChoices?: TurnChoice[];
}

export interface DeckScore {
  result: CalculationResult;
  cappedTotal: number;
  capped: { vo: number; da: number; vi: number };
}

const EPS = 1e-6;

/** 1属性を上限でクランプ。 */
export function capStat(value: number, statCap: number): number {
  return Math.min(value, statCap);
}

/**
 * デッキ採点オラクル。最適化器の内部スコア (total_value) ではなく
 * **実 `calculate` の cap 後合計** を「正しさの基準」として返す。
 *
 * 採点に使うターン選択は、最適化器が内部評価に用いるのと同一の
 * {@link buildTurnChoices} に揃えてある (opts.turnChoices で上書き可)。
 */
export function scoreDeck(
  plan: TrainingPlan,
  cards: SupportCard[],
  mainStats: string[],
  opts: ScoreOptions = {},
): DeckScore {
  const turnChoices = opts.turnChoices ?? buildTurnChoices(plan, mainStats);
  const result = calculate(
    plan,
    cards,
    turnChoices,
    opts.uncapLevels,
    opts.additionalCounts,
    opts.character ?? null,
    opts.memoryBonuses ?? null,
  );
  const cap = plan.status_limit;
  const capped = {
    vo: capStat(result.final_status.vo, cap),
    da: capStat(result.final_status.da, cap),
    vi: capStat(result.final_status.vi, cap),
  };
  return {
    result,
    capped,
    cappedTotal: capped.vo + capped.da + capped.vi,
  };
}

/** 2 つのデッキ合計が (浮動小数の誤差を許容して) 等しいか。 */
export function totalsEqual(a: number, b: number): boolean {
  return Math.abs(a - b) <= EPS;
}

/** a >= b (誤差許容)。 */
export function totalGte(a: number, b: number): boolean {
  return a - b >= -EPS;
}

export { EPS };
