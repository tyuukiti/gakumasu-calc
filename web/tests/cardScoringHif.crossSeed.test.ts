import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { calculate } from '../src/services/statusCalculation';
import { loadAllCards, loadPlan, loadCharacters, REPO_ROOT } from './helpers/loadRealData';
import { emptyAdditionalCounts } from '../src/types/models';
import type {
  TrainingPlan, TurnChoice, StatusValues, WeekSchedule, ActionType, SupportCard, Character,
} from '../src/types/models';

/**
 * 回帰テスト: ユーザ報告(2026-06) HIF 診断シナリオ。
 * リーリヤ + HIF Lv5 (flat+100/para+10%, 本戦上限+200 => cap3200), DaSP3, アノマリー,
 * 所持カードのみ, exam配分 80/200/220 (全Vi)。
 *
 * バグ: 自動編成が「Da偏重・Viカード0枚」の局所最適 (Dance2/Free3, 6354) に落ち、
 *       ほっぺた等の高Viカードを使う balanced 最適 (6418) を逃していた。
 * 修正: selectMultiplePatternsHif の cross-seed 大域最適化 (フリー5を全パターンの種から
 *       joint 単一スワップ山登り)。
 * 検証: 自動最良 ≧ ユーザが手動で組める ほっぺた入りデッキ。
 *       ([[feedback_optimizer_is_the_product]]: 自動が手動に負けたらバグ)
 */

type Choice = { action: string; sub_stat?: 'vo' | 'da' | 'vi' };
const choices: Record<number, Choice> = {
  1: { action: 'activity_supply' }, 2: { action: 'da_lesson', sub_stat: 'vi' }, 3: { action: 'vo_class' },
  4: { action: 'da_lesson', sub_stat: 'vi' }, 5: { action: 'outing' }, 6: { action: 'vo_class' },
  8: { action: 'activity_supply' }, 9: { action: 'da_lesson', sub_stat: 'vi' }, 10: { action: 'vo_class' },
  11: { action: 'da_lesson', sub_stat: 'vi' }, 12: { action: 'consultation' }, 14: { action: 'activity_supply' },
  15: { action: 'da_lesson', sub_stat: 'vi' }, 16: { action: 'activity_supply' }, 17: { action: 'vo_class' },
  18: { action: 'da_lesson', sub_stat: 'vi' }, 19: { action: 'consultation' }, 21: { action: 'vo_class' },
  22: { action: 'da_lesson', sub_stat: 'vi' }, 23: { action: 'activity_supply' }, 24: { action: 'vo_class' },
  25: { action: 'da_lesson', sub_stat: 'vi' }, 26: { action: 'consultation' },
};
const examAllocations: Record<number, StatusValues> = {
  7: { vo: 0, da: 0, vi: 80 }, 13: { vo: 0, da: 0, vi: 200 }, 20: { vo: 0, da: 0, vi: 220 },
};

// hifStore.buildPlanAndChoices の複製
function buildPlanAndChoices(hifPlan: TrainingPlan): { plan: TrainingPlan; turnChoices: TurnChoice[] } {
  const newSchedule: WeekSchedule[] = hifPlan.schedule.map((w) => {
    if (w.type === 'public_lesson') {
      const choice = choices[w.week];
      if (!choice || !choice.sub_stat) return { ...w, lessons: [...w.lessons] };
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

describe('HIF cross-seed 回帰 (ユーザ報告 2026-06: 上限張り付き属性での balanced 最適)', () => {
  it('自動最良 ≧ 手動 ほっぺた入りデッキ (Da偏重局所最適に落ちない)', () => {
    const allCards = loadAllCards();
    const hifPlan = loadPlan('hif');
    const lilja = loadCharacters().find((c) => c.id === 'char_lilja')!;
    const inventory: InvEntry[] = JSON.parse(
      readFileSync(resolve(REPO_ROOT, 'TestFixtures', 'hif_repro_inventory.json'), 'utf-8'),
    );

    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    const candidateCards = allCards.filter((c) => ownedIds.has(c.id));
    const uncapLevels: Record<string, number> = {};
    for (const e of inventory) uncapLevels[e.card_id] = e.uncap;
    const rentalPool = allCards;

    const { plan: basePlan, turnChoices } = buildPlanAndChoices(hifPlan);
    const plan: TrainingPlan = { ...basePlan, status_limit: basePlan.status_limit + 200 };

    const effectiveChar: Character = {
      id: 'char_lilja', name: lilja.name, color: lilja.color, initial: lilja.initial,
      base_status_bonus: {
        vo: lilja.base_status_bonus.vo + 100, da: lilja.base_status_bonus.da + 100, vi: lilja.base_status_bonus.vi + 100,
      },
      para_bonus: { vo: lilja.para_bonus.vo + 10, da: lilja.para_bonus.da + 10, vi: lilja.para_bonus.vi + 10 },
      uncap3_bonus: lilja.uncap3_bonus, step4_bonus: lilja.step4_bonus,
    };

    const additionalCounts = {
      ...emptyAdditionalCounts(),
      p_drink_acquire: 15, p_item_acquire: 6, skill_acquire: 20, skill_ssr_acquire: 8,
      skill_enhance: 4, skill_delete: 2, skill_custom: 3, skill_change: 3,
      active_enhance: 3, active_delete: 2, mental_acquire: 8, mental_enhance: 1,
      mental_delete: 2, active_acquire: 8, conserve_acquire: 8, fullpower_acquire: 8,
      aggressive_acquire: 4, consultation_drink: 6,
    };

    const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
    for (const tc of turnChoices) {
      if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
      else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
      else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
    }
    const mainStats = ['da', 'vo'];
    const spCounts = { da: 3 };
    const cap = plan.status_limit;

    const score = (cards: SupportCard[], rentalId?: string): number => {
      const uc = { ...uncapLevels };
      if (rentalId) uc[rentalId] = 4;
      const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, effectiveChar, null).final_status;
      return Math.min(fs.vo, cap) + Math.min(fs.da, cap) + Math.min(fs.vi, cap);
    };

    const patterns = selectMultiplePatternsHif(
      plan, candidateCards, mainStats, lessonAllocation, spCounts, 'anomaly',
      additionalCounts, uncapLevels, rentalPool, undefined, effectiveChar, null, turnChoices, undefined,
    );

    let autoBest = -Infinity;
    let autoBestCards: SupportCard[] = [];
    for (const p of patterns) {
      const cards = p.selected_cards.map((cs) => cs.card);
      const rentalId = p.selected_cards.find((cs) => cs.is_rental)?.card.id;
      const s = score(cards, rentalId);
      if (s > autoBest) { autoBest = s; autoBestCards = cards; }
    }

    // ユーザが手動で組める ほっぺた入りデッキ (0059=レンタル4凸): 修正前の自動(6354)はこれに負けていた
    const manualIds = ['SP_SSR_0069', 'SP_SSR_0059', 'SP_SSR_0084', 'SP_SR_0010', 'SP_SR_0071', 'SP_SR_0008'];
    const manualDeck = manualIds.map((id) => allCards.find((c) => c.id === id)!);
    const manualScore = score(manualDeck, 'SP_SSR_0059');

    expect(autoBest).toBeGreaterThanOrEqual(manualScore - 1e-6);
    // balanced 最適は Viカードを含む (Da偏重・Vi0枚の退化編成ではない)
    expect(autoBestCards.some((c) => c.type === 'vi')).toBe(true);
  });
});
