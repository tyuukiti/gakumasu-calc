import { describe, it, expect } from 'vitest';
import { loadAllCards, loadPlans, loadPlan } from './helpers/loadRealData';
import { selectMultiplePatterns } from '../src/services/cardScoring';
import { scoreDeck } from './helpers/scoreDeck';

describe('toolchain smoke', () => {
  it('実データ (カード/プラン) を読み込める', () => {
    const cards = loadAllCards();
    const plans = loadPlans();
    expect(cards.length).toBeGreaterThan(50);
    expect(plans.map((p) => p.id)).toContain('hatsu_legend');
  });

  it('selectMultiplePatterns が編成を返し scoreDeck で採点できる', () => {
    const plan = loadPlan('hatsu_legend');
    const cards = loadAllCards();
    const mainStats = ['vo', 'da'];
    const lessonWeeks = plan.schedule.filter(
      (w) => w.lessons.length > 0 && w.type !== 'fixed_event',
    ).length;

    const patterns = selectMultiplePatterns(
      plan,
      cards,
      mainStats,
      'vi',
      lessonWeeks,
    );
    expect(patterns.length).toBeGreaterThan(0);

    const best = patterns.reduce((a, b) => (b.total_value > a.total_value ? b : a));
    const score = scoreDeck(plan, best.selected_cards.map((c) => c.card), mainStats);
    expect(score.cappedTotal).toBeGreaterThan(0);
  });
});
