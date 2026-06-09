import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { loadAllCards, loadPlan, loadCharacters, REPO_ROOT } from './helpers/loadRealData';
import { emptyAdditionalCounts } from '../src/types/models';
import type {
  TrainingPlan, TurnChoice, StatusValues, WeekSchedule, SupportCard, Character,
} from '../src/types/models';
import type { ActionType } from '../src/types/enums';

/**
 * 回帰テスト: ユーザ報告(2026-06) HIF 診断シナリオ「必須カードを増やすとレンタル枠が消える」。
 * 紫雲清夏 / sense / 所持カードのみ ON / コンテストモード ON / DaSP2 / 必須4枚 (いずれも Da-SP 非カバー)。
 *
 * バグ: 必須カード4枚 (step0) + DaSP補充2枚 (step1) で所持枠が6枚埋まり、「レンタル1枠」選出ブロック
 *       (selected.length < 6) が発火しないため is_rental が1枚も立たず、レンタル枠が消える。
 *       さらに上限張り付き(Da capped)で借用が score-tie になると、後続の付け替えも 4凸所持カードに
 *       レンタルが乗ったまま (0凸カードがデッキ内にあるのにレンタルは 4凸) になっていた。
 * 修正: selectOptimalDeck/crossSeed で ensureRentalSlot により必ずレンタル枠を確保し、
 *       optimizeRentalAssignment の同点タイブレークを「最低凸カード優先」にした。
 *
 * 検証 (答え非依存の不変条件):
 *   ① 所持カードのみ ON では各パターンに必ずレンタル枠が1枚存在する (= 消えない)。
 *   ② 全カード所持のデッキでは、レンタルはデッキ内最低凸の所持カードに乗る
 *      (4凸所持カードをレンタルに据える浪費をしない。借用は4凸なので低凸ほど恩恵が大きい)。
 * teeth: 修正前は ① が rental=0 で赤、ensureRentalSlot のみでは ② が rental=4凸 で赤になる。
 * 関連: feedback_rental_slot_assignment / feedback_constraint_enforcement_pattern。
 */

type Choice =
  | { action: 'da_lesson' | 'vo_lesson' | 'vi_lesson'; sub_stat: 'vo' | 'da' | 'vi' }
  | { action: 'vo_class' | 'da_class' | 'vi_class' | 'outing' | 'consultation' | 'activity_supply' | 'special_training' };

const choices: Record<number, Choice> = {
  1: { action: 'activity_supply' }, 2: { action: 'da_lesson', sub_stat: 'vo' }, 3: { action: 'vi_class' },
  4: { action: 'da_lesson', sub_stat: 'vo' }, 5: { action: 'outing' }, 6: { action: 'vi_class' },
  8: { action: 'activity_supply' }, 9: { action: 'da_lesson', sub_stat: 'vo' }, 10: { action: 'vi_class' },
  11: { action: 'da_lesson', sub_stat: 'vo' }, 12: { action: 'consultation' }, 14: { action: 'activity_supply' },
  15: { action: 'da_lesson', sub_stat: 'vo' }, 16: { action: 'activity_supply' }, 17: { action: 'vo_class' },
  18: { action: 'da_lesson', sub_stat: 'vo' }, 19: { action: 'consultation' }, 21: { action: 'vo_class' },
  22: { action: 'da_lesson', sub_stat: 'vo' }, 23: { action: 'activity_supply' }, 24: { action: 'vo_class' },
  25: { action: 'da_lesson', sub_stat: 'vo' }, 26: { action: 'consultation' },
};
const examAllocations: Record<number, StatusValues> = {
  7: { vo: 28, da: 26, vi: 26 }, 13: { vo: 68, da: 66, vi: 66 }, 20: { vo: 74, da: 73, vi: 73 },
};

// hifStore.buildPlanAndChoices の複製
function buildPlanAndChoices(hifPlan: TrainingPlan): { plan: TrainingPlan; turnChoices: TurnChoice[] } {
  const newSchedule: WeekSchedule[] = hifPlan.schedule.map((w) => {
    if (w.type === 'public_lesson') {
      const choice = choices[w.week] as Extract<Choice, { sub_stat: 'vo' | 'da' | 'vi' }> | undefined;
      if (!choice || !('sub_stat' in choice)) return { ...w, lessons: [...w.lessons] };
      const mainStat = choice.action.split('_')[0] as 'vo' | 'da' | 'vi';
      const subStat = choice.sub_stat;
      const mainValue = (w.lessons.find((l) => l.type === mainStat)?.sp_bonus[mainStat] ?? 0) as number;
      const subValue = w.hif_sub_value ?? 0;
      const newLessons = w.lessons.map((l) => {
        if (l.type !== mainStat) return l;
        const sp: StatusValues = { vo: 0, da: 0, vi: 0 };
        sp[mainStat] = mainValue;
        sp[subStat] = (sp[subStat] ?? 0) + subValue;
        return { ...l, sp_bonus: sp };
      });
      return { ...w, lessons: newLessons };
    }
    if (w.type === 'audition' && (w.hif_exam_base != null || w.hif_exam_distributed != null)) {
      const base = w.hif_exam_base ?? 0;
      const alloc = examAllocations[w.week] ?? { vo: 0, da: 0, vi: 0 };
      const status_gain: StatusValues = {
        vo: base + Math.max(0, Math.floor(alloc.vo)),
        da: base + Math.max(0, Math.floor(alloc.da)),
        vi: base + Math.max(0, Math.floor(alloc.vi)),
      };
      return { ...w, status_gain };
    }
    return w;
  });
  const newPlan: TrainingPlan = { ...hifPlan, schedule: newSchedule };
  const turnChoices: TurnChoice[] = [];
  for (const w of newSchedule) {
    if (w.type === 'audition' || w.type === 'fixed_event' || w.type === 'exam') continue;
    if (w.available_actions.length === 0) continue;
    const choice = choices[w.week];
    if (!choice) continue;
    turnChoices.push({ week: w.week, chosen_action: choice.action as ActionType });
  }
  return { plan: newPlan, turnChoices };
}

