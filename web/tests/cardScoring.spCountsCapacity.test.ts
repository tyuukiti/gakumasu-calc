import { describe, it, expect } from 'vitest';
import { selectOptimalDeck } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, loadCharacters } from './helpers/loadRealData';
import { deckCards, countSp, hasNoDuplicates } from './helpers/constraints';

/**
 * 回帰 (ユーザ報告 2026-09): hatsu_legend/sense で必須3枚 (全て非SP) + SP指定 vo3/vi1 を
 * 与えると、デッキが7枚に膨張する (必須3 + voSP3 + viSP1 = 7)。
 *
 * 原因: ステップ1のSP率先取りがデッキ容量 (所持枠数) を確認せずに spCounts 分を
 * 無条件に push する。必須カードで枠が埋まっている場合に所持枠が溢れ、
 * 「レンタル1枠」ブロック (selected.length < 6) も発火しなくなる。
 *
 * 期待: デッキは常に6枚。SP要求は all/as 型 (アシスト) の重複カバーとレンタル枠で
 * 充足可能 (voSP2 + all型SP1 + レンタルvoSP → vo3/vi1) なので、必ず満たす。
 */
describe('回帰: 非SP必須3枚+SP指定vo3/vi1でデッキが7枚に膨張しない', () => {
  const allCards = loadAllCards();
  const plan = loadPlan('hatsu_legend');
  const character = loadCharacters().find((c) => c.id === 'char_kotone') ?? null;

  // あら、奇遇ね / 二人ならあっという間だね / あなたにも作ってあげる！ (いずれも非SP)
  const REQUIRED = ['SP_SSR_0005', 'SP_SSR_0032', 'SP_SSR_0017'];

  // 所持: sense+free の eligible カードを全所持
  const eligible = allCards.filter(
    (c) =>
      c.plan == null || c.plan === '' || c.plan === 'sense' || c.plan === 'free',
  );
  const uncapLevels: Record<string, number> = {};
  for (const c of eligible) uncapLevels[c.id] = 4;
  uncapLevels['SP_SSR_0005'] = 3; // あら、奇遇ね
  uncapLevels['SP_SSR_0017'] = 2; // あなたにも作ってあげる！
  uncapLevels['SP_SSR_0079'] = 3; // レディ・セット、ゴー！

  function run(cardTypeSlots: Record<string, number>, freeSlots: number) {
    return selectOptimalDeck(
      plan,
      eligible, // candidateCards = 所持のみ
      { vo: 3, da: 0, vi: 2 },
      cardTypeSlots,
      ['vo', 'vi'],
      { vo: 3, vi: 1 },
      'sense',
      undefined,
      uncapLevels,
      allCards, // rentalPool = 全カード
      freeSlots,
      REQUIRED,
      character,
      null,
    );
  }

  const patterns: Array<[Record<string, number>, number, string]> = [
    [{ vo: 2 }, 3, 'Vo2/フリー3'], // ユーザ報告のパターン
    [{}, 5, 'オールフリー'],
  ];

  for (const [slots, free, name] of patterns) {
    it(`${name}: 6枚・重複なし・SP充足・必須全含有・レンタル1枚`, () => {
      const deck = run(slots, free);
      const cards = deckCards(deck);
      expect(cards.length).toBe(6);
      expect(hasNoDuplicates(cards)).toBe(true);
      expect(countSp(cards, 'vo')).toBeGreaterThanOrEqual(3);
      expect(countSp(cards, 'vi')).toBeGreaterThanOrEqual(1);
      const ids = cards.map((c) => c.id);
      for (const r of REQUIRED) expect(ids).toContain(r);
      expect(deck.selected_cards.filter((cs) => cs.is_rental).length).toBe(1);
    });
  }
});
