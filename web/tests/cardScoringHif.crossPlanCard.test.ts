import { describe, it, expect } from 'vitest';
import { selectMultiplePatternsHif } from '../src/services/cardScoring';
import { loadAllCards, loadPlan } from './helpers/loadRealData';
import { countSp, coversSpStat, deckCards, hasNoDuplicates } from './helpers/constraints';
import { emptyAdditionalCounts } from '../src/types/models';
import type {
  TrainingPlan, TurnChoice, StatusValues, WeekSchedule, Character, MemoryBonus, SupportCard,
} from '../src/types/models';
import type { ActionType } from '../src/types/enums';

/**
 * 回帰テスト: ユーザ報告(2026-09) HIF「別プラン (sense) のサポカが1枚だけ選出される」。
 * 十王星南 / logic / 所持カードのみ ON / VoSP3 /
 * 必須3枚: 私の目に狂いはない(SP_SSR_0007, 3凸) / やっと見つけたぞ！(SP_SSR_0081, 2凸) /
 * 進化したお弁当、気になる(SP_SSR_0094, 1凸)。
 * 診断情報ではレンタル枠に もう一度、最初から！(SP_SSR_0044, plan: sense) が入っていた。
 *
 * 原因: 必須3枚で所持枠が3つ埋まり、ステップ1のSP先取りは所持枠上限 (5) で打ち切られるため
 *       VoSP は所持2枚までしか確保できない。残り1枚は enforceSpCounts が「レンタル枠を
 *       レンタルプールの VoSP カードに差し替える」経路で補充するが、この経路だけ
 *       rentalPool を planType でフィルタしておらず、別プラン (sense/anomaly) の SP カードが
 *       素の寄与順で先頭に来るとそのまま採用される (入口はこの1経路なので混入は最大1枚)。
 *       レンタル枠は 2.11.1 で「SP 1枚の受け皿」として恒常的に使われるようになり顕在化した。
 *       同じ差し替えは必須カードが載ったレンタル枠 (ensureRentalSlot が低凸の必須カードを
 *       借用先に指定したケース) も対象にしてしまい、必須カードがデッキから消える。
 *       さらに別プランカードが所持カードだった場合、後続の optimizeRentalAssignment が
 *       レンタルフラグを別カードへ付け替えるため「所持サポカが別プラン」にも見える。
 *
 * 検証 (答え非依存の不変条件): 全パターンで 6枚・重複なし・必須3枚全含有・VoSP>=3・
 * レンタル1枚、かつ必須指定でない全カードが planType (logic) または free/無指定 であること。
 * teeth: 修正前は
 *   - ケース「報告編成の再現」: 診断情報と同一の編成 (0044 sense がレンタル) になり赤
 *   - ケース「プラン内全4凸所持」: 5枚編成・必須 0094 脱落・0044 sense レンタルで赤
 *   - ケース「全カード所持」: 0044 sense が所持枠に残り (レンタルは必須 0081 へ付替) 赤
 */

type Choice =
  | { action: 'da_lesson' | 'vo_lesson' | 'vi_lesson'; sub_stat: 'vo' | 'da' | 'vi' }
  | { action: 'vo_class' | 'da_class' | 'vi_class' | 'outing' | 'consultation' | 'activity_supply' | 'special_training' };

