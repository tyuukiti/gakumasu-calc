import { describe, it, expect } from 'vitest';
import { selectOptimalDeck, selectMultiplePatterns } from '../src/services/cardScoring';
import { makeCard, makePlan, resetIds } from './helpers/factories';
import { loadAllCards, loadPlan } from './helpers/loadRealData';
import {
  countSp,
  countTypeSlotFillable,
  hasNoDuplicates,
  deckCards,
} from './helpers/constraints';
import type { SupportCard, TrainingPlan } from '../src/types/models';

/**
 * L1: 制約遵守・決定性の不変条件テスト。
 * 採点に依存しない構造的性質なので、スコア調整があっても壊れない広いセーフティネット。
 */

const ALLOC = { vo: 0, da: 0, vi: 0 };

function select(
  plan: TrainingPlan,
  pool: SupportCard[],
  cardTypeSlots: Record<string, number>,
  freeSlots: number,
  mainStats: string[],
  spCounts?: Record<string, number>,
  requiredCardIds?: string[],
) {
  return selectOptimalDeck(
    plan, pool, ALLOC, cardTypeSlots, mainStats,
    spCounts, undefined, undefined, undefined, undefined,
    freeSlots, requiredCardIds,
  );
}

function syntheticPool(): SupportCard[] {
  resetIds();
  return [
    makeCard({ id: 'VO1', type: 'vo', equip: { vo: 200 }, sp: ['vo'] }),
    makeCard({ id: 'VO2', type: 'vo', equip: { vo: 180 }, sp: ['vo'] }),
    makeCard({ id: 'VO3', type: 'vo', equip: { vo: 160 } }),
    makeCard({ id: 'VO4', type: 'vo', equip: { vo: 140 } }),
    makeCard({ id: 'DA1', type: 'da', equip: { da: 190 } }),
    makeCard({ id: 'DA2', type: 'da', equip: { da: 170 } }),
    makeCard({ id: 'DA3', type: 'da', equip: { da: 150 } }),
    makeCard({ id: 'VI1', type: 'vi', equip: { vi: 130 } }),
    makeCard({ id: 'VI2', type: 'vi', equip: { vi: 120 } }),
    makeCard({ id: 'AS1', type: 'all', equip: { vo: 60, da: 60, vi: 60 } }),
  ];
}

describe('L1: 制約遵守 (合成)', () => {
  it('デッキは常に 6 枚・重複なし', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const deck = select(plan, syntheticPool(), { vo: 3, da: 2 }, 1, ['vo', 'da']);
    const cards = deckCards(deck);
    expect(cards.length).toBe(6);
    expect(hasNoDuplicates(cards)).toBe(true);
  });

  it('必須カードは必ず編成に含まれる', () => {
    const plan = makePlan({ statusLimit: 9999 });
    // 寄与の低い VI2 を必須指定しても入るはず
    const deck = select(plan, syntheticPool(), { vo: 3, da: 2 }, 1, ['vo', 'da'], undefined, ['VI2']);
    const ids = deckCards(deck).map((c) => c.id);
    expect(ids).toContain('VI2');
  });

  it('属性枠 (cardTypeSlots) を満たす', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const deck = select(plan, syntheticPool(), { vo: 3, da: 2 }, 1, ['vo', 'da']);
    const cards = deckCards(deck);
    expect(countTypeSlotFillable(cards, 'vo')).toBeGreaterThanOrEqual(3);
    expect(countTypeSlotFillable(cards, 'da')).toBeGreaterThanOrEqual(2);
  });

  it('SP 枚数を満たせるプールでは要求枚数を満たす', () => {
    const plan = makePlan({ statusLimit: 9999 });
    // vo SP カードは VO1/VO2 の 2 枚。spCounts={vo:2} を要求 → 2 枚とも入るべき
    const deck = select(plan, syntheticPool(), { vo: 2, da: 2 }, 2, ['vo', 'da'], { vo: 2 });
    expect(countSp(deckCards(deck), 'vo')).toBeGreaterThanOrEqual(2);
  });

  it('必須 > SP枚数 > 編成パターン の優先順位: 必須は SP 強制でも残る', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const deck = select(
      plan, syntheticPool(), { vo: 2, da: 2 }, 2, ['vo', 'da'], { vo: 2 }, ['DA3'],
    );
    const ids = deckCards(deck).map((c) => c.id);
    expect(ids).toContain('DA3'); // 必須
    expect(countSp(deckCards(deck), 'vo')).toBeGreaterThanOrEqual(2); // SP も確保
  });
});

describe('L1: 決定性', () => {
  it('同一入力なら同一出力 (ID 順含め)', () => {
    const plan = makePlan({ statusLimit: 500, baseStatus: { vo: 50, da: 50, vi: 50 } });
    const a = select(plan, syntheticPool(), { vo: 3, da: 2 }, 1, ['vo', 'da']);
    const b = select(plan, syntheticPool(), { vo: 3, da: 2 }, 1, ['vo', 'da']);
    expect(a.selected_cards.map((c) => c.card.id)).toEqual(
      b.selected_cards.map((c) => c.card.id),
    );
  });
});

describe('L1: 構造的不変条件 (実データ全パターン)', () => {
  const plan = loadPlan('hatsu_legend');
  const allCards = loadAllCards();
  const lessonWeeks = plan.schedule.filter(
    (w) => w.lessons.length > 0 && w.type !== 'fixed_event',
  ).length;

  const combos: Array<[string[], string]> = [
    [['vo', 'da'], 'vi'],
    [['da', 'vi'], 'vo'],
    [['vo', 'vi'], 'da'],
  ];

  for (const [mainStats, subStat] of combos) {
    it(`main=${mainStats.join('/')} の全パターンが 6 枚・重複なし`, () => {
      const patterns = selectMultiplePatterns(
        plan, allCards, mainStats, subStat, lessonWeeks, { [mainStats[0]]: 1 },
      );
      expect(patterns.length).toBeGreaterThan(0);
      for (const p of patterns) {
        const cards = deckCards(p);
        expect(cards.length).toBe(6);
        expect(hasNoDuplicates(cards)).toBe(true);
      }
    });
  }
});
