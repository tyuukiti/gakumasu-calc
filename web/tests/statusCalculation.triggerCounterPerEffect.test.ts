import { describe, it, expect } from 'vitest';
import {
  calculate,
  getEffectValue,
  getEventParamBoostPercent,
} from '../src/services/statusCalculation';
import { emptyAdditionalCounts } from '../src/types/models';
import type { CardEffect, SupportCard } from '../src/types/models';
import { makeCard, makePlan } from './helpers/factories';
import { getCard } from './helpers/loadRealData';

/**
 * 回帰テスト: 同一カード内の別効果が発動回数カウンタを共有する (C# 版 ReproTriggerCounterPerEffectTests と対)。
 *
 * fireTrigger の発動回数カウンタのキーが `カードID_トリガー_属性` だったため、同じトリガー・同じ属性の
 * flat 効果を2つ持つカード (通常アビリティ + Pアイテム効果。例: もうすぐ本番ですね SP_SSR_0110) では、
 * 後続の効果が前の効果の消費分だけ早く max_count に達し、合計が期待値より低くなっていた。
 * 実例: 0110 を1凸で採用した編成が不採用の編成より合計が低くなる (最適化側は効果ごとに独立に見積もるため採用する)。
 *
 * 期待仕様: 同一カード・同一トリガー・同一属性でも、別々の効果はそれぞれ独立した上限回数として扱う。
 */

function triggerEffect(
  trigger: string,
  stat: string,
  values: number[],
  opts: { max?: number; item?: boolean } = {},
): CardEffect {
  const e: CardEffect = { trigger, stat, values, value_type: 'flat' };
  if (opts.max != null) e.max_count = opts.max;
  if (opts.item) {
    e.source = 'item';
    e.condition = 'vi>=400';
  }
  return e;
}

/** スケジュールなしのプランで trigger を fires 回発火し、最終ステータスを返す。 */
function fireAndGet(
  cards: SupportCard[],
  trigger: string,
  fires: number,
  uncap = 4,
  baseVi = 0,
) {
  const plan = makePlan({ baseStatus: { vi: baseVi } });
  const uncapLevels: Record<string, number> = {};
  for (const c of cards) uncapLevels[c.id] = uncap;
  const counts = { ...emptyAdditionalCounts(), [trigger]: fires };
  return calculate(plan, cards, [], uncapLevels, counts).final_status;
}

const five = (v: number) => [v, v, v, v, v];

describe('同一カード内の同一トリガー・同一属性の flat 効果は上限回数を共有しない', () => {
  it('max_count が異なる2効果 (3回 + 2回) は、それぞれの上限まで発動する', () => {
    const card = makeCard({
      type: 'vi',
      effects: [
        triggerEffect('skill_change', 'vi', five(30), { max: 3 }),
        triggerEffect('skill_change', 'vi', five(20), { max: 2, item: true }),
      ],
    });
    // 5回発火: 30×3 + 20×2 = 130 (共有カウンタだと 30+20+30 = 80 で止まる)
    expect(fireAndGet([card], 'skill_change', 5).vi).toBe(130);
  });

  it('上限なし効果と上限付き効果 (1回) の組は、上限付き側も発動する', () => {
    const card = makeCard({
      type: 'vi',
      effects: [
        triggerEffect('good_impression_acquire', 'vi', five(5)),
        triggerEffect('good_impression_acquire', 'vi', five(30), { max: 1, item: true }),
      ],
    });
    // 4回発火: 5×4 + 30×1 = 50 (共有カウンタだと上限付き側が一度も発動せず 20)
    expect(fireAndGet([card], 'good_impression_acquire', 4).vi).toBe(50);
  });

  it.each([
    [4, 192], // 39×3 + 25×3
    [1, 156], // 27×3 + 25×3
    [0, 156],
  ])('凸数 %i でも各効果が上限回数まで発動する (期待 %i)', (uncap, expected) => {
    const card = makeCard({
      type: 'vi',
      effects: [
        triggerEffect('skill_change', 'vi', [27, 27, 27, 39, 39], { max: 3 }),
        triggerEffect('skill_change', 'vi', five(25), { max: 3, item: true }),
      ],
    });
    expect(fireAndGet([card], 'skill_change', 3, uncap).vi).toBe(expected);
  });

  it.each([4, 1])('実データ: もうすぐ本番ですね (SP_SSR_0110) %i凸・スキルチェンジ3回', (uncap) => {
    const card = getCard('SP_SSR_0110');
    const changes = card.effects.filter(
      (e) => e.trigger === 'skill_change' && e.value_type === 'flat' && e.stat === 'vi',
    );
    // 前提: 通常アビリティ + Pアイテム効果の2効果 (データが変わったらこのテストの意味も変わる)
    expect(changes).toHaveLength(2);

    // 初期値ボーナス: event_param 付き flat には同カードの event_param_boost% が乗る
    const boostMul = 1 + getEventParamBoostPercent(card, uncap) / 100;
    const equip = card.effects
      .filter((e) => e.trigger === 'equip' && e.value_type === 'flat' && e.stat === 'vi')
      .reduce((s, e) => {
        const raw = getEffectValue(e, uncap);
        return s + Math.floor(e.event_param ? raw * boostMul : raw);
      }, 0);
    const expected = changes.reduce(
      (s, e) => s + getEffectValue(e, uncap) * Math.min(3, e.max_count ?? 3),
      0,
    );
    // Pアイテム条件 (vi>=400) を満たすよう初期 Vi=400
    expect(fireAndGet([card], 'skill_change', 3, uncap, 400).vi).toBe(400 + equip + expected);
  });
});

describe('カウンタの独立性 (修正前後とも成立すべき不変条件)', () => {
  it('別カードの同一トリガー・同一属性は互いに独立', () => {
    const a = makeCard({ type: 'vi', effects: [triggerEffect('skill_change', 'vi', five(10), { max: 1 })] });
    const b = makeCard({ type: 'vi', effects: [triggerEffect('skill_change', 'vi', five(10), { max: 1 })] });
    expect(fireAndGet([a, b], 'skill_change', 2).vi).toBe(20);
  });

  it('同一カードの同一トリガー・別属性は互いに独立', () => {
    const card = makeCard({
      type: 'all',
      effects: [
        triggerEffect('skill_change', 'vo', five(10), { max: 1 }),
        triggerEffect('skill_change', 'vi', five(10), { max: 1 }),
      ],
    });
    const fs = fireAndGet([card], 'skill_change', 2);
    expect(fs.vo).toBe(10);
    expect(fs.vi).toBe(10);
  });
});
