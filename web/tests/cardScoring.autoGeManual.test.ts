import { describe, it, expect } from 'vitest';
import { selectMultiplePatterns } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, templateAdditionalCounts } from './helpers/loadRealData';
import { scoreDeck, totalGte } from './helpers/scoreDeck';
import type { SupportCard, TrainingPlan, AdditionalCounts } from '../src/types/models';

/**
 * 自動編成 ≧ 手動編成 (通常モード・実データ)。このツールの核心。
 *
 * 「ユーザがメイン属性指定の意図に沿って手で組みそうなバランス編成」を実データで作り、
 * 自動ピックの cap 後合計がそのいずれにも劣らないことを検証する。自動 < 手動 はバグ。
 *
 * イベント回数は実テンプレート (hatsu_legend「センス（活動支給軸）」) を選択して
 * additionalCounts に渡し、トリガー系カードのスコアも実運用と同条件で評価する。
 *
 * 注: 手動編成は最適化器が探索する設計空間 (メイン属性バランス) 内に限定する。
 * 「単一属性6枚」のような型偏重編成は意図的に生成しない (確定仕様): 6枚同属性だと
 * メイン2/サブが必要ステータスに届かずゲーム的に成立しないため。詳細は project_monotype_pattern_gap。
 */

const plan = loadPlan('hatsu_legend');
const allCards = loadAllCards();
const counts: AdditionalCounts = templateAdditionalCounts('hatsu_legend', 'センス（活動支給軸）');
const lessonWeeks = plan.schedule.filter(
  (w) => w.lessons.length > 0 && w.type !== 'fixed_event',
).length;

const WILD = new Set(['all', 'as']);

function topByType(p: TrainingPlan, cards: SupportCard[], stat: string, n: number): SupportCard[] {
  return cards
    .filter((c) => c.type === stat || WILD.has(c.type))
    .map((c) => ({ c, v: scoreDeck(p, [c], [stat], { additionalCounts: counts }).cappedTotal }))
    .sort((a, b) => b.v - a.v)
    .map((x) => x.c)
    .slice(0, n);
}

function fillDistinct(primary: SupportCard[], pool: SupportCard[], size: number): SupportCard[] {
  const out: SupportCard[] = [];
  const used = new Set<string>();
  for (const c of [...primary, ...pool]) {
    if (used.has(c.id)) continue;
    out.push(c);
    used.add(c.id);
    if (out.length === size) break;
  }
  return out;
}

function autoBest(mainStats: string[], subStat: string, spCounts?: Record<string, number>): number {
  const patterns = selectMultiplePatterns(
    plan, allCards, mainStats, subStat, lessonWeeks, spCounts, undefined, counts,
  );
  let best = -Infinity;
  for (const pat of patterns) {
    const s = scoreDeck(plan, pat.selected_cards.map((c) => c.card), mainStats, { additionalCounts: counts }).cappedTotal;
    if (s > best) best = s;
  }
  return best;
}

function balancedManuals(
  mainStats: string[],
  subStat: string,
): Array<{ label: string; cards: SupportCard[] }> {
  const [m1, m2] = mainStats;
  const ranked = [...allCards]
    .map((c) => ({ c, v: scoreDeck(plan, [c], mainStats, { additionalCounts: counts }).cappedTotal }))
    .sort((a, b) => b.v - a.v)
    .map((x) => x.c);

  const deck33 = fillDistinct(
    [...topByType(plan, allCards, m1, 3), ...topByType(plan, allCards, m2, 3)], ranked, 6,
  );
  const deck222 = fillDistinct(
    [
      ...topByType(plan, allCards, 'vo', 2),
      ...topByType(plan, allCards, 'da', 2),
      ...topByType(plan, allCards, 'vi', 2),
    ], ranked, 6,
  );
  const deck321 = fillDistinct(
    [
      ...topByType(plan, allCards, m1, 3),
      ...topByType(plan, allCards, m2, 2),
      ...topByType(plan, allCards, subStat, 1),
    ], ranked, 6,
  );
  return [
    { label: 'バランス3+3', cards: deck33 },
    { label: 'バランス2+2+2', cards: deck222 },
    { label: 'バランス3+2+1', cards: deck321 },
  ];
}

const combos: Array<[string[], string]> = [
  [['vo', 'da'], 'vi'],
  [['da', 'vi'], 'vo'],
  [['vo', 'vi'], 'da'],
];

describe('自動編成 ≧ 手動バランス編成 (通常モード・hatsu_legend + テンプレ適用)', () => {
  for (const [mainStats, subStat] of combos) {
    const auto = autoBest(mainStats, subStat);
    for (const m of balancedManuals(mainStats, subStat)) {
      it(`main=${mainStats.join('/')}: 自動 ≧ 手動[${m.label}]`, () => {
        const manualScore = scoreDeck(plan, m.cards, mainStats, { additionalCounts: counts }).cappedTotal;
        expect(totalGte(auto, manualScore)).toBe(true);
      });
    }
  }
});
