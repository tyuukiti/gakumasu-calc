/**
 * 現在の計算結果 (表示中のパターン) から共有 URL 用のペイロードを組み立てる。
 * 復元側 (calcStore / hifStore の applySharedResult) が同じ結果を再現するために必要な入力だけを載せる。
 * 所持カード一覧 (開いた人の環境) は含めない。
 */
import type { useCalcStore } from '../stores/calcStore';
import { SCHEDULE_PLAN_IDS } from '../stores/calcStore';
import type { useHifStore } from '../stores/hifStore';
import { isEmptyAllMemoryBonuses } from '../types/models';
import { SHARE_PAYLOAD_VERSION, type SharePayload } from './shareState';

type CalcSnapshot = ReturnType<typeof useCalcStore.getState>;
type HifSnapshot = ReturnType<typeof useHifStore.getState>;

/** Record<number, T> を JSON と同じ文字列キーに揃える */
function toStringKeyed<T>(rec: Record<number, T>): Record<string, T> {
  const out: Record<string, T> = {};
  for (const [k, v] of Object.entries(rec)) out[k] = v;
  return out;
}

/**
 * 共有ペイロードを作る。結果が無い／共有に対応しないプラン (ロール方式) なら null。
 * @param mode 'hif' = HIFタブ / 'calc' = 初レジェンド・NIA タブ
 */
export function buildSharePayload(
  mode: 'calc' | 'hif',
  calc: CalcSnapshot,
  hif: HifSnapshot,
): SharePayload | null {
  const source = mode === 'hif' ? hif : calc;
  if (!source.calculationResult) return null;
  const pattern = source.deckResults[source.selectedPatternIndex];
  if (!pattern || pattern.selected_cards.length === 0) return null;

  const planId = mode === 'hif' ? 'hif' : calc.selectedPlanId;
  if (mode === 'calc' && !SCHEDULE_PLAN_IDS.has(planId)) return null;

  // イベント回数は 0 のキーを省いて短くする (復元側で既知キー全体に展開する)
  const additionalCounts: Record<string, number> = {};
  for (const [k, v] of Object.entries(calc.additionalCounts)) {
    if (typeof v === 'number' && v > 0) additionalCounts[k] = v;
  }

  const payload: SharePayload = {
    v: SHARE_PAYLOAD_VERSION,
    plan: planId,
    at: Math.floor(Date.now() / 1000),
    label: pattern.label,
    deck: pattern.selected_cards.map((cs) => ({
      id: cs.card.id,
      uncap: cs.is_rental ? 4 : cs.uncap_level,
      rental: cs.is_rental,
      required: cs.is_required,
    })),
    calc: {
      planType: calc.selectedPlanType,
      characterId: calc.selectedCharacterId,
      uncap3: calc.uncap3BonusEnabled,
      step4: calc.step4BonusEnabled,
      spCounts: { vo: calc.voSpCount, da: calc.daSpCount, vi: calc.viSpCount },
      additionalCounts,
      templateName: calc.selectedTemplateName,
      contestMode: calc.contestMode,
      requiredCardIds: [...calc.requiredCardIds],
      excludedCardIds: [...calc.excludedCardIds],
      memoryBonuses: isEmptyAllMemoryBonuses(calc.memoryBonuses)
        ? []
        : calc.memoryBonuses.map((m) => ({ vo: { ...m.vo }, da: { ...m.da }, vi: { ...m.vi } })),
    },
  };

  if (mode === 'hif') {
    payload.hif = {
      scheduleChoices: toStringKeyed(hif.scheduleChoices),
      examAllocations: toStringKeyed(hif.examAllocations),
      bonusLevels: { ...hif.bonusLevels },
      bulkLessonDefault: { ...hif.bulkLessonDefault },
      bulkClassStat: hif.bulkClassStat,
    };
  } else {
    payload.sched = {
      scheduleChoices: toStringKeyed(calc.scheduleChoices[planId] ?? {}),
      niaAuditionTierByWeek: toStringKeyed(calc.niaAuditionTierByWeek),
      bulkLessonStat: calc.scheduleBulkLessonStat,
      bulkClassStat: calc.scheduleBulkClassStat,
    };
  }
  return payload;
}
