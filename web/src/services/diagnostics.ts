import type { useCalcStore } from '../stores/calcStore';
import type { useAppStore } from '../stores/appStore';
import type { useHifStore } from '../stores/hifStore';
import type { MemoryBonus } from '../types/models';
import { ACTION_TYPE_DISPLAY } from '../types/enums';

type CalcSnapshot = ReturnType<typeof useCalcStore.getState>;
type AppSnapshot = ReturnType<typeof useAppStore.getState>;
type HifSnapshot = ReturnType<typeof useHifStore.getState>;

/** イベントカウントのキー→日本語ラベル (EventCountConfig.tsx と同期) */
const COUNT_LABELS: Record<string, string> = {
  p_drink_acquire: 'Pドリンク獲得',
  p_item_acquire: 'Pアイテム獲得',
  skill_acquire: 'スキルカード獲得',
  skill_ssr_acquire: 'スキル(SSR)獲得',
  skill_enhance: 'スキル強化',
  skill_delete: 'スキル削除',
  skill_custom: 'スキルカスタム',
  skill_change: 'スキルチェンジ',
  active_enhance: 'アクティブ強化',
  active_delete: 'アクティブ削除',
  mental_acquire: 'メンタル獲得',
  mental_enhance: 'メンタル強化',
  mental_delete: 'メンタル削除',
  active_acquire: 'アクティブ獲得',
  good_condition_acquire: '好調カード獲得',
  concentrate_acquire: '集中カード獲得',
  genki_acquire: '元気カード獲得',
  good_impression_acquire: '好印象カード獲得',
  motivation_acquire: 'やる気カード獲得',
  conserve_acquire: '温存カード獲得',
  fullpower_acquire: '全力カード獲得',
  aggressive_acquire: '強気カード獲得',
  consultation_drink: '相談Pドリンク交換',
};

const PLAN_TYPE_LABELS: Record<string, string> = {
  sense: 'センス',
  logic: 'ロジック',
  anomaly: 'アノマリー',
};

function formatMemoryLine(m: MemoryBonus): string | null {
  const parts: string[] = [];
  for (const [stat, label] of [['vo', 'Vo'], ['da', 'Da'], ['vi', 'Vi']] as const) {
    const ab = m[stat];
    if (ab.value !== 0) {
      parts.push(`${label} ${ab.value > 0 ? '+' : ''}${ab.value}${ab.type === 'para' ? '%(パラボ)' : ''}`);
    }
  }
  return parts.length > 0 ? parts.join(' / ') : null;
}

/** イベントカウント (0以外のみ) を lines に追記。calc / hif で共有。 */
function appendEventCounts(lines: string[], additionalCounts: Record<string, number>): void {
  const active = Object.entries(additionalCounts).filter(([, v]) => v > 0);
  if (active.length === 0) return;
  lines.push('');
  lines.push('[イベントカウント]');
  for (const [key, value] of active) {
    lines.push(`${COUNT_LABELS[key] ?? key}: ${value}`);
  }
}

/** 持ち込みメモリー (空でないもの) を lines に追記。 */
function appendMemory(lines: string[], memoryBonuses: MemoryBonus[]): void {
  const memoryLines = memoryBonuses
    .map((m, i) => {
      const body = formatMemoryLine(m);
      return body ? `メモリー${i + 1}: ${body}` : null;
    })
    .filter((s): s is string => s !== null);
  if (memoryLines.length === 0) return;
  lines.push('');
  lines.push('[持ち込みメモリー]');
  lines.push(...memoryLines);
}

/** 選択編成 (パターン名 + カード一覧) を lines に追記。 */
function appendSelectedPattern(
  lines: string[],
  deckResults: { label: string; selected_cards: { card: { id: string; name: string }; is_rental: boolean; is_required: boolean }[] }[],
  selectedPatternIndex: number,
): void {
  const pattern = deckResults[selectedPatternIndex];
  if (!pattern) return;
  lines.push('');
  lines.push(`[選択編成] パターン: ${pattern.label}`);
  pattern.selected_cards.forEach((cs, i) => {
    const tags: string[] = [];
    if (cs.is_rental) tags.push('レンタル');
    if (cs.is_required) tags.push('必須');
    const tagStr = tags.length > 0 ? ` [${tags.join(', ')}]` : '';
    lines.push(`${i + 1}. ${cs.card.name} (${cs.card.id})${tagStr}`);
  });
}

