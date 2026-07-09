import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { calculate } from '../src/services/statusCalculation';
import { loadAllCards, loadPlan, REPO_ROOT } from './helpers/loadRealData';
import { emptyAdditionalCounts } from '../src/types/models';
import type {
  TrainingPlan, TurnChoice, StatusValues, WeekSchedule, Character, MemoryBonus,
} from '../src/types/models';
import type { ActionType } from '../src/types/enums';

/**
 * 回帰テスト: ユーザ報告(2026-07) HIF「未所持カードがレンタル選出されず合計が理論値を下回る」。
 * 倉本千奈 / sense / 所持カードのみ ON / DaSP3 / 必須 パシャっとキメポ(SP_SR_0017) /
 * いつまでも続けばいいのに(SP_SSR_0069) は未所持。
 *
 * バグ (2つの複合):
 * ① inventory は未所持カードを owned:false, uncap:4 で保存するため、uncap だけを見る
 *    isUserOwned4Star が未所持カード全てを「4凸所持 (借用恩恵ゼロ)」と誤判定し、
 *    Pattern A/B/C のレンタル候補プールから除外していた。
 * ② レンタル枠が SP要員 (0071 0凸所持を4凸借用) で確定すると、optimizeRentalCard の
 *    単手入替は SP枚数不足で全滅し、optimizeRentalBorrowUpgrade も借用候補が
 *    「低凸所持カード」限定のため、「未所持カードを借用し、旧レンタルを所持0凸の
 *    SP要員に戻し、弱い1枚を落とす」複合手 (診断②の正解編成) に到達できなかった。
 *
 * 修正: isUserOwned4Star を所持集合との積で判定 (①)、optimizeRentalBorrowUpgrade の
 *       借用候補に rentalPool 内の未所持カードを追加 (②)。
 *
 * 検証 (答え非依存の不変条件): 自動選出の最良合計は、ユーザが必須指定で作れる
 * 手動編成 (0069 4凸レンタル + 0071 0凸 + 0064 + 0057 + 0008 + 0017) を下回らない。
 * teeth: 修正前は自動 6589 < 手動 6631 で赤。修正後は 6631 で緑。
 * 関連: feedback_optimizer_is_the_product / feedback_rental_slot_assignment。
 */

type Choice =
  | { action: 'da_lesson' | 'vo_lesson' | 'vi_lesson'; sub_stat: 'vo' | 'da' | 'vi' }
  | { action: 'vo_class' | 'da_class' | 'vi_class' | 'outing' | 'consultation' | 'activity_supply' | 'special_training' };

