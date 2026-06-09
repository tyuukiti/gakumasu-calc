import { describe, it, expect } from 'vitest';
import { selectOptimalDeck } from '../src/services/cardScoring';
import { makeCard, makePlan, resetIds } from './helpers/factories';
import { findOptimalDeck } from './helpers/bruteForce';
import { scoreDeck, totalsEqual, totalGte } from './helpers/scoreDeck';
import type { SupportCard, TrainingPlan } from '../src/types/models';

/**
 * L1: 不変条件テスト (合成フィクスチャ + 総当たりオラクル)。
 *
 * デッキは (レンタルなしのとき) 常に 6 枚。cardTypeSlots は「属性ごとの最低枚数」で、
 * 残りは補充される。総当たりオラクルも totalSlots=6 で列挙する。
 *
 * 採点は最適化器の内部スコアではなく「実 calculate の cap 後合計」で行う (scoreDeck)。
 * 自動ピックの cap 後合計が総当たり最適と一致することを検証する。
 * 不一致は「自動 < 手動 が起こりうる」= optimizer-is-the-product 的にバグ。
 */

const DECK_SIZE = 6;

function autoSelect(
  plan: TrainingPlan,
  pool: SupportCard[],
  cardTypeSlots: Record<string, number>,
  freeSlots: number,
  mainStats: string[],
  spCounts?: Record<string, number>,
) {
  const alloc = { vo: 0, da: 0, vi: 0 };
  return selectOptimalDeck(
    plan,
    pool,
    alloc,
    cardTypeSlots,
    mainStats,
    spCounts, // spCounts
    undefined, // planType
    undefined, // additionalCounts
    undefined, // uncapLevels
    undefined, // rentalPool
    freeSlots,
  );
}

function autoScore(
  plan: TrainingPlan,
  pool: SupportCard[],
  cardTypeSlots: Record<string, number>,
  freeSlots: number,
  mainStats: string[],
): number {
  const deck = autoSelect(plan, pool, cardTypeSlots, freeSlots, mainStats);
  return scoreDeck(plan, deck.selected_cards.map((c) => c.card), mainStats).cappedTotal;
}

