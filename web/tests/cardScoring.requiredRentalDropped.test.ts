import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, REPO_ROOT } from './helpers/loadRealData';
import { emptyAdditionalCounts } from '../src/types/models';
import { deckCards, countSp, hasNoDuplicates } from './helpers/constraints';
import type { Character, MemoryBonus, TurnChoice } from '../src/types/models';
import type { ActionType } from '../src/types/enums';

/**
 * 回帰 (ユーザ報告 2026-08): hatsu_legend/anomaly で未所持の必須カード
 * ｖギャルピーーースッｖ (SP_SSR_0073, vo型SP) がレンタル枠を他カードに吸われ編成から漏れる。
 * 雨夜燕 / 所持カードのみ ON / SP vo3/da2 / 必須3枚 = 0019(vo SP, 所持) + 0014(da 非SP, 所持)
 * + 0073(vo SP, 未所持=レンタル必須)。
 *
 * バグ: ステップ0で未所持必須カードは requiredRentalCard として保留されるが、そのSP率が
 * spCountsForFill から減算されない。ステップ1が vo SP を1枚過剰確保して所持枠が
 * 2(必須)+4(SP補充)=6枚に達し、「レンタル1枠」ブロックの selected.length < 6 が偽になって
 * requiredRentalCard が黙って捨てられる (ensureRentalSlot は枠を立て直すだけで必須は戻さない)。
 *
 * 修正: ①ステップ0で必須レンタルカードのSP率も spCountsForFill から減算、
 *       ②レンタル投入前に所持枠が埋まっていたら最弱の非必須カードを落として必ず枠を空ける。
 *
 * 期待: 全パターンで 6枚・重複なし・必須3枚全含有・未所持の 0073 がレンタル枠・SP充足。
 */

interface InvEntry { card_id: string; owned: boolean; uncap: number }

// 診断情報の日程 (W10/W18 は固定イベントのため選択なし)
const CHOICES: Record<number, string> = {
  1: 'da_class', 2: 'da_class', 3: 'activity_supply', 4: 'da_lesson',
  5: 'consultation', 6: 'vo_class', 7: 'da_lesson', 8: 'consultation',
  9: 'special_training', 11: 'activity_supply', 12: 'da_lesson',
  13: 'consultation', 14: 'da_lesson', 15: 'vo_class', 16: 'vo_lesson',
  17: 'consultation',
};

describe('回帰: 未所持の必須カードがレンタル枠から漏れない (hatsu_legend)', () => {
  it('全パターンで必須3枚を含み、未所持必須がレンタル枠に乗る', () => {
    const allCards = loadAllCards();
    const plan = loadPlan('hatsu_legend');
    const inventory: InvEntry[] = JSON.parse(
      readFileSync(resolve(REPO_ROOT, 'TestFixtures', 'hif_repro_inventory.json'), 'utf-8'),
    );
    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    const uncapLevels: Record<string, number> = {};
    for (const e of inventory) uncapLevels[e.card_id] = e.uncap;
    // 診断時点の凸数に合わせる
    uncapLevels['SP_SSR_0098'] = 2;
    uncapLevels['SP_SR_0069'] = 4;

    const candidateCards = allCards.filter((c) => ownedIds.has(c.id));
    const rentalPool = [...allCards];

    const turnChoices: TurnChoice[] = Object.entries(CHOICES).map(([w, a]) => ({
      week: Number(w),
      chosen_action: a as ActionType,
    }));

    const lessonAllocation = { vo: 1, da: 4, vi: 0 };
    const mainStats = ['da', 'vo'];
    const spCounts = { vo: 3, da: 2 };
    const REQUIRED = ['SP_SSR_0019', 'SP_SSR_0014', 'SP_SSR_0073'];
    const UNOWNED_REQUIRED = 'SP_SSR_0073';

    // 雨夜燕 (3凸OFF) の実効キャラ補正 (診断値)
    const effectiveChar: Character = {
      id: 'char_tsubame', name: '雨夜燕', color: '#7B68EE', initial: '燕',
      base_status_bonus: { vo: 115, da: 140, vi: 110 },
      para_bonus: { vo: 17, da: 20, vi: 13 },
    };

    const additionalCounts = {
      ...emptyAdditionalCounts(),
      p_drink_acquire: 7, p_item_acquire: 6, skill_acquire: 15, skill_ssr_acquire: 4,
      skill_enhance: 4, skill_delete: 5, skill_custom: 3, skill_change: 3,
      active_enhance: 3, active_delete: 3, mental_acquire: 8, mental_enhance: 3,
      mental_delete: 3, active_acquire: 8, genki_acquire: 8, good_condition_acquire: 8,
      good_impression_acquire: 8, conserve_acquire: 8, concentrate_acquire: 8,
      motivation_acquire: 8, fullpower_acquire: 8, aggressive_acquire: 8,
    };

    const memory: MemoryBonus = {
      vo: { value: 2.8, type: 'para' },
      da: { value: 2.8, type: 'para' },
      vi: { value: 20, type: 'flat' },
    };
    const memoryBonuses: MemoryBonus[] = [memory, memory, memory, memory];

    const patterns = selectMultiplePatternsHif(
      plan, candidateCards, mainStats, lessonAllocation, spCounts, 'anomaly',
      additionalCounts, uncapLevels, rentalPool, REQUIRED, effectiveChar,
      memoryBonuses, turnChoices, undefined,
    );
    expect(patterns.length).toBeGreaterThan(0);

    for (const p of patterns) {
      const cards = deckCards(p);
      expect(cards.length, `${p.label}: 6枚`).toBe(6);
      expect(hasNoDuplicates(cards), `${p.label}: 重複なし`).toBe(true);
      const ids = cards.map((c) => c.id);
      for (const r of REQUIRED) {
        expect(ids, `${p.label}: 必須カード ${r} が編成に含まれること`).toContain(r);
      }
      // 未所持の必須カードは必ずレンタル枠に乗る
      const rentals = p.selected_cards.filter((cs) => cs.is_rental);
      expect(rentals.length, `${p.label}: レンタル1枚`).toBe(1);
      expect(rentals[0].card.id, `${p.label}: 未所持必須がレンタル枠`).toBe(UNOWNED_REQUIRED);
      // SP枚数も維持される (必須と両立可能な構成)
      expect(countSp(cards, 'vo'), `${p.label}: vo SP>=3`).toBeGreaterThanOrEqual(3);
      expect(countSp(cards, 'da'), `${p.label}: da SP>=2`).toBeGreaterThanOrEqual(2);
    }
  });
});