interface InvEntry { card_id: string; owned: boolean; uncap: number }

describe('HIF 回帰 (ユーザ報告 2026-06: 必須カードを増やすとレンタル枠が消える)', () => {
  it('所持のみ ON では各パターンにレンタルが1枚存在し、最低凸カードに乗る', () => {
    const allCards = loadAllCards();
    const hifPlan = loadPlan('hif');
    const sumika = loadCharacters().find((c) => c.id === 'char_sumika')!;
    const inventory: InvEntry[] = JSON.parse(
      readFileSync(resolve(REPO_ROOT, 'TestFixtures', 'hif_repro_inventory.json'), 'utf-8'),
    );

    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    const uncapLevels: Record<string, number> = {};
    for (const e of inventory) uncapLevels[e.card_id] = e.uncap;

    // contestMode ON: skill / exam_item を除外
    const contestFilter = (c: SupportCard) => c.tag !== 'skill' && c.tag !== 'exam_item';
    const candidateCards = allCards.filter((c) => ownedIds.has(c.id) && contestFilter(c));
    const rentalPool = allCards.filter(contestFilter);

    // 必須4枚: いずれも Da-SP を持たない → step1 が DaSP を2枚補充して所持枠が6枚に達する (overfill)。
    // 4枚とも exam_item/skill タグなので contestMode で候補から外れる。hifStore と同様に
    // 所持済み必須カードを candidateCards / rentalPool へ再投入する (これがないと必須が無視され overfill が起きない)。
    const requiredCardIds = ['SP_SSR_0014', 'SP_SSR_0005', 'SP_SSR_0069', 'SP_SSR_0002'];
    const candSet = new Set(candidateCards.map((c) => c.id));
    const rentalSet = new Set(rentalPool.map((c) => c.id));
    for (const card of allCards) {
      if (!requiredCardIds.includes(card.id)) continue;
      if (ownedIds.has(card.id) && !candSet.has(card.id)) candidateCards.push(card);
      if (!rentalSet.has(card.id)) rentalPool.push(card);
    }

    const { plan: basePlan, turnChoices } = buildPlanAndChoices(hifPlan);
    const plan: TrainingPlan = { ...basePlan, status_limit: basePlan.status_limit + 200 }; // finalCap Lv6

    // sense キャラ + HIFボーナス Lv5 (flat+100/para+10%)。invariant は補正値に依存しないが診断を再現。
    const effectiveChar: Character = {
      id: 'char_sumika', name: sumika.name, color: sumika.color, initial: sumika.initial,
      base_status_bonus: {
        vo: sumika.base_status_bonus.vo + 100, da: sumika.base_status_bonus.da + 100, vi: sumika.base_status_bonus.vi + 100,
      },
      para_bonus: { vo: sumika.para_bonus.vo + 10, da: sumika.para_bonus.da + 10, vi: sumika.para_bonus.vi + 10 },
      uncap3_bonus: sumika.uncap3_bonus, step4_bonus: sumika.step4_bonus,
    };

    const additionalCounts = {
      ...emptyAdditionalCounts(),
      p_drink_acquire: 15, p_item_acquire: 6, skill_acquire: 20, skill_ssr_acquire: 8,
      skill_enhance: 4, skill_delete: 2, skill_custom: 3, skill_change: 3,
      active_enhance: 3, active_delete: 2, mental_acquire: 8, mental_enhance: 1,
      mental_delete: 2, active_acquire: 8, good_condition_acquire: 8, concentrate_acquire: 8,
      consultation_drink: 6,
    };

    const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
    for (const tc of turnChoices) {
      if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
      else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
      else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
    }
    const mainStats = ['da', 'vo'];
    const spCounts = { da: 2 };

    const patterns = selectMultiplePatternsHif(
      plan, candidateCards, mainStats, lessonAllocation, spCounts, 'sense',
      additionalCounts, uncapLevels, rentalPool, requiredCardIds, effectiveChar, null, turnChoices, undefined,
    );

    expect(patterns.length).toBeGreaterThan(0);
    for (const p of patterns) {
      const ids = new Set(p.selected_cards.map((cs) => cs.card.id));
      // 前提: 必須4枚が実際に編成へ入っている (= overfill が起きる条件が成立している)
      for (const req of requiredCardIds) {
        expect(ids.has(req), `${p.label}: 必須カード ${req} が編成に含まれること`).toBe(true);
      }
      const rentals = p.selected_cards.filter((cs) => cs.is_rental);
      // ① レンタルは消えない: 各パターンにちょうど1枚
      expect(rentals.length, `${p.label}: レンタル枠が1枚であること`).toBe(1);

      // ② 全カード所持なら、レンタルはデッキ内最低凸の所持カードに乗る
      const allOwned = p.selected_cards.every((cs) => ownedIds.has(cs.card.id));
      if (allOwned) {
        const minUncap = Math.min(...p.selected_cards.map((cs) => uncapLevels[cs.card.id] ?? 4));
        const rentalUncap = uncapLevels[rentals[0].card.id] ?? 4;
        expect(rentalUncap, `${p.label}: レンタルは最低凸(${minUncap})カードに乗るべき`).toBe(minUncap);
      }
    }
  });
});
