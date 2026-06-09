import { describe, it, expect } from 'vitest';
import { selectOptimalDeck } from '../src/services/cardScoring';
import { makeCard, makePlan, resetIds } from './helpers/factories';
import { rentalCard, deckCards, hasNoDuplicates } from './helpers/constraints';
import type { SupportCard } from '../src/types/models';

/**
 * L1/L3: レンタル枠割当の不変条件。
 * レンタル枠は「どの 1 枚を 4 凸借用するか」。4 凸所持カードを浪費せず、
 * 借用すべき強カード (未所持/低凸) を選ぶこと。
 * 参照: feedback_rental_slot_assignment
 */

const ALLOC = { vo: 0, da: 0, vi: 0 };

describe('L1: レンタル枠割当', () => {
  it('レンタルモードでデッキは 6 枚・レンタル枠ちょうど 1・重複なし', () => {
    resetIds();
    const plan = makePlan({ statusLimit: 99999, baseStatus: { vo: 0, da: 0, vi: 0 } });
    const owned: SupportCard[] = [
      makeCard({ id: 'O1', type: 'vo', equip: { vo: 200 } }),
      makeCard({ id: 'O2', type: 'vo', equip: { vo: 180 } }),
      makeCard({ id: 'O3', type: 'da', equip: { da: 170 } }),
      makeCard({ id: 'O4', type: 'da', equip: { da: 160 } }),
      makeCard({ id: 'O5', type: 'vi', equip: { vi: 150 } }),
      makeCard({ id: 'O6', type: 'vi', equip: { vi: 100 } }),
    ];
    const u1 = makeCard({ id: 'U1', type: 'vo', equip: { vo: 400 } });
    const rentalPool = [...owned, u1];
    const uncapLevels: Record<string, number> = {};
    for (const c of [...owned, u1]) uncapLevels[c.id] = 4;

    const deck = selectOptimalDeck(
      plan, owned, ALLOC, {}, ['vo', 'da'],
      undefined, undefined, undefined, uncapLevels, rentalPool, 5,
    );

    const cards = deckCards(deck);
    expect(cards.length).toBe(6);
    expect(hasNoDuplicates(cards)).toBe(true);
    expect(deck.selected_cards.filter((c) => c.is_rental).length).toBe(1);
  });

  it('未所持の強カードをレンタルで借用する (4凸所持カードを浪費しない)', () => {
    resetIds();
    const plan = makePlan({ statusLimit: 99999, baseStatus: { vo: 0, da: 0, vi: 0 } });
    const owned: SupportCard[] = [
      makeCard({ id: 'O1', type: 'vo', equip: { vo: 200 } }),
      makeCard({ id: 'O2', type: 'vo', equip: { vo: 180 } }),
      makeCard({ id: 'O3', type: 'da', equip: { da: 170 } }),
      makeCard({ id: 'O4', type: 'da', equip: { da: 160 } }),
      makeCard({ id: 'O5', type: 'vi', equip: { vi: 150 } }),
      makeCard({ id: 'O6', type: 'vi', equip: { vi: 100 } }),
    ];
    const u1 = makeCard({ id: 'U1', type: 'vo', equip: { vo: 400 } }); // 未所持・最強
    const rentalPool = [...owned, u1];
    const uncapLevels: Record<string, number> = {};
    for (const c of [...owned, u1]) uncapLevels[c.id] = 4;

    const deck = selectOptimalDeck(
      plan, owned, ALLOC, {}, ['vo', 'da'],
      undefined, undefined, undefined, uncapLevels, rentalPool, 5,
    );

    const rental = rentalCard(deck);
    expect(rental).toBeDefined();
    // 借用すべきは最強の未所持カード U1。4凸所持カードをレンタルにしない。
    expect(rental!.card.id).toBe('U1');
    // U1 は所持枠 (5枚) には現れない (レンタル枠でのみ使う)
    const ownedSlotIds = deck.selected_cards.filter((c) => !c.is_rental).map((c) => c.card.id);
    expect(ownedSlotIds).not.toContain('U1');
  });
});