describe('L1: 自動ピック == 総当たり最適 (cap 後合計)', () => {
  it('上限張り付きトラップ: 同属性を積み過ぎず均等配分を選ぶ', () => {
    resetIds();
    // base 0 / cap 250。vo,da は +200 を 1 枚で 200、2 枚目は 400→cap250 で限界価値激減。
    // 均等 (vo2/da2/vi2) が最適。cap-aware が壊れると同属性を積んで損をする。
    const plan = makePlan({ statusLimit: 250, baseStatus: { vo: 0, da: 0, vi: 0 } });
    const pool: SupportCard[] = [
      makeCard({ id: 'VO1', type: 'vo', equip: { vo: 200 } }),
      makeCard({ id: 'VO2', type: 'vo', equip: { vo: 200 } }),
      makeCard({ id: 'VO3', type: 'vo', equip: { vo: 200 } }),
      makeCard({ id: 'DA1', type: 'da', equip: { da: 200 } }),
      makeCard({ id: 'DA2', type: 'da', equip: { da: 200 } }),
      makeCard({ id: 'DA3', type: 'da', equip: { da: 200 } }),
      makeCard({ id: 'VI1', type: 'vi', equip: { vi: 120 } }),
      makeCard({ id: 'VI2', type: 'vi', equip: { vi: 120 } }),
    ];
    const mainStats = ['vo', 'da'];
    const auto = autoScore(plan, pool, {}, DECK_SIZE, mainStats);
    const oracle = findOptimalDeck(plan, pool, {}, DECK_SIZE, mainStats);
    expect(oracle.bestTotal).toBe(740); // 250 + 250 + 240
    expect(totalsEqual(auto, oracle.bestTotal)).toBe(true);
  });

  it('上限なし: 寄与合計が最大の 6 枚を選ぶ', () => {
    resetIds();
    const plan = makePlan({ statusLimit: 999999, baseStatus: { vo: 0, da: 0, vi: 0 } });
    const pool: SupportCard[] = [
      makeCard({ id: 'A', type: 'vo', equip: { vo: 300 } }),
      makeCard({ id: 'B', type: 'vo', equip: { vo: 250 } }),
      makeCard({ id: 'C', type: 'da', equip: { da: 240 } }),
      makeCard({ id: 'D', type: 'da', equip: { da: 220 } }),
      makeCard({ id: 'E', type: 'vi', equip: { vi: 210 } }),
      makeCard({ id: 'F', type: 'vi', equip: { vi: 80 } }),
      makeCard({ id: 'G', type: 'vo', equip: { vo: 70 } }),
      makeCard({ id: 'H', type: 'da', equip: { da: 60 } }),
    ];
    const mainStats = ['vo', 'da'];
    const auto = autoScore(plan, pool, {}, DECK_SIZE, mainStats);
    const oracle = findOptimalDeck(plan, pool, {}, DECK_SIZE, mainStats);
    expect(totalsEqual(auto, oracle.bestTotal)).toBe(true);
  });

  it('属性枠あり: vo2/da2 + フリー2 でも総当たり最適に一致', () => {
    resetIds();
    const plan = makePlan({ statusLimit: 600, baseStatus: { vo: 50, da: 50, vi: 50 } });
    const pool: SupportCard[] = [
      makeCard({ id: 'VO1', type: 'vo', equip: { vo: 180 } }),
      makeCard({ id: 'VO2', type: 'vo', equip: { vo: 120 } }),
      makeCard({ id: 'VO3', type: 'vo', equip: { vo: 90 } }),
      makeCard({ id: 'DA1', type: 'da', equip: { da: 200 } }),
      makeCard({ id: 'DA2', type: 'da', equip: { da: 140 } }),
      makeCard({ id: 'DA3', type: 'da', equip: { da: 60 } }),
      makeCard({ id: 'VI1', type: 'vi', equip: { vi: 300 } }),
      makeCard({ id: 'AS1', type: 'all', equip: { vo: 50, da: 50, vi: 50 } }),
    ];
    const mainStats = ['vo', 'da'];
    const cardTypeSlots = { vo: 2, da: 2 };
    const auto = autoScore(plan, pool, cardTypeSlots, 2, mainStats);
    const oracle = findOptimalDeck(plan, pool, cardTypeSlots, 2, mainStats);
    expect(totalsEqual(auto, oracle.bestTotal)).toBe(true);
  });

  it('自動 >= 総当たりの「あらゆる手動編成」(全列挙に対する優位)', () => {
    resetIds();
    const plan = makePlan({ statusLimit: 400, baseStatus: { vo: 20, da: 20, vi: 20 } });
    const pool: SupportCard[] = [
      makeCard({ id: 'VO1', type: 'vo', equip: { vo: 220 }, paraBonus: { vo: 10 } }),
      makeCard({ id: 'VO2', type: 'vo', equip: { vo: 130 } }),
      makeCard({ id: 'DA1', type: 'da', equip: { da: 210 } }),
      makeCard({ id: 'DA2', type: 'da', equip: { da: 150 } }),
      makeCard({ id: 'VI1', type: 'vi', equip: { vi: 260 } }),
      makeCard({ id: 'VI2', type: 'vi', equip: { vi: 110 } }),
      makeCard({ id: 'AS1', type: 'all', equip: { vo: 60, da: 60, vi: 60 } }),
      makeCard({ id: 'AS2', type: 'all', equip: { vo: 40, da: 40, vi: 40 } }),
    ];
    const mainStats = ['vo', 'vi'];
    const auto = autoScore(plan, pool, { vo: 2, vi: 2 }, 2, mainStats);
    const oracle = findOptimalDeck(plan, pool, { vo: 2, vi: 2 }, 2, mainStats);
    // 自動は手動の真の最大に「届く」べき (劣ってはならない)
    expect(totalGte(auto, oracle.bestTotal)).toBe(true);
  });
});
