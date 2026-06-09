import { describe, it, expect } from 'vitest';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, templateAdditionalCounts } from './helpers/loadRealData';
import { scoreDeck, totalGte } from './helpers/scoreDeck';
import { hifTurnChoices, lessonAllocationFrom } from './helpers/hif';
import { hasNoDuplicates, deckCards, coversSpStat } from './helpers/constraints';
import type { SupportCard, AdditionalCounts } from '../src/types/models';

/**
 * HIFモード (selectMultiplePatternsHif) の実データテスト。
 *
 * HIF は通常モードと別の入口・別ロジック (ターン選択 override / オールフリーパターン)。
 * - メイン属性は順序込みの全6通り (vo/da, vo/vi, da/vo, da/vi, vi/vo, vi/da) を検証する。
 *   HIF はメイン1のレッスンを多めに割り当てるため、順序が変わると結果も変わりうる。
 * - イベント回数は実テンプレート (HIF「センス」) を選択して additionalCounts に渡す。
 *   これによりトリガー系カード (Pドリンク獲得時+Vo 等) のスコアも実運用と同条件で評価される。
 * - 選択の turnChoicesOverride と採点 (scoreDeck) で同一の turnChoices / additionalCounts を共有。
 *
 * 注: HIF の「オールフリー」パターンは単一属性6枚も生成できる (通常モードと異なりサブ強制なし)。
 */

const plan = loadPlan('hif');
const allCards = loadAllCards();
const counts: AdditionalCounts = templateAdditionalCounts('hif', 'センス');

// メイン属性の順序込み全6通り
const orderedPairs: string[][] = [
  ['vo', 'da'], ['vo', 'vi'],
  ['da', 'vo'], ['da', 'vi'],
  ['vi', 'vo'], ['vi', 'da'],
];

function autoPatterns(mainStats: string[], spCounts?: Record<string, number>) {
  const tc = hifTurnChoices(plan, mainStats);
  const alloc = lessonAllocationFrom(tc);
  const patterns = selectMultiplePatternsHif(
    plan, allCards, mainStats, alloc,
    spCounts, undefined, counts, undefined, undefined, undefined,
    null, null, tc,
  );
  return { patterns, tc };
}

describe('HIF: 全パターンが6枚・重複なし (実データ + テンプレ適用)', () => {
  for (const mainStats of orderedPairs) {
    it(`main=${mainStats.join('/')}`, () => {
      const { patterns } = autoPatterns(mainStats);
      expect(patterns.length).toBeGreaterThan(0);
      for (const p of patterns) {
        const cards = deckCards(p);
        expect(cards.length).toBe(6);
        expect(hasNoDuplicates(cards)).toBe(true);
      }
    });
  }
});

describe('HIF: 自動編成 ≧ 手動編成 (実データ + テンプレ適用)', () => {
  for (const mainStats of orderedPairs) {
    it(`main=${mainStats.join('/')}: 自動 ≧ 手動[単体寄与トップ6]`, () => {
      const { patterns, tc } = autoPatterns(mainStats);
      let auto = -Infinity;
      for (const p of patterns) {
        const s = scoreDeck(plan, deckCards(p), mainStats, { turnChoices: tc, additionalCounts: counts }).cappedTotal;
        if (s > auto) auto = s;
      }
      // HIF はオールフリーで単型も組めるため、型偏重編成 (単体寄与トップ6) も比較対象にできる
      const manual: SupportCard[] = [...allCards]
        .map((c) => ({ c, v: scoreDeck(plan, [c], mainStats, { turnChoices: tc, additionalCounts: counts }).cappedTotal }))
        .sort((a, b) => b.v - a.v)
        .slice(0, 6)
        .map((x) => x.c);
      const manualScore = scoreDeck(plan, manual, mainStats, { turnChoices: tc, additionalCounts: counts }).cappedTotal;
      expect(totalGte(auto, manualScore)).toBe(true);
    });
  }
});

describe('HIF: SP指定で全パターンがSP制約を満たす', () => {
  it('main=vo/da, vo SP 2枚', () => {
    const { patterns } = autoPatterns(['vo', 'da'], { vo: 2 });
    for (const p of patterns) {
      const voSp = deckCards(p).filter((c) => coversSpStat(c, 'vo')).length;
      expect(voSp).toBeGreaterThanOrEqual(2);
    }
  });
});
