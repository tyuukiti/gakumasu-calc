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

  // 要望 #138: W先生×Pアイテム3つ等でサポカ5枚が確定する運用向けに、必須カードは
  // デッキ枠と同じ6枚まで登録できる (5枚固定+自動1枠、または全6枠固定の直接評価)。
  it('必須カード5枚がすべて編成に含まれ 6 枚に収まる', () => {
    const plan = makePlan({ statusLimit: 9999 });
    // 寄与下位を含む5枚を必須指定しても全部入り、自動選出は残り1枠のみ
    const required = ['VO3', 'VO4', 'DA3', 'VI1', 'VI2'];
    const deck = select(plan, syntheticPool(), { vo: 2, da: 2 }, 2, ['vo', 'da'], undefined, required);
    const cards = deckCards(deck);
    expect(cards.length).toBe(6);
    expect(hasNoDuplicates(cards)).toBe(true);
    for (const id of required) {
      expect(cards.map((c) => c.id), `必須カード ${id} が編成に含まれること`).toContain(id);
    }
  });

  it('所持のみ相当 (レンタルあり) の必須5枚: 全必須含有・レンタル1枚・自動選出枠が1枚残る', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const pool = syntheticPool();
    const required = ['VO3', 'VO4', 'DA3', 'VI1', 'VI2'];
    const deck = selectOptimalDeck(
      plan, pool, ALLOC, { vo: 2, da: 2 }, ['vo', 'da'],
      undefined, undefined, undefined, undefined, pool,
      2, required,
    );
    const cards = deckCards(deck);
    expect(cards.length).toBe(6);
    expect(hasNoDuplicates(cards)).toBe(true);
    for (const id of required) {
      expect(cards.map((c) => c.id), `必須カード ${id} が編成に含まれること`).toContain(id);
    }
    // 所持のみ運用ではレンタル枠がちょうど1枚
    expect(deck.selected_cards.filter((cs) => cs.is_rental).length).toBe(1);
    // 必須5枚以外に自動選出された1枚が存在する
    expect(cards.filter((c) => !required.includes(c.id)).length).toBe(1);
  });

  it('必須カード6枚 (上限) でデッキ全枠が固定される', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const required = ['VO1', 'VO3', 'VO4', 'DA3', 'VI1', 'VI2'];
    const deck = select(plan, syntheticPool(), { vo: 2, da: 2 }, 2, ['vo', 'da'], undefined, required);
    expect(deckCards(deck).map((c) => c.id).sort()).toEqual([...required].sort());
  });

  it('所持のみ相当 (レンタルあり) の必須6枚: デッキ=必須6枚そのもの・借用枠は必須内に1枚割り当たる', () => {
    const plan = makePlan({ statusLimit: 9999 });
    const pool = syntheticPool();
    const required = ['VO1', 'VO3', 'VO4', 'DA3', 'VI1', 'VI2'];
    const deck = selectOptimalDeck(
      plan, pool, ALLOC, { vo: 2, da: 2 }, ['vo', 'da'],
      undefined, undefined, undefined, undefined, pool,
      2, required,
    );
    expect(deckCards(deck).map((c) => c.id).sort()).toEqual([...required].sort());
    // 全枠必須でもレンタル(借用)枠は消えず、必須6枚のうち1枚に割り当たる
    expect(deck.selected_cards.filter((cs) => cs.is_rental).length).toBe(1);
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