/** 計算結果 (cap適用後 / 超過時はcap前も) を lines に追記。 */
function appendResult(
  lines: string[],
  result: { final_status: { vo: number; da: number; vi: number } } | null,
  statCap: number,
): void {
  if (!result) {
    lines.push('');
    lines.push('[計算結果] 未計算');
    return;
  }
  const cap = (v: number) => Math.min(v, statCap);
  const fs = result.final_status;
  const total = cap(fs.vo) + cap(fs.da) + cap(fs.vi);
  lines.push('');
  lines.push(`[計算結果] (上限 ${statCap})`);
  for (const [stat, label] of [['vo', 'Vo'], ['da', 'Da'], ['vi', 'Vi']] as const) {
    const raw = fs[stat];
    const capped = cap(raw);
    const overflow = raw - capped;
    lines.push(`${label}: ${capped}${overflow > 0 ? ` (cap前 ${raw})` : ''}`);
  }
  lines.push(`合計: ${total}`);
}

/**
 * 現在の計算設定・選択編成・計算結果を、問題報告用の平文レポートにまとめる。
 * 計算未実行でも設定だけはダンプできる。
 */
export function buildDiagnosticReport(calc: CalcSnapshot, app: AppSnapshot): string {
  const lines: string[] = [];
  lines.push('=== 学マス計算ツール 診断情報 ===');

  // --- 設定 ---
  lines.push('');
  lines.push('[設定]');
  const plan = app.plans.find((p) => p.id === calc.selectedPlanId);
  lines.push(`プラン: ${plan ? plan.name : '(未選択)'}${plan ? ` (${plan.id})` : ''}`);
  lines.push(`プランタイプ: ${PLAN_TYPE_LABELS[calc.selectedPlanType] ?? calc.selectedPlanType}`);
  lines.push(`ロール: Vo=${calc.voRole} / Da=${calc.daRole} / Vi=${calc.viRole}`);
  lines.push(`SP回数: Vo=${calc.voSpCount} / Da=${calc.daSpCount} / Vi=${calc.viSpCount}`);
  lines.push(`所持カードのみ: ${calc.ownedOnly ? 'ON' : 'OFF'}`);
  lines.push(`コンテストモード: ${calc.contestMode ? 'ON' : 'OFF'}`);

  const character = calc.selectedCharacterId
    ? app.characters.find((c) => c.id === calc.selectedCharacterId)
    : null;
  lines.push(
    `キャラ: ${character ? character.name : '(なし)'}` +
      (character ? ` / 3凸ボーナス: ${calc.uncap3BonusEnabled ? 'ON' : 'OFF'}` : '') +
      (character?.step4_bonus ? ` / STEP4ボーナス: ${calc.step4BonusEnabled ? 'ON' : 'OFF'}` : ''),
  );

  if (calc.selectedTemplateName) {
    lines.push(`テンプレート: ${calc.selectedTemplateName}`);
  }

  if (calc.requiredCardIds.length > 0) {
    const names = calc.requiredCardIds.map((id) => {
      const c = app.cards.find((card) => card.id === id);
      return c ? `${c.name} (${id})` : id;
    });
    lines.push(`必須カード: ${names.join(', ')}`);
  }

  appendEventCounts(lines, calc.additionalCounts);
  appendMemory(lines, calc.memoryBonuses);
  appendSelectedPattern(lines, calc.deckResults, calc.selectedPatternIndex);
  appendResult(lines, calc.calculationResult, plan?.status_limit ?? 2800);

  return lines.join('\n');
}

/**
 * HIFモードの設定・スケジュール・選択編成・計算結果を問題報告用の平文にまとめる。
 * 共有設定 (キャラ/メモリー/イベントカウント/SP回数/所持・コンテスト等) は calcStore 由来。
 * HIF固有 (ボーナスLv/overflow罰則/スケジュール/結果) は hifStore 由来。
 */
