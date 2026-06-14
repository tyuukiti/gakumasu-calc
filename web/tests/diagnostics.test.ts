import { describe, it, expect } from 'vitest';
import { buildDiagnosticReport, buildHifDiagnosticReport } from '../src/services/diagnostics';
import { loadPlan, loadPlans, loadAllCards, loadCharacters } from './helpers/loadRealData';
import { emptyAdditionalCounts } from '../src/types/models';
import type { ActionType } from '../src/types/enums';

type CalcSnap = Parameters<typeof buildDiagnosticReport>[0];
type AppSnap = Parameters<typeof buildDiagnosticReport>[1];
type HifSnap = Parameters<typeof buildHifDiagnosticReport>[0];

// 清夏 (紫雲清夏) は nia_trend / nia_criteria を持つキャラ
const SUMIKA = 'char_sumika';

function appSnap(): AppSnap {
  return {
    plans: loadPlans(),
    characters: loadCharacters(),
    cards: loadAllCards(),
  } as unknown as AppSnap;
}

function calcSnap(over: Partial<Record<string, unknown>>): CalcSnap {
  return {
    selectedPlanId: 'nia',
    selectedPlanType: 'logic',
    voRole: 'サブ',
    daRole: 'サブ',
    viRole: 'サブ',
    voSpCount: 0,
    daSpCount: 0,
    viSpCount: 0,
    ownedOnly: false,
    contestMode: false,
    selectedCharacterId: null,
    uncap3BonusEnabled: false,
    step4BonusEnabled: true,
    selectedTemplateName: null,
    requiredCardIds: [],
    additionalCounts: emptyAdditionalCounts(),
    memoryBonuses: [],
    deckResults: [],
    selectedPatternIndex: 0,
    calculationResult: null,
    scheduleChoices: {},
    niaAuditionTierByWeek: {},
    ...over,
  } as unknown as CalcSnap;
}

/** プランの行動可能週すべてに先頭の available_action を割り当てた scheduleChoices を作る。 */
function fullScheduleChoices(planId: string): Record<number, { action: ActionType }> {
  const out: Record<number, { action: ActionType }> = {};
  for (const w of loadPlan(planId).schedule) {
    if (w.available_actions.length > 0) {
      out[w.week] = { action: w.available_actions[0] as ActionType };
    }
  }
  return out;
}

describe('診断レポート: 日程方式プラン (NIA / 初レジェンド)', () => {
  it('NIA: 日程・NIAオーディション・実効キャラ補正を含み、ロールは出さない', () => {
    const calc = calcSnap({
      selectedPlanId: 'nia',
      selectedCharacterId: SUMIKA,
      scheduleChoices: { nia: fullScheduleChoices('nia') },
    });
    const report = buildDiagnosticReport(calc, appSnap());

    expect(report).toContain('[日程]');
    expect(report).toContain('[NIAオーディション]');
    expect(report).toContain('審査基準:');
    expect(report).toContain('流行1=');
    expect(report).toContain('キャラ補正(実効):');
    // 日程方式はロールを持たない
    expect(report).not.toContain('ロール:');
    // キャラ設定済みなら獲得0表記にならず、種別→獲得値の行が出る
    expect(report).toContain('→');
    expect(report).not.toContain('獲得0 (キャラ/流行未設定)');
  });

  it('NIA: キャラ未選択なら流行未設定・獲得0として明示される', () => {
    const calc = calcSnap({
      selectedPlanId: 'nia',
      selectedCharacterId: null,
      scheduleChoices: { nia: fullScheduleChoices('nia') },
    });
    const report = buildDiagnosticReport(calc, appSnap());

    expect(report).toContain('[NIAオーディション]');
    expect(report).toContain('(未設定 → 獲得0)');
    // キャラ未選択なら実効補正行は出ない
    expect(report).not.toContain('キャラ補正(実効):');
  });

  it('初レジェンド: 日程・実効キャラ補正を含み、NIAオーディション節は出さない', () => {
    const calc = calcSnap({
      selectedPlanId: 'hatsu_legend',
      selectedCharacterId: SUMIKA,
      scheduleChoices: { hatsu_legend: fullScheduleChoices('hatsu_legend') },
    });
    const report = buildDiagnosticReport(calc, appSnap());

    expect(report).toContain('[日程]');
    expect(report).toContain('キャラ補正(実効):');
    expect(report).not.toContain('ロール:');
    expect(report).not.toContain('[NIAオーディション]');
  });
});

describe('診断レポート: HIF', () => {
  function hifSnap(): HifSnap {
    return {
      bonusLevels: { voUpLevel: 0, daUpLevel: 0, viUpLevel: 0, finalStatLimitLevel: 0 },
      overflowPenalty: { enabled: false, threshold: 100 },
      scheduleChoices: {},
      examAllocations: {},
      deckResults: [],
      selectedPatternIndex: 0,
      calculationResult: null,
      _lastPlan: null,
    } as unknown as HifSnap;
  }

  it('HIF: 実効キャラ補正を含む', () => {
    const calc = calcSnap({ selectedCharacterId: SUMIKA });
    const report = buildHifDiagnosticReport(hifSnap(), calc, appSnap());

    expect(report).toContain('診断情報 (HIF)');
    expect(report).toContain('キャラ補正(実効):');
  });
});
