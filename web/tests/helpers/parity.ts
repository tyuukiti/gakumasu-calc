import { selectMultiplePatterns, selectMultiplePatternsHif, buildTurnChoices } from '../../src/services/cardScoring';
import { scoreDeck } from './scoreDeck';
import { lessonAllocationFrom } from './hif';
import { templateAdditionalCounts, loadHifPouch } from './loadRealData';
import { computeHifPouchCounts, mergeAdditionalCounts } from '../../src/services/hifPouch';
import { emptyAdditionalCounts } from '../../src/types/models';
import type { SupportCard, TrainingPlan, AdditionalCounts } from '../../src/types/models';

/**
 * クロス実装パリティ用の共有計算。TS版・C#版が同じ設定から同じ結果を出すことを保証する。
 * 通常モード(selectMultiplePatterns) と HIFモード(selectMultiplePatternsHif) の両方に対応。
 * 出力はパターン順の {ids(ソート済), total(cap後合計)} 配列。
 */

export interface ParityScenario {
  id: string;
  mainStats: string[];
  subStat: string;
  spCounts?: Record<string, number>;
  /** 'hif' なら selectMultiplePatternsHif を使う。省略時は通常モード。 */
  mode?: 'normal' | 'hif';
  /** シナリオ個別のプランID。省略時は config.planId。 */
  planId?: string;
  /** イベント回数テンプレート名 (plan は plan.id で解決)。指定時は counts を additionalCounts に渡す。 */
  templateName?: string;
  /** HIF のポーチ選択 (hif モードのみ)。日程 (buildTurnChoices) から発動回数を集計して counts に合算する。 */
  pouch?: { color: string; plush: string | null; deco: string | null };
}

export interface ParityConfig {
  planId: string;
  scenarios: ParityScenario[];
}

export interface PatternResult {
  ids: string[];
  total: number;
}

export function lessonWeekCount(plan: TrainingPlan): number {
  return plan.schedule.filter(
    (w) => w.lessons.length > 0 && w.type !== 'fixed_event',
  ).length;
}

/** 1 シナリオの全パターン結果を計算 (通常 / HIF)。 */
export function computeScenario(
  plan: TrainingPlan,
  allCards: SupportCard[],
  scenario: ParityScenario,
): PatternResult[] {
  let counts: AdditionalCounts | undefined = scenario.templateName
    ? templateAdditionalCounts(plan.id, scenario.templateName)
    : undefined;

  if (scenario.mode === 'hif') {
    const tc = buildTurnChoices(plan, scenario.mainStats);
    const alloc = lessonAllocationFrom(tc);
    if (scenario.pouch) {
      const pouchCounts = computeHifPouchCounts(loadHifPouch(), scenario.pouch, tc).counts;
      counts = mergeAdditionalCounts(counts ?? emptyAdditionalCounts(), pouchCounts);
    }
    const patterns = selectMultiplePatternsHif(
      plan, allCards, scenario.mainStats, alloc,
      scenario.spCounts, undefined, counts, undefined, undefined, undefined,
      null, null, tc,
    );
    return patterns.map((pat) => {
      const cards = pat.selected_cards.map((c) => c.card);
      const ids = cards.map((c) => c.id).sort();
      const total = scoreDeck(plan, cards, scenario.mainStats, { turnChoices: tc, additionalCounts: counts }).cappedTotal;
      return { ids, total };
    });
  }

  const patterns = selectMultiplePatterns(
    plan,
    allCards,
    scenario.mainStats,
    scenario.subStat,
    lessonWeekCount(plan),
    scenario.spCounts,
    undefined,
    counts,
  );
  return patterns.map((pat) => {
    const cards = pat.selected_cards.map((c) => c.card);
    const ids = cards.map((c) => c.id).sort();
    const total = scoreDeck(plan, cards, scenario.mainStats, { additionalCounts: counts }).cappedTotal;
    return { ids, total };
  });
}

/** 全シナリオを計算し id -> PatternResult[] のマップを返す。 */
export function computeParity(
  config: ParityConfig,
  getPlan: (id: string) => TrainingPlan,
  allCards: SupportCard[],
): Record<string, PatternResult[]> {
  const out: Record<string, PatternResult[]> = {};
  for (const sc of config.scenarios) {
    const plan = getPlan(sc.planId ?? config.planId);
    out[sc.id] = computeScenario(plan, allCards, sc);
  }
  return out;
}
