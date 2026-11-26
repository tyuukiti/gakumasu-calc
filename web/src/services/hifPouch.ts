/**
 * HIF カスタムPアイテム (ポーチ) の発動回数集計。
 *
 * ポーチは 選抜①突破で入手 (段階0=未カスタマイズ) → ②突破で1段階目 (ぬいぐるみ) → ③突破で2段階目 (デコレーション)
 * と変化し、段階ごとに「レッスン終了時 / 相談選択時 / 差し入れ終了時 / おでかけ終了時」に効果を起こす。
 * ここでは段階ごとの有効週 (stage_windows) の中で trigger に合う日程を先頭から max_count 件まで採用し、
 * 1回あたりの effects (AdditionalCounts のキー) を掛けて追加イベント回数にする。
 * 計算エンジンは変更せず、合算した AdditionalCounts を渡すだけ (デスクトップ版 HifPouchService と対)。
 */
import type {
  AdditionalCounts,
  HifPouchDefinition,
  HifPouchSelection,
  HifPouchStageEffect,
  HifPouchTrigger,
  TurnChoice,
} from '../types/models';

/** 段階ごとの集計結果 (プレビュー表・診断で共用) */
export interface HifPouchStageResult {
  /** 0=未カスタマイズ, 1=ぬいぐるみ, 2=デコレーション */
  stage: number;
  /** 表示名 (例: "未カスタマイズ", "1段階目 もじゃ", "2段階目 花") */
  label: string;
  trigger: HifPouchTrigger;
  maxCount: number;
  /** 1回あたりの追加回数 */
  effects: Record<string, number>;
  description: string;
  fromWeek: number;
  toWeek: number;
  /** 採用した発動日 (week 昇順、max_count 件まで) */
  firedWeeks: number[];
  fires: number;
}

export interface HifPouchCounts {
  /** 追加イベント回数への加算分 (0 のキーは持たない) */
  counts: Record<string, number>;
  /** index = 段階。未選択 (または定義なし) の段階は null */
  stages: (HifPouchStageResult | null)[];
}

/** 合計行・診断で使う回数キーと表示名 (この順で表示) */
export const HIF_POUCH_COUNT_LABELS: readonly { key: string; label: string }[] = [
  { key: 'p_drink_acquire', label: 'Pドリンク獲得' },
  { key: 'skill_ssr_acquire', label: 'SSR獲得' },
  { key: 'skill_change', label: 'チェンジ' },
  { key: 'skill_enhance', label: '強化' },
  { key: 'skill_delete', label: '削除' },
];

export const HIF_POUCH_TRIGGER_LABELS: Record<HifPouchTrigger, string> = {
  lesson_end: 'レッスン終了時',
  consultation: '相談選択時',
  activity_supply: '差し入れ終了時',
  outing_end: 'おでかけ終了時',
};

/**
 * ポーチプリセットの初期値 (Wiki 掲載の 3 ルート)。プリセットの保存領域が未作成のときだけ投入し、
 * 以後は他のプリセットと同じくユーザが上書き・削除できる (デスクトップ版 HifPouchService.DefaultPresets と対)。
 */
export const HIF_POUCH_DEFAULT_PRESETS: readonly { name: string; selection: HifPouchSelection }[] = [
  { name: '花ロボ 黄', selection: { color: 'yellow', plush: 'robo', deco: 'flower' } },
  { name: '花もじゃ 黄', selection: { color: 'yellow', plush: 'moja', deco: 'flower' } },
  { name: 'メダルもじゃ 黄', selection: { color: 'yellow', plush: 'moja', deco: 'medal' } },
];

/** 日程の行動がポーチの発動契機に当たるか */
export function hifPouchTriggerMatches(trigger: HifPouchTrigger, action: string): boolean {
  switch (trigger) {
    case 'lesson_end':
      return action.endsWith('_lesson');
    case 'consultation':
      return action === 'consultation';
    case 'activity_supply':
      return action === 'activity_supply';
    case 'outing_end':
      return action === 'outing';
    default:
      return false;
  }
}

/** 選択を定義と突き合わせて整合させる (定義に無い id は null、親が null なら子も null) */
export function sanitizeHifPouchSelection(
  def: HifPouchDefinition | null | undefined,
  sel: Partial<HifPouchSelection> | null | undefined,
): HifPouchSelection {
  const color = def?.colors.find((c) => c.id === sel?.color) ?? null;
  const plush = color?.plushes.find((p) => p.id === sel?.plush) ?? null;
  const deco = plush?.decorations.find((d) => d.id === sel?.deco) ?? null;
  return { color: color?.id ?? null, plush: plush?.id ?? null, deco: deco?.id ?? null };
}

