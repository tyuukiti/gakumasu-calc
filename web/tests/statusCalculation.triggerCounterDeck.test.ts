import { describe, it, expect } from 'vitest';
import { calculate } from '../src/services/statusCalculation';
import { emptyAdditionalCounts } from '../src/types/models';
import type {
  CardEffect,
  SupportCard,
  TrainingPlan,
  TurnChoice,
  WeekSchedule,
  StatusValues,
} from '../src/types/models';
import { makeCard, makePlan } from './helpers/factories';

/**
 * 回帰テスト: 同一カード内の別効果が発動回数カウンタを共有するバグの 6枚編成版
 * (C# 版 ReproTriggerCounterDeckTests と対。単体版は statusCalculation.triggerCounterPerEffect)。
 *
 * 実データにある「通常アビリティ + Pアイテム効果 (同トリガー・同属性)」の型を網羅した合成カード 6枚を
 * 1つの編成に入れ、週次処理 (レッスン / 試験 / 休む) と追加イベント回数の両経路で発火させる。
 * 期待値は「効果ごとに独立した上限回数」で手計算した値。
 *
 *   A 0110型 (1凸): 装備 Vi+20 / スキルチェンジ Vi 27×3 + アイテム 25×3 / 試験終了 Vi 26×2      = 228
 *   B 試験型:        試験終了 Vi 26 (5回まで) + アイテム 10 (2回まで)、試験 2回                   =  72
 *   C SR_0067型:     VoSP終了 Vo 7 (無制限) + アイテム 20 (2回まで)、Voレッスン 2回              =  54
 *   D 0108型:        集中獲得 Da 16 (4回まで) + アイテム 10 (2回まで)、5回                       =  84
 *   E SR_0056型:     好印象獲得 Vi 3 (無制限) + アイテム 30 (1回まで)、4回                       =  42
 *   F 休む型:        休む Da 8 (2回まで) + アイテム 12 (1回まで)、休む 2回 / スキルチェンジ Da 10 (1回) =  38
 *
 * 共有カウンタだと A=151 / B=62 / C=34 / D=58 / E=12 / F=26 になり、全カードが期待値を下回る。
 * 併せて「6枚の寄与 = 各カード単体の寄与の和」(カード間で干渉しない) も確認する。
 */

function eff(
  trigger: string,
  stat: string,
  values: number[],
  opts: { max?: number; item?: boolean } = {},
): CardEffect {
  const e: CardEffect = { trigger, stat, values, value_type: 'flat' };
  if (opts.max != null) e.max_count = opts.max;
  if (opts.item) {
    e.source = 'item';
    e.condition = `${stat}>=400`;
  }
  return e;
}
const five = (v: number) => [v, v, v, v, v];

const cardA = makeCard({
  id: 'A_0110型', type: 'vi',
  effects: [
    { trigger: 'equip', stat: 'vi', values: five(20), value_type: 'flat', event_param: true },
    eff('skill_change', 'vi', [27, 27, 27, 39, 39], { max: 3 }),
    eff('exam_end', 'vi', [26, 26, 26, 26, 37], { max: 5 }),
    eff('skill_change', 'vi', five(25), { max: 3, item: true }),
  ],
});
const cardB = makeCard({
  id: 'B_試験型', type: 'vi',
  effects: [eff('exam_end', 'vi', five(26), { max: 5 }), eff('exam_end', 'vi', five(10), { max: 2, item: true })],
});
const cardC = makeCard({
  id: 'C_SR0067型', type: 'vo',
  effects: [eff('vo_sp_end', 'vo', five(7)), eff('vo_sp_end', 'vo', five(20), { max: 2, item: true })],
});
const cardD = makeCard({
  id: 'D_0108型', type: 'da',
  effects: [eff('concentrate_acquire', 'da', five(16), { max: 4 }), eff('concentrate_acquire', 'da', five(10), { max: 2, item: true })],
});
const cardE = makeCard({
  id: 'E_SR0056型', type: 'vi',
  effects: [eff('good_impression_acquire', 'vi', five(3)), eff('good_impression_acquire', 'vi', five(30), { max: 1, item: true })],
});
const cardF = makeCard({
  id: 'F_休む型', type: 'da',
  effects: [
    eff('rest', 'da', five(8), { max: 2 }),
    eff('rest', 'da', five(12), { max: 1, item: true }),
    eff('skill_change', 'da', five(10), { max: 1 }),
  ],
});

