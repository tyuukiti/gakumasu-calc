import { buildTurnChoices } from '../../src/services/cardScoring';
import type { TrainingPlan, TurnChoice } from '../../src/types/models';

/**
 * HIFモードのテスト入力ヘルパ。
 * 実運用では hifStore が schedule 選択から turnChoices を組むが、テストでは
 * export 済みの buildTurnChoices を使い、選択(turnChoicesOverride)と採点(scoreDeck)で
 * 同一の turnChoices を共有することで整合させる。
 */

/** HIFプランの turnChoices を構築 (選択・採点で共有する)。 */
export function hifTurnChoices(plan: TrainingPlan, mainStats: string[]): TurnChoice[] {
  return buildTurnChoices(plan, mainStats);
}

/** turnChoices からレッスン配分 (vo/da/vi の lesson 回数) を集計。 */
export function lessonAllocationFrom(turnChoices: TurnChoice[]): Record<string, number> {
  const alloc: Record<string, number> = { vo: 0, da: 0, vi: 0 };
  for (const tc of turnChoices) {
    if (tc.chosen_action === 'vo_lesson') alloc.vo++;
    else if (tc.chosen_action === 'da_lesson') alloc.da++;
    else if (tc.chosen_action === 'vi_lesson') alloc.vi++;
  }
  return alloc;
}