const choices: Record<number, Choice> = {
  1: { action: 'activity_supply' },
  2: { action: 'da_lesson', sub_stat: 'vi' },
  3: { action: 'vo_class' },
  4: { action: 'da_lesson', sub_stat: 'vi' },
  5: { action: 'outing' },
  6: { action: 'vo_class' },
  8: { action: 'outing' },
  9: { action: 'da_lesson', sub_stat: 'vi' },
  10: { action: 'vo_class' },
  11: { action: 'da_lesson', sub_stat: 'vi' },
  12: { action: 'consultation' },
  14: { action: 'outing' },
  15: { action: 'da_lesson', sub_stat: 'vi' },
  16: { action: 'consultation' },
  17: { action: 'vo_class' },
  18: { action: 'da_lesson', sub_stat: 'vi' },
  19: { action: 'consultation' },
  21: { action: 'vo_class' },
  22: { action: 'da_lesson', sub_stat: 'vi' },
  23: { action: 'activity_supply' },
  24: { action: 'vo_class' },
  25: { action: 'da_lesson', sub_stat: 'vi' },
  26: { action: 'consultation' },
};
const examAllocations: Record<number, StatusValues> = {
  7: { vo: 0, da: 0, vi: 80 },
  13: { vo: 0, da: 0, vi: 200 },
  20: { vo: 0, da: 0, vi: 220 },
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

describe('HIF 回帰 (ユーザ報告 2026-07: 未所持カードがレンタル選出されない)', () => {
  it('自動選出の最良合計は手動編成 (未所持 0069 を4凸レンタル) を下回らない', () => {
    const allCards = loadAllCards();
    const hifPlan = loadPlan('hif');
    const inventory: InvEntry[] = JSON.parse(
      readFileSync(resolve(REPO_ROOT, 'TestFixtures', 'hif_unowned_rental_inventory.json'), 'utf-8'),
    );

    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    // buildUncapLevels(ownedOnly=true) 相当: 未所持カードも uncap=4 のままエントリされる
    const uncapLevels: Record<string, number> = {};
    for (const e of inventory) uncapLevels[e.card_id] = e.uncap;

    const candidateCards = allCards.filter((c) => ownedIds.has(c.id));
    const rentalPool = [...allCards];

    const { plan: basePlan, turnChoices } = buildPlanAndChoices(hifPlan);
    const plan: TrainingPlan = { ...basePlan, status_limit: basePlan.status_limit + 200 }; // cap Lv6 → 3200

    // 倉本千奈 (3凸OFF/STEP4 ON: 基礎95/125/135, パラボ13/24/21.5) + HIF Lv5 (flat+100/para+10)
    const effectiveChar: Character = {
      id: 'char_china', name: '倉本千奈', color: '#F68B1F', initial: '千',
      base_status_bonus: { vo: 95 + 100, da: 125 + 100, vi: 135 + 100 },
      para_bonus: { vo: 13 + 10, da: 24 + 10, vi: 21.5 + 10 },
    };

    const additionalCounts = {
      ...emptyAdditionalCounts(),
      p_drink_acquire: 16, p_item_acquire: 6, skill_acquire: 20, skill_ssr_acquire: 4,
      skill_enhance: 4, skill_delete: 8, skill_custom: 3, skill_change: 3,
      active_enhance: 3, active_delete: 3, mental_acquire: 8, mental_enhance: 1,
      mental_delete: 3, active_acquire: 8, genki_acquire: 8, good_condition_acquire: 8,
      good_impression_acquire: 8, conserve_acquire: 8, concentrate_acquire: 8,
      motivation_acquire: 8, fullpower_acquire: 8, aggressive_acquire: 8,
    };

    const memory: MemoryBonus = {
      vo: { value: 20, type: 'flat' },
      da: { value: 2.8, type: 'para' },
      vi: { value: 2.8, type: 'para' },
    };
    const memoryBonuses: MemoryBonus[] = [memory, memory, memory, memory];

    const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
    for (const tc of turnChoices) {
      if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
      else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
      else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
    }
    const mainStats = ['da', 'vo'];
    const spCounts = { da: 3 };
    const requiredCardIds = ['SP_SR_0017'];
    const overflowPenalty = { threshold: 100 };

    const patterns = selectMultiplePatternsHif(
      plan, candidateCards, mainStats, lessonAllocation, spCounts, 'sense',
      additionalCounts, uncapLevels, rentalPool, requiredCardIds, effectiveChar,
      memoryBonuses, turnChoices, overflowPenalty,
    );
    expect(patterns.length).toBeGreaterThan(0);

    const cap = plan.status_limit;
    const cappedTotal = (cards: { id: string }[], rentalIds: Set<string>): number => {
      const uc: Record<string, number> = { ...uncapLevels };
      for (const id of rentalIds) uc[id] = 4;
      const fs = calculate(
        plan,
        cards.map((c) => allCards.find((ac) => ac.id === c.id)!),
        turnChoices, uc, additionalCounts, effectiveChar, memoryBonuses,
      ).final_status;
      return Math.min(fs.vo, cap) + Math.min(fs.da, cap) + Math.min(fs.vi, cap);
    };

    // 自動選出の最良合計 (hifStore と同じくキャラ込みキャップ後合計で比較)
    let bestAuto = -Infinity;
    for (const p of patterns) {
      const rentalIds = new Set(p.selected_cards.filter((cs) => cs.is_rental).map((cs) => cs.card.id));
      const total = cappedTotal(p.selected_cards.map((cs) => cs.card), rentalIds);
      if (total > bestAuto) bestAuto = total;
    }

    // 手動編成: 診断②でユーザが必須指定により到達した編成 (自動が下回ったらバグ)
    const manualIds = ['SP_SSR_0069', 'SP_SSR_0071', 'SP_SSR_0064', 'SP_SR_0057', 'SP_SR_0008', 'SP_SR_0017'];
    const manualTotal = cappedTotal(manualIds.map((id) => ({ id })), new Set(['SP_SSR_0069']));

    expect(
      bestAuto,
      `自動選出(${bestAuto})が手動編成(${manualTotal})を下回った: 未所持カードのレンタル候補除外 or 複合手の取り逃し`,
    ).toBeGreaterThanOrEqual(manualTotal);
  });
});