export function buildHifDiagnosticReport(
  hif: HifSnapshot,
  calc: CalcSnapshot,
  app: AppSnapshot,
): string {
  const lines: string[] = [];
  lines.push('=== 学マス計算ツール 診断情報 (HIF) ===');

  // --- 設定 ---
  lines.push('');
  lines.push('[設定]');
  lines.push('モード: HIF (Hatsuboshi IDOL FESTIVAL)');
  lines.push(`プランタイプ: ${PLAN_TYPE_LABELS[calc.selectedPlanType] ?? calc.selectedPlanType}`);
  lines.push(`SP回数: Vo=${calc.voSpCount} / Da=${calc.daSpCount} / Vi=${calc.viSpCount}`);
  lines.push(`所持カードのみ: ${calc.ownedOnly ? 'ON' : 'OFF'}`);
  lines.push(`コンテストモード: ${calc.contestMode ? 'ON' : 'OFF'}`);

  const character = calc.selectedCharacterId
    ? app.characters.find((c) => c.id === calc.selectedCharacterId)
    : null;
  lines.push(
    `キャラ: ${character ? character.name : '(なし)'}` +
      (character ? ` / 3凸ボーナス: ${calc.uncap3BonusEnabled ? 'ON' : 'OFF'}` : '') +
      (character?.step4_bonus ? ` / STEP4ボーナス: ${calc.step4BonusEnabled ? 'ON' : 'OFF'}` : ''),
  );

  if (calc.selectedTemplateName) {
    lines.push(`テンプレート: ${calc.selectedTemplateName}`);
  }

  if (calc.requiredCardIds.length > 0) {
    const names = calc.requiredCardIds.map((id) => {
      const c = app.cards.find((card) => card.id === id);
      return c ? `${c.name} (${id})` : id;
    });
    lines.push(`必須カード: ${names.join(', ')}`);
  }

  // --- HIFボーナス ---
  const bl = hif.bonusLevels;
  lines.push('');
  lines.push('[HIFボーナス] (Lv)');
  lines.push(`Vo上昇=${bl.voUpLevel} / Da上昇=${bl.daUpLevel} / Vi上昇=${bl.viUpLevel}`);
  lines.push(`本戦パラメータ上限増加=${bl.finalStatLimitLevel}`);
  if (hif.overflowPenalty.enabled) {
    lines.push(`MAX超過再抽選: ON (閾値 ${hif.overflowPenalty.threshold})`);
  }

  // --- HIFスケジュール ---
  const hifPlan = app.plans.find((p) => p.id === 'hif');
  if (hifPlan) {
    const schedLines: string[] = [];
    for (const w of hifPlan.schedule) {
      const choice = hif.scheduleChoices[w.week];
      // 選択肢なしの日 (本戦インターバル等) の stale choice は計算対象外なのでダンプしない
      if (choice && w.available_actions.length > 0) {
        const actLabel = ACTION_TYPE_DISPLAY[choice.action] ?? choice.action;
        const subStr =
          'sub_stat' in choice ? `（サブ${choice.sub_stat.toUpperCase()}）` : '';
        schedLines.push(`W${w.week}: ${actLabel}${subStr}`);
      }
      const alloc = hif.examAllocations[w.week];
      if (alloc && (alloc.vo || alloc.da || alloc.vi)) {
        schedLines.push(`W${w.week} 試験配分: Vo${alloc.vo}/Da${alloc.da}/Vi${alloc.vi}`);
      }
    }
    if (schedLines.length > 0) {
      lines.push('');
      lines.push('[HIFスケジュール]');
      lines.push(...schedLines);
    }
  }

  appendEventCounts(lines, calc.additionalCounts);
  appendMemory(lines, calc.memoryBonuses);
  appendSelectedPattern(lines, hif.deckResults, hif.selectedPatternIndex);
  // HIFの上限は本戦上限増加を反映した _lastPlan.status_limit を使う
  appendResult(lines, hif.calculationResult, hif._lastPlan?.status_limit ?? hifPlan?.status_limit ?? 1800);

  return lines.join('\n');
}