// 診断情報 [HIFスケジュール] の再構築 (Voレッスン/サブVi, Vi授業)
const choices: Record<number, Choice> = {
  1: { action: 'activity_supply' },
  2: { action: 'vo_lesson', sub_stat: 'vi' },
  3: { action: 'vi_class' },
  4: { action: 'vo_lesson', sub_stat: 'vi' },
  5: { action: 'outing' },
  6: { action: 'vi_class' },
  8: { action: 'outing' },
  9: { action: 'vo_lesson', sub_stat: 'vi' },
  10: { action: 'vi_class' },
  11: { action: 'vo_lesson', sub_stat: 'vi' },
  12: { action: 'consultation' },
  14: { action: 'outing' },
  15: { action: 'vo_lesson', sub_stat: 'vi' },
  16: { action: 'consultation' },
  17: { action: 'vi_class' },
  18: { action: 'vo_lesson', sub_stat: 'vi' },
  19: { action: 'consultation' },
  21: { action: 'vi_class' },
  22: { action: 'vo_lesson', sub_stat: 'vi' },
  23: { action: 'activity_supply' },
  24: { action: 'vi_class' },
  25: { action: 'vo_lesson', sub_stat: 'vi' },
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

const PLAN_TYPE = 'logic';
const REQUIRED = ['SP_SSR_0007', 'SP_SSR_0081', 'SP_SSR_0094'];
// 診断情報で所持枠に入っていた VoSP 2枚 (可愛いと可愛いで可愛い！ / 今はあえて、背を向けて)
const REPORTED_OWNED_VO_SP = new Set(['SP_SSR_0008', 'SP_SR_0014']);

function planOk(card: SupportCard): boolean {
  return card.plan == null || card.plan === '' || card.plan === PLAN_TYPE || card.plan === 'free';
}

/**
 * 所持セットの定義。診断情報にインベントリは含まれないため、報告編成を再現する所持条件を
 * 再構築する。共通: 必須3枚は報告の凸数 (0094:1 / 0007:3 / 0081:2)、それ以外の所持は4凸。
 */
const INVENTORIES: Array<[string, (c: SupportCard) => boolean]> = [
  [
    // 報告編成の再現: logic/free のうち非必須の vi 型カードは未所持 (レンタル候補が非SPのみになる)、
    // VoSP は報告どおり 0008 / SR_0014 の2枚のみ所持。
    // → Pattern A/B/C は非SPの vi カードをレンタルに選び、enforceSpCounts がそれを
    //   レンタルプール先頭の VoSP (= 0044 sense) に差し替える。
    '報告編成の再現 (VoSP は 0008/SR_0014 のみ所持・vi 型非必須は未所持)',
    (c) =>
      planOk(c) &&
      !(c.type === 'vi' && !REQUIRED.includes(c.id)) &&
      (!coversSpStat(c, 'vo') || REPORTED_OWNED_VO_SP.has(c.id)),
  ],
  [
    // プラン内カードを全て4凸所持: 4凸所持カードはレンタル候補から除外されるため
    // 候補が (デッキ内の) 必須カードだけになり、レンタル枠が立たず ensureRentalSlot が
    // 低凸の必須 0094 を借用先に指定 → enforceSpCounts がその必須カードを 0044 に差し替える。
    'プラン内 (logic/free) 全4凸所持',
    (c) => planOk(c),
  ],
  [
    // 全カード所持 (別プラン含む): 0044 が所持カードなので、レンタル差し替え後に
    // optimizeRentalAssignment がレンタルフラグを低凸の必須カードへ付け替え、
    // 0044 (sense) が所持枠に残る = 「所持サポカが別プラン」の症状。
    '全カード所持 (別プラン含む)',
    () => true,
  ],
];

describe('HIF 回帰 (ユーザ報告 2026-09: 別プランのサポカが選出される)', () => {
  const allCards = loadAllCards();
  const hifPlan = loadPlan('hif');
  const rentalPool = [...allCards];

  const { plan: basePlan, turnChoices } = buildPlanAndChoices(hifPlan);
  const plan: TrainingPlan = { ...basePlan, status_limit: basePlan.status_limit + 200 }; // 本戦上限増加 Lv6 → 3200

  // 十王星南 + HIF Lv5: 診断情報の「キャラ補正(実効)」をそのまま使う
  const effectiveChar: Character = {
    id: 'char_sena', name: '十王星南', color: '#F6AE54', initial: '星',
    base_status_bonus: { vo: 175, da: 125, vi: 140 },
    para_bonus: { vo: 15, da: 8, vi: 20.5 },
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
    vo: { value: 2.8, type: 'para' },
    da: { value: 20, type: 'flat' },
    vi: { value: 2.8, type: 'para' },
  };
  const memoryBonuses: MemoryBonus[] = [memory, memory, memory, memory];

  const lessonAllocation: Record<string, number> = { vo: 0, da: 0, vi: 0 };
  for (const tc of turnChoices) {
    if (tc.chosen_action === 'vo_lesson') lessonAllocation.vo++;
    else if (tc.chosen_action === 'da_lesson') lessonAllocation.da++;
    else if (tc.chosen_action === 'vi_lesson') lessonAllocation.vi++;
  }
  const mainStats = ['vo', 'da']; // inferMainStats: Voレッスンのみ → vo, タイブレークで da
  const spCounts = { vo: 3 };
  const overflowPenalty = { threshold: 100 };

  for (const [name, ownedPred] of INVENTORIES) {
    it(`${name}: 6枚・必須全含有・VoSP>=3・レンタル1枚・必須以外は logic/free のみ`, () => {
      const ownedIds = new Set(allCards.filter(ownedPred).map((c) => c.id));
      // buildUncapLevels(ownedOnly=true) 相当: 未所持カードも uncap=4 でエントリされる
      const uncapLevels: Record<string, number> = {};
      for (const c of allCards) uncapLevels[c.id] = 4;
      uncapLevels['SP_SSR_0094'] = 1; // 進化したお弁当、気になる
      uncapLevels['SP_SSR_0007'] = 3; // 私の目に狂いはない
      uncapLevels['SP_SSR_0081'] = 2; // やっと見つけたぞ！
      const candidateCards = allCards.filter((c) => ownedIds.has(c.id));

      const patterns = selectMultiplePatternsHif(
        plan, candidateCards, mainStats, lessonAllocation, spCounts, PLAN_TYPE,
        additionalCounts, uncapLevels, rentalPool, REQUIRED, effectiveChar,
        memoryBonuses, turnChoices, overflowPenalty,
      );
      expect(patterns.length).toBeGreaterThan(0);

      for (const p of patterns) {
        const cards = deckCards(p);
        const desc = p.selected_cards
          .map((cs) => `${cs.card.id}(${cs.card.plan})${cs.is_rental ? '[R]' : ''}${cs.is_required ? '[必]' : ''}`)
          .join(', ');

        // 本題: 必須指定でないカードに別プランが混ざらない (レンタル・所持を問わず)
        const crossPlan = p.selected_cards.filter((cs) => !cs.is_required && !planOk(cs.card));
        expect(
          crossPlan.map((cs) => `${cs.card.id}(${cs.card.plan})`),
          `${p.label}: 別プランのカードが選出された [${desc}]`,
        ).toEqual([]);

        // 基本不変条件 (必須 > SP枚数 > 編成パターン / 6枚 / レンタル1枚)
        expect(cards.length, `${p.label}: 6枚編成 [${desc}]`).toBe(6);
        expect(hasNoDuplicates(cards), `${p.label}: 重複なし`).toBe(true);
        const ids = new Set(cards.map((c) => c.id));
        for (const r of REQUIRED) expect(ids.has(r), `${p.label}: 必須 ${r} 含有 [${desc}]`).toBe(true);
        expect(countSp(cards, 'vo'), `${p.label}: VoSP>=3 [${desc}]`).toBeGreaterThanOrEqual(3);
        expect(p.selected_cards.filter((cs) => cs.is_rental).length, `${p.label}: レンタル1枚`).toBe(1);
      }
    });
  }
});