/** 選択の表示名 "黄 → もじゃ → 花" (未選択なら null) */
export function describeHifPouchSelection(
  def: HifPouchDefinition | null | undefined,
  sel: HifPouchSelection | null | undefined,
): string | null {
  const color = def?.colors.find((c) => c.id === sel?.color);
  if (!color) return null;
  const parts = [color.name];
  const plush = color.plushes.find((p) => p.id === sel?.plush);
  if (plush) {
    parts.push(plush.name);
    const deco = plush.decorations.find((d) => d.id === sel?.deco);
    if (deco) parts.push(deco.name);
  }
  return parts.join(' → ');
}

/**
 * 日程 (turnChoices) からポーチの発動回数を集計する。
 * def / selection が無ければ回数ゼロ (ポーチ未使用と同じ)。
 */
export function computeHifPouchCounts(
  def: HifPouchDefinition | null | undefined,
  selection: HifPouchSelection | null | undefined,
  turnChoices: readonly TurnChoice[],
): HifPouchCounts {
  const stages: (HifPouchStageResult | null)[] = [null, null, null];
  const counts: Record<string, number> = {};
  if (!def || !selection?.color) return { counts, stages };

  const color = def.colors.find((c) => c.id === selection.color);
  if (!color) return { counts, stages };
  const plush = selection.plush ? color.plushes.find((p) => p.id === selection.plush) ?? null : null;
  const deco = plush && selection.deco ? plush.decorations.find((d) => d.id === selection.deco) ?? null : null;

  const stageEffects: ({ effect: HifPouchStageEffect; label: string } | null)[] = [
    { effect: color.base, label: '未カスタマイズ' },
    plush ? { effect: plush, label: `1段階目 ${plush.name}` } : null,
    deco ? { effect: deco, label: `2段階目 ${deco.name}` } : null,
  ];

  const sorted = [...turnChoices].sort((a, b) => a.week - b.week);
  for (let s = 0; s < stageEffects.length; s++) {
    const se = stageEffects[s];
    if (!se) continue;
    const win = def.stage_windows.find((w) => w.stage === s);
    if (!win) continue;
    const fired: number[] = [];
    for (const tc of sorted) {
      if (fired.length >= se.effect.max_count) break;
      if (tc.week < win.from_week || tc.week > win.to_week) continue;
      if (!hifPouchTriggerMatches(se.effect.trigger, tc.chosen_action)) continue;
      fired.push(tc.week);
    }
    if (fired.length > 0) {
      for (const [k, v] of Object.entries(se.effect.effects)) {
        if (v > 0) counts[k] = (counts[k] ?? 0) + v * fired.length;
      }
    }
    stages[s] = {
      stage: s,
      label: se.label,
      trigger: se.effect.trigger,
      maxCount: se.effect.max_count,
      effects: { ...se.effect.effects },
      description: se.effect.description ?? '',
      fromWeek: win.from_week,
      toWeek: win.to_week,
      firedWeeks: fired,
      fires: fired.length,
    };
  }
  return { counts, stages };
}

/** 手入力の AdditionalCounts にポーチ分を足した新しいオブジェクトを返す (元は変更しない) */
export function mergeAdditionalCounts(base: AdditionalCounts, extra: Record<string, number>): AdditionalCounts {
  const out: Record<string, number> = { ...base };
  for (const [k, v] of Object.entries(extra)) {
    if (v > 0) out[k] = (out[k] ?? 0) + v;
  }
  return out as AdditionalCounts;
}

/** 合計行の文言 "Pドリンク獲得 +6 ／ SSR獲得 +0 ／ ..." */
export function formatHifPouchCountSummary(counts: Record<string, number>): string {
  return HIF_POUCH_COUNT_LABELS.map(({ key, label }) => `${label} +${counts[key] ?? 0}`).join(' ／ ');
}

/** 1回あたりの効果 "Pドリンク獲得+2・チェンジ+1" (無ければ "効果なし") */
export function formatHifPouchEffects(effects: Record<string, number>): string {
  const parts = HIF_POUCH_COUNT_LABELS
    .filter(({ key }) => (effects[key] ?? 0) > 0)
    .map(({ key, label }) => `${label}+${effects[key]}`);
  return parts.length > 0 ? parts.join('・') : '効果なし';
}

/** 診断情報の段階行 (デスクトップ版 HifPouchService.FormatStageLine と同一書式) */
export function formatHifPouchStageLine(st: HifPouchStageResult): string {
  const fired = st.fires > 0
    ? `${st.firedWeeks.map((w) => `W${w}`).join(', ')} → ${st.fires}回`
    : '発動なし';
  return `${st.label}: ${HIF_POUCH_TRIGGER_LABELS[st.trigger]} (×${st.maxCount}) ${fired} / ${formatHifPouchEffects(st.effects)}`;
}