const deck = [cardA, cardB, cardC, cardD, cardE, cardF];
const uncapLevels: Record<string, number> = { [cardA.id]: 1 };
for (const c of deck) if (c !== cardA) uncapLevels[c.id] = 4;

const expectedByCard: Array<[string, SupportCard, StatusValues]> = [
  ['A 0110型', cardA, { vo: 0, da: 0, vi: 228 }],
  ['B 試験型', cardB, { vo: 0, da: 0, vi: 72 }],
  ['C SR_0067型', cardC, { vo: 54, da: 0, vi: 0 }],
  ['D 0108型', cardD, { vo: 0, da: 84, vi: 0 }],
  ['E SR_0056型', cardE, { vo: 0, da: 0, vi: 42 }],
  ['F 休む型', cardF, { vo: 0, da: 38, vi: 0 }],
];
const expectedDeck: StatusValues = { vo: 54, da: 122, vi: 342 };

/** Voレッスン×2 → 試験×2 → 休む×2 の 6週 */
function buildPlan(): { plan: TrainingPlan; turnChoices: TurnChoice[] } {
  const base = makePlan({ lessonWeeks: { vo: 2 }, lessonGain: 100 });
  const blank = { available_actions: [] as string[], lessons: [], classes: [] };
  const extra: WeekSchedule[] = [
    { week: 3, type: 'fixed_event', event_name: '試験1', ...blank },
    { week: 4, type: 'fixed_event', event_name: '試験2', ...blank },
    { week: 5, type: 'normal', ...blank, available_actions: ['rest'] },
    { week: 6, type: 'normal', ...blank, available_actions: ['rest'] },
  ];
  const plan: TrainingPlan = { ...base, schedule: [...base.schedule, ...extra], total_weeks: 6 };
  const turnChoices: TurnChoice[] = [
    { week: 1, chosen_action: 'vo_lesson' },
    { week: 2, chosen_action: 'vo_lesson' },
    { week: 5, chosen_action: 'rest' },
    { week: 6, chosen_action: 'rest' },
  ];
  return { plan, turnChoices };
}

const counts = { ...emptyAdditionalCounts(), skill_change: 3, concentrate_acquire: 5, good_impression_acquire: 4 };

/** カード群の寄与 = (カードあり) − (カードなし) の最終ステータス差 */
function contribution(cards: SupportCard[]): StatusValues {
  const { plan, turnChoices } = buildPlan();
  const withCards = calculate(plan, cards, turnChoices, uncapLevels, counts).final_status;
  const baseline = calculate(plan, [], turnChoices, uncapLevels, counts).final_status;
  return { vo: withCards.vo - baseline.vo, da: withCards.da - baseline.da, vi: withCards.vi - baseline.vi };
}

describe('6枚編成: 同一カード内の同トリガー・同属性の効果が週次処理と追加イベントの両経路で独立に上限まで発動する', () => {
  it.each(expectedByCard)('単体: %s の寄与が効果ごとの上限回数で計算される', (_name, card, expected) => {
    expect(contribution([card])).toEqual(expected);
  });

  it('6枚編成の寄与が全カードの期待値の合計に一致する', () => {
    expect(contribution(deck)).toEqual(expectedDeck);
  });

  it('6枚編成の寄与 = 各カード単体の寄与の和 (カード間でカウンタが干渉しない)', () => {
    const sum: StatusValues = { vo: 0, da: 0, vi: 0 };
    for (const card of deck) {
      const c = contribution([card]);
      sum.vo += c.vo;
      sum.da += c.da;
      sum.vi += c.vi;
    }
    expect(contribution(deck)).toEqual(sum);
  });
});
