import { describe, it, expect } from 'vitest';
import { calculateLessonStatTotals } from '../src/services/cardScoring';
import { loadPlan } from './helpers/loadRealData';
import type { TurnChoice } from '../src/types/models';

/**
 * 回帰テスト: 初レジェンド等のスケジュール方式で、パラメータボーナスの土台
 * (calculateLessonStatTotals) がユーザの指定した「踏み順」を尊重することを保証する。
 *
 * 旧実装は lessonAllocation の回数だけを見て「多い属性を高値週へ」割り当てていたため、
 * 例えば DaDaDaDaVi 指定 (Vi を最終週 W16=高値 に踏む) が ViDaDaDaDa 扱い
 * (Vi を W4=低値 に踏む) になり、パラボ土台・内訳・デッキ選出が実際の踏み順と食い違った。
 */

// hatsu_legend のレッスン週は W4/W7/W12/W14/W16 (値は週後半ほど大きい)。
const LESSON_WEEKS = [4, 7, 12, 14, 16];

function turnChoicesFor(order: Array<'vo' | 'da' | 'vi'>): TurnChoice[] {
  return LESSON_WEEKS.map((week, i) => ({
    week,
    chosen_action: `${order[i]}_lesson`,
  }));
}

describe('初レジェンド: パラボ土台がレッスンの踏み順を尊重する', () => {
  const plan = loadPlan('hatsu_legend');
  // 配分回数は同じ (Da4 / Vi1) でも、Vi をどの週に踏むかで Vi 土台は変わる。
  const alloc = { vo: 0, da: 4, vi: 1 };

  it('Vi を最終週(W16=高値)に踏むと Vi 土台が大きい', () => {
    // DaDaDaDaVi: W4..W14=Da, W16=Vi
    const tc = turnChoicesFor(['da', 'da', 'da', 'da', 'vi']);
    const totals = calculateLessonStatTotals(plan, alloc, tc);
    // Vi 土台 = W16 Vi(570) + Da レッスンの Vi 成分(55+60+70+90)
    expect(totals.vi).toBe(570 + 55 + 60 + 70 + 90);
  });

  it('Vi を初週(W4=低値)に踏むと Vi 土台が小さい', () => {
    // ViDaDaDaDa: W4=Vi, W7..W16=Da
    const tc = turnChoicesFor(['vi', 'da', 'da', 'da', 'da']);
    const totals = calculateLessonStatTotals(plan, alloc, tc);
    // Vi 土台 = W4 Vi(140) + Da レッスンの Vi 成分(60+70+90+115)
    expect(totals.vi).toBe(140 + 60 + 70 + 90 + 115);
  });

  it('配分回数が同じでも踏み順が違えば Vi 土台は異なる (順序を無視しない)', () => {
    const high = calculateLessonStatTotals(plan, alloc, turnChoicesFor(['da', 'da', 'da', 'da', 'vi']));
    const low = calculateLessonStatTotals(plan, alloc, turnChoicesFor(['vi', 'da', 'da', 'da', 'da']));
    expect(high.vi).toBeGreaterThan(low.vi);
  });

  it('turnChoices 未指定時は従来の配分回数ベース近似 (高値週優先) にフォールバックする', () => {
    // 自動ピックモード: Da4/Vi1 なら Da が高値週、Vi は最低値週(W4) に割り当てられる近似。
    const totals = calculateLessonStatTotals(plan, alloc);
    expect(totals.vi).toBe(140 + 60 + 70 + 90 + 115);
  });
});
