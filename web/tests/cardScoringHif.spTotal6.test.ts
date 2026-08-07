import { describe, it, expect } from 'vitest';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { loadAllCards, loadPlan } from './helpers/loadRealData';
import type { SupportCard } from '../src/types/models';
import type { DeckResult } from '../src/types/results';

/**
 * 回帰テスト: issue #145「SP枚数設定が多いときに『有効な編成パターンが見つかりませんでした』」。
 * 初Legend / 所持カードのみON相当 / SP Vo=4+Da=2 (合計6) /
 * 必須2枚: 食欲の秋なんです(SP_SSR_0036, as型SP all) + 相手にとって不足なしよ！(SP_SSR_0053, vi型SP・未所持→レンタル)。
 *
 * バグ: selectMultiplePatternsHif のスキップ判定 `spShortage > free` がレンタル枠(6枚目)を
 *       吸収容量に数えておらず、SP合計6では4パターン全てがスキップされ結果0件 → エラー表示。
 *       下流 (selectOptimalDeck の Step1 SP先取り + enforceSpCounts) は同条件で制約を満たす
 *       デッキを組めることを確認済みで、事前スキップ判定だけが唯一のブロッカーだった。
 * 修正: 吸収容量に `rentalPool != null ? 1 : 0` を加算 (C# SelectMultiplePatternsHif と対)。
 *
 * 検証 (答え非依存の不変条件): SP合計6でもパターンが1件以上返り、各パターンは
 * 6枚編成・必須カード全含・SP枚数充足・レンタルちょうど1枚を満たす。
 * teeth: 修正前はパターン0件で赤。
 * 関連: feedback_constraint_enforcement_pattern / feedback_fix_both_csharp_and_web。
 */

const SP_COUNTS = { vo: 4, da: 2 };
const REQUIRED = ['SP_SSR_0036', 'SP_SSR_0053'];

function coversSpStat(card: SupportCard, stat: string): boolean {
  return card.effects.some(
    (e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'),
  );
}

function assertDeckInvariants(patterns: DeckResult[], requiredIds: string[]) {
  expect(patterns.length).toBeGreaterThan(0);
  for (const p of patterns) {
    const cards = p.selected_cards.map((cs) => cs.card);
    expect(cards.length, `${p.label}: 6枚編成であること`).toBe(6);
    const ids = new Set(cards.map((c) => c.id));
    for (const req of requiredIds) {
      expect(ids.has(req), `${p.label}: 必須カード ${req} が編成に含まれること`).toBe(true);
    }
    const voSp = cards.filter((c) => coversSpStat(c, 'vo')).length;
    const daSp = cards.filter((c) => coversSpStat(c, 'da')).length;
    expect(voSp, `${p.label}: VoSP枚数`).toBeGreaterThanOrEqual(SP_COUNTS.vo);
    expect(daSp, `${p.label}: DaSP枚数`).toBeGreaterThanOrEqual(SP_COUNTS.da);
    const rentals = p.selected_cards.filter((cs) => cs.is_rental);
    expect(rentals.length, `${p.label}: レンタル枠が1枚であること`).toBe(1);
  }
}

describe('issue #145 回帰: SP合計6でもパターンが返る', () => {
  const allCards = loadAllCards();
  const plan = loadPlan('hatsu_legend');
  // 所持カードのみON相当: 診断情報でレンタル扱いだった SP_SSR_0053 のみ未所持
  const owned = allCards.filter((c) => c.id !== 'SP_SSR_0053');
  const rentalPool = [...allCards];
  // ユーザ日程相当: Voレッスン2回 / Daレッスン3回
  const lessonAllocation = { vo: 2, da: 3, vi: 0 };

  it('必須2枚あり (報告シナリオ): パターンが返り、必須+SP枚数+レンタル1枚を満たす', () => {
    const patterns = selectMultiplePatternsHif(
      plan, owned, ['vo', 'da'], lessonAllocation, SP_COUNTS, 'anomaly',
      undefined, undefined, rentalPool, REQUIRED, null, null, undefined, undefined,
    );
    assertDeckInvariants(patterns, REQUIRED);
  });

  it('必須なしでも SP合計6 でパターンが返る (SP先取りで所持枠が埋まる overfill 経路)', () => {
    const patterns = selectMultiplePatternsHif(
      plan, [...allCards], ['vo', 'da'], lessonAllocation, SP_COUNTS, 'anomaly',
      undefined, undefined, rentalPool, undefined, null, null, undefined, undefined,
    );
    assertDeckInvariants(patterns, []);
  });

  it('レンタルなし (rentalPool 無指定) では従来どおりフリー枠のみで判定される', () => {
    // 吸収容量+1はレンタル枠がある場合のみ。レンタルなしで SP合計6 は
    // フリー5パターンでも吸収不能 (6 > 5) のため従来どおり0件になる。
    const patterns = selectMultiplePatternsHif(
      plan, [...allCards], ['vo', 'da'], lessonAllocation, SP_COUNTS, 'anomaly',
      undefined, undefined, undefined, undefined, null, null, undefined, undefined,
    );
    expect(patterns.length).toBe(0);
  });
});
