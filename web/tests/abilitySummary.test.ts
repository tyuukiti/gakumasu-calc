import { describe, it, expect } from 'vitest';
import { buildAbilitySummary, selectMultiplePatterns } from '../src/services/cardScoring';
import { loadAllCards, loadPlan } from './helpers/loadRealData';
import type { SupportCard, CardEffect } from '../src/types/models';
import type { CardScore } from '../src/types/results';

function mkEffect(partial: Partial<CardEffect> & { trigger: string; stat: string; values: number[]; value_type: string }): CardEffect {
  return { ...partial };
}

function mkCard(id: string, effects: CardEffect[]): SupportCard {
  return { id, name: id, rarity: 'ssr', type: 'vo', plan: '', effects };
}

function mkScore(card: SupportCard, isRental = false): CardScore {
  return {
    card,
    total_value: 0,
    raw_vo: 0,
    raw_da: 0,
    raw_vi: 0,
    team_bonus_total: 0,
    team_bonus_contributors: [],
    breakdowns: [],
    is_rental: isRental,
    is_required: false,
    uncap_level: 4,
  };
}

describe('buildAbilitySummary (アビリティまとめ・行動別)', () => {
  const cardA = mkCard('A', [mkEffect({ trigger: 'class_end', stat: 'vo', value_type: 'flat', values: [45] })]);
  const cardB = mkCard('B', [mkEffect({ trigger: 'class_end', stat: 'vo', value_type: 'flat', values: [30] })]);
  const cardC = mkCard('C', [mkEffect({ trigger: 'mental_acquire', stat: 'vi', value_type: 'flat', values: [40] })]);
  // 装備(初期値) と パラボ は行動選択で変動しないため除外される
  const cardD = mkCard('D', [mkEffect({ trigger: 'equip', stat: 'vo', value_type: 'flat', values: [800] })]);
  const cardE = mkCard('E', [mkEffect({ trigger: 'equip', stat: 'vo', value_type: 'para_bonus', values: [30] })]);
  // max_count=2 → class_end が6回でも2回ぶんしか発火しない
  const cardF = mkCard('F', [mkEffect({ trigger: 'class_end', stat: 'da', value_type: 'flat', values: [20], max_count: 2 })]);

  const selected = [cardA, cardB, cardC, cardD, cardE, cardF].map((c) => mkScore(c));
  const triggerCounts = { class_end: 6, mental_acquire: 3 };

  const entries = buildAbilitySummary(selected, triggerCounts, undefined);

  it('同一トリガーでまとまり、グループ合計降順・グループ内は Vo→Da→Vi 順に並ぶ', () => {
    // class_end グループ合計 = 450(vo) + 40(da) = 490 > mental_acquire 120 なので先に来る。
    // class_end 内は Vo→Da の順。
    expect(entries.map((e) => `${e.trigger}/${e.stat}`)).toEqual([
      'class_end/vo',
      'class_end/da',
      'mental_acquire/vi',
    ]);
  });

  it('per_fire は各カードの per-fire の和、parts は降順、total = per_fire × 発動回数', () => {
    const vo = entries.find((e) => e.trigger === 'class_end' && e.stat === 'vo')!;
    expect(vo.per_fire).toBe(75);
    expect(vo.parts).toEqual([45, 30]);
    expect(vo.fires).toBe(6);
    expect(vo.total).toBe(450);

    const mental = entries.find((e) => e.trigger === 'mental_acquire')!;
    expect(mental.trigger_name).toBe('メンタル獲得');
    expect(mental.total).toBe(120);
  });

  it('max_count は実効発動回数を制限する (total は正確、表示の fires は行動の発動回数)', () => {
    const da = entries.find((e) => e.trigger === 'class_end' && e.stat === 'da')!;
    expect(da.per_fire).toBe(20);
    expect(da.fires).toBe(6); // 表示上の発動回数
    expect(da.total).toBe(40); // 20 × min(6, max_count=2)
  });

  it('装備(初期値)・パラボは除外される', () => {
    expect(entries.some((e) => e.trigger === 'equip')).toBe(false);
  });

  it('発動回数0のトリガーは出ない', () => {
    const only = buildAbilitySummary([mkScore(cardA)], { class_end: 0 }, undefined);
    expect(only).toEqual([]);
  });
});

describe('buildAbilitySummary 統合 (実データ・selectMultiplePatterns 経由)', () => {
  const plan = loadPlan('hatsu_legend');
  const cards = loadAllCards();
  const patterns = selectMultiplePatterns(plan, cards, ['vo', 'da'], 'vi',
    plan.schedule.filter((w) => w.lessons.length > 0 && w.type !== 'fixed_event').length);

  it('各パターンに ability_summary が付与され、行動トリガーのエントリを含む', () => {
    expect(patterns.length).toBeGreaterThan(0);
    for (const p of patterns) {
      expect(Array.isArray(p.ability_summary)).toBe(true);
    }
    // hatsu_legend は授業/レッスン等が必ずあるので、最良パターンには行動エントリが出る
    const best = patterns.reduce((a, b) => (b.total_value > a.total_value ? b : a));
    expect(best.ability_summary.length).toBeGreaterThan(0);
  });

  it('装備は含まず、同一トリガーで連続・グループ内Vo→Da→Vi・各エントリ整合 (total ≤ per_fire×fires、per_fire = Σparts)', () => {
    const best = patterns.reduce((a, b) => (b.total_value > a.total_value ? b : a));
    const s = best.ability_summary;
    expect(s.some((e) => e.trigger === 'equip')).toBe(false);

    const statRank = (st: string) => (st === 'vo' ? 0 : st === 'da' ? 1 : st === 'vi' ? 2 : st === 'all' ? 3 : 4);
    const groupTotal = new Map<string, number>();
    for (const e of s) groupTotal.set(e.trigger, (groupTotal.get(e.trigger) ?? 0) + e.total);
    const seenTriggers = new Set<string>();
    for (let i = 0; i < s.length; i++) {
      if (i > 0 && s[i].trigger !== s[i - 1].trigger) {
        // トリガーが変わる境界: 既出トリガーへ戻らない(連続性) & グループ合計は非増加
        expect(seenTriggers.has(s[i].trigger)).toBe(false);
        expect(groupTotal.get(s[i - 1].trigger)!).toBeGreaterThanOrEqual(groupTotal.get(s[i].trigger)!);
      }
      if (i > 0 && s[i].trigger === s[i - 1].trigger) {
        // 同一トリガー内は Vo→Da→Vi→All 順
        expect(statRank(s[i].stat)).toBeGreaterThanOrEqual(statRank(s[i - 1].stat));
      }
      seenTriggers.add(s[i].trigger);
    }

    for (const e of s) {
      expect(e.fires).toBeGreaterThan(0);
      // per_fire は parts の和 (丸め誤差許容)
      const partsSum = e.parts.reduce((a, b) => a + b, 0);
      expect(Math.abs(e.per_fire - partsSum)).toBeLessThan(0.5);
      // max_count は回数を減らすだけなので total は per_fire×fires を超えない (正の寄与前提)
      if (e.per_fire > 0) {
        expect(e.total).toBeLessThanOrEqual(e.per_fire * e.fires + 0.5);
      }
    }
  });
});
