import { describe, it, expect } from 'vitest';
import { selectOptimalDeck } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, loadCharacters } from './helpers/loadRealData';
import { deckCards, countSp, hasNoDuplicates } from './helpers/constraints';

/**
 * 回帰 (ユーザ報告 2026-07): hatsu_legend/anomaly で必須4枚 (内 all型SP=食欲, vi型SP=のんびり)
 * + SP指定 da2/vi3 を与えると、デッキが7枚に膨張し非SPカードがレンタル枠を浪費する。
 *
 * 原因: ステップ0で all型SP必須カードを spCountsForFill から減算する際 break で1属性しか
 * 減算されず、ステップ1が vi SPを過剰確保 → 所持枠が6枚に膨張しレンタルが乗って7枚。
 * 修正: all型SPはカバーする全属性を減算する (break 除去)。
 *
 * 期待: デッキは常に6枚、SP枚数 (da>=2, vi>=3) を満たし、必須4枚を全て含み、レンタルは1枚。
 */
describe('回帰: 必須+SP指定でデッキが7枚に膨張しない', () => {
  const allCards = loadAllCards();
  const plan = loadPlan('hatsu_legend');
  const character = loadCharacters().find((c) => c.id === 'char_saki') ?? null;

  const REQUIRED = ['SP_SSR_0007', 'SP_SR_0073', 'SP_SSR_0014', 'SP_SSR_0036'];

  // 所持: anomaly+free の eligible カードを全所持 (ひとりごと SP_SSR_0058 は非所持=レンタル専用)。
  // ひとりごとは強Viの非SPカードで、7枚バグ時にレンタル枠を吸っていた対象。
  const OWNED_EXCLUDE = new Set(['SP_SSR_0058']);
  const eligible = allCards.filter(
    (c) =>
      (c.plan == null || c.plan === '' || c.plan === 'anomaly' || c.plan === 'free') &&
      !OWNED_EXCLUDE.has(c.id),
  );
  const uncapLevels: Record<string, number> = {};
  for (const c of eligible) uncapLevels[c.id] = 4;
  uncapLevels['SP_SSR_0007'] = 2; // 私の目
  uncapLevels['SP_SR_0073'] = 1; // のんびり

  function run(cardTypeSlots: Record<string, number>, freeSlots: number) {
    return selectOptimalDeck(
      plan,
      eligible, // candidateCards = 所持のみ
      { da: 5, vi: 5, vo: 0 },
      cardTypeSlots,
      ['da', 'vi'],
      { da: 2, vi: 3 },
      'anomaly',
      undefined,
      uncapLevels,
      allCards, // rentalPool = 全カード
      freeSlots,
      REQUIRED,
      character,
      null,
    );
  }

  // spCounts=da2/vi3 で selectMultiplePatternsHif が実際に試すパターン
  // (Vo2/フリー3 は SP不足でスキップされるため対象外)
  const patterns: Array<[Record<string, number>, number, string]> = [
    [{ da: 2 }, 3, 'Da2/フリー3'], // ユーザ報告のパターン
    [{ vi: 2 }, 3, 'Vi2/フリー3'],
    [{}, 5, 'オールフリー'],
  ];

  for (const [slots, free, name] of patterns) {
    it(`${name}: 6枚・重複なし・SP充足・必須全含有・レンタル1枚`, () => {
      const deck = run(slots, free);
      const cards = deckCards(deck);
      expect(cards.length).toBe(6);
      expect(hasNoDuplicates(cards)).toBe(true);
      expect(countSp(cards, 'da')).toBeGreaterThanOrEqual(2);
      expect(countSp(cards, 'vi')).toBeGreaterThanOrEqual(3);
      const ids = cards.map((c) => c.id);
      for (const r of REQUIRED) expect(ids).toContain(r);
      expect(deck.selected_cards.filter((cs) => cs.is_rental).length).toBe(1);
    });
  }
});
