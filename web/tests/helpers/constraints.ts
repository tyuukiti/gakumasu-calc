import type { SupportCard } from '../../src/types/models';
import type { CardScore, DeckResult } from '../../src/types/results';

/**
 * 編成制約を検証する述語群。最適化器が満たすべき不変条件のアサートに使う。
 * SP 率カードの判定は cardScoring.ts の enforceSpCounts と同じ定義に揃えてある。
 */

const WILDCARD_TYPES = new Set(['all', 'as']);

/** カードが指定属性の SP 率を持つか (enforceSpCounts の coversStat と同義)。 */
export function coversSpStat(card: SupportCard, stat: string): boolean {
  return card.effects.some(
    (e) =>
      e.trigger === 'equip' &&
      e.value_type === 'sp_rate' &&
      (e.stat === stat || e.stat === 'all'),
  );
}

/** デッキ内の指定属性 SP カード枚数。 */
export function countSp(cards: SupportCard[], stat: string): number {
  return cards.filter((c) => coversSpStat(c, stat)).length;
}

/** card.type==stat または all/as ワイルドのカード枚数 (属性枠を埋められる枚数)。 */
export function countTypeSlotFillable(cards: SupportCard[], stat: string): number {
  return cards.filter((c) => c.type === stat || WILDCARD_TYPES.has(c.type)).length;
}

/** 重複カード ID がないか。 */
export function hasNoDuplicates(cards: SupportCard[]): boolean {
  return new Set(cards.map((c) => c.id)).size === cards.length;
}

/** DeckResult からカード配列を取り出す。 */
export function deckCards(deck: DeckResult): SupportCard[] {
  return deck.selected_cards.map((cs) => cs.card);
}

/** レンタル枠の CardScore (なければ undefined)。 */
export function rentalCard(deck: DeckResult): CardScore | undefined {
  return deck.selected_cards.find((cs) => cs.is_rental);
}

export { WILDCARD_TYPES };
