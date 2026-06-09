import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { calculate } from '../src/services/statusCalculation';
import { loadAllCards, loadPlan, loadCharacters, REPO_ROOT } from './helpers/loadRealData';
import { bruteForceOptimalRental } from './helpers/bruteForce';
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
 *
 * 検証方針 (重要): 既存スイートがこのバグを取りこぼした構造的原因は、
 *   ① 総当たりオラクルが合成小プール専用かつレンタル枠を非モデル化 → 実データ最適性が未検証
 *   ② 実データのテストが「auto ≧ 素朴な手動編成」のみ → バグ自動でも素朴手動には勝てて素通り
 * だった。ここでは **レンタル枠対応の総当たりオラクルを実データ(寄与上位プール)に適用**し、
 * 「自動最良 ≧ 独立に総当たりで求めた最適」を検証する。答えを事前に知らなくても
 * このクラスのバグ(局所最適への落ち込み)を捕捉できる。
 * ([[feedback_optimizer_is_the_product]]: 自動が(独立に求めた)最適に負けたらバグ)
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
  it('自動最良 ≧ 総当たり最適 (実データ・レンタル枠考慮)', () => {
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
    for (const p of patterns) {
      const cards = p.selected_cards.map((cs) => cs.card);
      const rentalId = p.selected_cards.find((cs) => cs.is_rental)?.card.id;
      const s = score(cards, rentalId);
      if (s > autoBest) autoBest = s;
    }

    // --- 独立した総当たりオラクル (実データ・レンタル枠考慮) ---
    // 「5枚(所持凸) + レンタル1枚(4凸)」を全列挙し SP(da>=3) を満たす真の最大を求める。
    // プール = 各パターンが選んだカードの和集合 (= 最適化器自身が surface した属性多様な部品。
    // 個別寄与が低くても最適編成に入る札 0008 等も含む) + レンタル候補の Da上位 (0059 等)。
    // バグの本質は「部品は各パターンに出ているが、それらを跨いだ最良の組合せを作れない」点なので、
    // この和集合を総当たりすれば答えを事前に知らなくても最適を独立に再現できる。
    const planOk = (c: SupportCard) => c.plan == null || c.plan === '' || c.plan === 'anomaly' || c.plan === 'free';
    const patternCardIds = new Set(patterns.flatMap((p) => p.selected_cards.map((cs) => cs.card.id)));
    const soloTotal = (c: SupportCard, asRental: boolean): number => {
      const uc = { ...uncapLevels };
      if (asRental) uc[c.id] = 4;
      const fs = calculate(plan, [c], turnChoices, uc, additionalCounts, effectiveChar, null).final_status;
      return Math.min(fs.vo, cap) + Math.min(fs.da, cap) + Math.min(fs.vi, cap);
    };
    const topDa = (pool: SupportCard[], n: number) => pool
      .filter((c) => planOk(c) && c.type === 'da')
      .sort((a, b) => soloTotal(b, true) - soloTotal(a, true)).slice(0, n);
    const dedupe = (cards: SupportCard[]) => [...new Map(cards.map((c) => [c.id, c])).values()];

    const ownedBF = candidateCards.filter((c) => patternCardIds.has(c.id) && planOk(c));
    const rentalBF = dedupe([
      ...rentalPool.filter((c) => patternCardIds.has(c.id) && planOk(c)),
      ...topDa(rentalPool, 5),
    ]);

    const coversDaSp = (c: SupportCard) =>
      c.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === 'da' || e.stat === 'all'));
    const validDeck = (deck: SupportCard[]) =>
      new Set(deck.map((c) => c.id)).size === deck.length && deck.filter(coversDaSp).length >= 3;

    const bf = bruteForceOptimalRental(ownedBF, rentalBF, 6, (deck, rentalId) => score(deck, rentalId), validDeck);

    // teeth: オラクルは旧出荷の局所最適(6354)を独立に上回るデッキを実際に見つけている
    // (= 修正前なら autoBest(6354) < bf.bestTotal となりこのテストは赤になる)
    expect(bf.bestTotal).toBeGreaterThan(6354);
    // 本検証: 自動最良は独立に求めた総当たり最適を下回ってはならない
    expect(autoBest).toBeGreaterThanOrEqual(bf.bestTotal - 1e-6);
  });
});
