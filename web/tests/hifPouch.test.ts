import { describe, it, expect, beforeEach } from 'vitest';
import { useAppStore } from '../src/stores/appStore';
import { useCalcStore } from '../src/stores/calcStore';
import {
  useHifStore,
  buildHifTurnChoices,
  defaultHifPouchPresets,
  MAX_HIF_POUCH_PRESETS,
  type HifChoice,
  type HifConditionPreset,
} from '../src/stores/hifStore';
import {
  computeHifPouchCounts,
  mergeAdditionalCounts,
  sanitizeHifPouchSelection,
  describeHifPouchSelection,
  formatHifPouchStageLine,
  formatHifPouchCountSummary,
  HIF_POUCH_DEFAULT_PRESETS,
} from '../src/services/hifPouch';
import { buildHifDiagnosticReport } from '../src/services/diagnostics';
import { sanitizeSharePayload } from '../src/services/shareState';
import { buildSharePayload } from '../src/services/sharePayload';
import { defaultHifBonusLevels } from '../src/types/hifBonus';
import { emptyAdditionalCounts, emptyMemoryBonus, emptyHifPouchSelection } from '../src/types/models';
import type { HifPouchSelection } from '../src/types/models';
import {
  loadAllCards,
  loadPlans,
  loadPlan,
  loadCharacters,
  loadTemplates,
  loadHifPouch,
  templateAdditionalCounts,
} from './helpers/loadRealData';

/**
 * HIF カスタムPアイテム (ポーチ) の発動回数集計 (C# 版 HifPouchTests と対)。
 *
 * - 定義ファイル Data/Hif/pouch.yaml の整合 (3色×4体、トリガー/効果キーが既知)
 * - computeHifPouchCounts: 既定日程で 花ロボ黄 = 2+4+4 = 10 本、花もじゃ黄 = 2+2+2 = 6 本、
 *   1段階目の窓に相談が無い日程では もじゃ段階が 0 本、部分選択、定義なし
 * - ストア経由: ポーチあり ＝ 手入力に同回数を足した結果と一致 (補正なしベースラインも)
 * - 共有ペイロード往復・条件プリセット往復 (pouch 欠落は未選択)・診断ブロック
 */

const def = loadHifPouch();
const plan = loadPlan('hif');
const KNOWN_KEYS = new Set(Object.keys(emptyAdditionalCounts()));
const TRIGGERS = new Set(['lesson_end', 'consultation', 'activity_supply', 'outing_end']);

const HANA_ROBO: HifPouchSelection = { color: 'yellow', plush: 'robo', deco: 'flower' };
const HANA_MOJA: HifPouchSelection = { color: 'yellow', plush: 'moja', deco: 'flower' };

/**
 * 既定日程に相当する scheduleChoices (全週): 公開レッスンは Da主/Viサブ、授業は Vi、
 * その他は 差し入れ > おでかけ > 相談 > 特別指導 の優先 (hifStore.defaultChoiceForWeek と同じ)。
 * → Day1/8/14/16/23 差し入れ、Day5 おでかけ、Day12/19/26 相談。
 */
function buildChoices(overrides: Record<number, HifChoice> = {}): Record<number, HifChoice> {
  const out: Record<number, HifChoice> = {};
  for (const w of plan.schedule) {
    if (w.available_actions.length === 0) continue;
    if (w.type === 'public_lesson') {
      out[w.week] = { action: 'da_lesson', sub_stat: 'vi' };
    } else if (w.available_actions.every((a) => a.endsWith('_class'))) {
      out[w.week] = { action: 'vi_class' };
    } else {
      const pick = ['activity_supply', 'outing', 'consultation', 'special_training']
        .find((a) => w.available_actions.includes(a)) as 'outing' | 'consultation' | 'activity_supply' | 'special_training';
      out[w.week] = { action: pick };
    }
  }
  return { ...out, ...overrides };
}

const defaultTurnChoices = buildHifTurnChoices(plan, buildChoices());

describe('ポーチ定義 (Data/Hif/pouch.yaml)', () => {
  it('3色×4体で、トリガー・効果キー・回数上限が既知の範囲にある', () => {
    expect(def.colors.map((c) => c.id)).toEqual(['red', 'green', 'yellow']);
    expect(def.stage_windows.map((w) => [w.stage, w.from_week, w.to_week])).toEqual([
      [0, 8, 12], [1, 14, 19], [2, 21, 26],
    ]);
    const check = (e: { trigger: string; max_count: number; effects: Record<string, number> }, label: string) => {
      expect(TRIGGERS.has(e.trigger), `${label} trigger=${e.trigger}`).toBe(true);
      expect(e.max_count, `${label} max_count`).toBeGreaterThanOrEqual(1);
      for (const [k, v] of Object.entries(e.effects)) {
        expect(KNOWN_KEYS.has(k), `${label} effect key=${k}`).toBe(true);
        expect(v, `${label} ${k}`).toBeGreaterThan(0);
      }
    };
    for (const c of def.colors) {
      check(c.base, c.id);
      expect(c.plushes.length, c.id).toBe(4);
      for (const p of c.plushes) {
        check(p, `${c.id}/${p.id}`);
        // レッスン終了時系 (赤くま・緑インコ・黄ロボ) のデコは 花/リボン/羽 の 3 択、それ以外は 4 択
        expect(p.decorations.length, `${c.id}/${p.id}`).toBe(p.trigger === 'lesson_end' ? 3 : 4);
        for (const d of p.decorations) {
          check(d, `${c.id}/${p.id}/${d.id}`);
          expect(d.trigger, `${c.id}/${p.id}/${d.id} はぬいぐるみと同じ契機`).toBe(p.trigger);
        }
      }
    }
  });

  it('初期プリセット (Wiki の 3 ルート) の id は定義に存在する', () => {
    expect(HIF_POUCH_DEFAULT_PRESETS.map((p) => p.name)).toEqual(['花ロボ 黄', '花もじゃ 黄', 'メダルもじゃ 黄']);
    for (const p of HIF_POUCH_DEFAULT_PRESETS) {
      expect(sanitizeHifPouchSelection(def, p.selection)).toEqual(p.selection);
    }
  });
});

describe('computeHifPouchCounts', () => {
  it('既定日程: 花ロボ黄 = 2+4+4 = 10 本、発動日はレッスン日', () => {
    const r = computeHifPouchCounts(def, HANA_ROBO, defaultTurnChoices);
    expect(r.counts).toEqual({ p_drink_acquire: 10 });
    expect(r.stages.map((s) => s?.firedWeeks)).toEqual([[9, 11], [15, 18], [22, 25]]);
    expect(r.stages.map((s) => s?.fires)).toEqual([2, 2, 2]);
  });

  it('既定日程: 花もじゃ黄 = 2+2+2 = 6 本 (相談は Day19 と 本戦6日目)', () => {
    const r = computeHifPouchCounts(def, HANA_MOJA, defaultTurnChoices);
    expect(r.counts).toEqual({ p_drink_acquire: 6 });
    expect(r.stages.map((s) => s?.firedWeeks)).toEqual([[9, 11], [19], [26]]);
    expect(r.stages[1]?.label).toBe('1段階目 もじゃ');
    expect(r.stages[2]?.label).toBe('2段階目 花');
  });

  it('1段階目の窓 (Day14〜19) に相談が無い日程では もじゃ段階は 0 本', () => {
    const tc = buildHifTurnChoices(plan, buildChoices({ 19: { action: 'special_training' } }));
    const r = computeHifPouchCounts(def, HANA_MOJA, tc);
    expect(r.counts).toEqual({ p_drink_acquire: 4 });
    expect(r.stages[1]?.fires).toBe(0);
    expect(r.stages[1]?.firedWeeks).toEqual([]);
  });

  it('おでかけ系 (黄うさぎ羽): おでかけを踏んだ段階だけ発動し、強化・削除も加算される', () => {
    const sel: HifPouchSelection = { color: 'yellow', plush: 'usagi', deco: 'feather' };
    // 既定日程は Day14/16/23 が差し入れなので段階1・2は不発
    const r0 = computeHifPouchCounts(def, sel, defaultTurnChoices);
    expect(r0.counts).toEqual({ p_drink_acquire: 2 });
    expect([r0.stages[1]?.fires, r0.stages[2]?.fires]).toEqual([0, 0]);
    // Day16 と 本戦3日目をおでかけにすると 1段階目 (2本+強化1)、2段階目 (2本+削除1) が発動
    const tc = buildHifTurnChoices(plan, buildChoices({ 16: { action: 'outing' }, 23: { action: 'outing' } }));
    const r = computeHifPouchCounts(def, sel, tc);
    expect(r.counts).toEqual({ p_drink_acquire: 6, skill_enhance: 1, skill_delete: 1 });
    expect(r.stages.map((s) => s?.firedWeeks)).toEqual([[9, 11], [16], [23]]);
  });

  it('発動回数は段階ごとの max_count で頭打ち (窓内に該当日が多くても先頭から採用)', () => {
    // 赤くま: レッスン終了時 ×2。窓 8〜12 にはレッスンが 2 日 (9, 11) しか無いので一致を確認
    const r = computeHifPouchCounts(def, { color: 'red', plush: 'kuma', deco: null }, defaultTurnChoices);
    expect(r.stages[1]?.maxCount).toBe(2);
    expect(r.stages[1]?.firedWeeks).toEqual([15, 18]);
    expect(r.counts).toEqual({ p_drink_acquire: 2 }); // 赤の段階0は効果なし、くまで 1本×2
  });

  it('部分選択: 色だけなら段階0のみ、色+ぬいぐるみなら段階0・1', () => {
    const colorOnly = computeHifPouchCounts(def, { color: 'yellow', plush: null, deco: null }, defaultTurnChoices);
    expect(colorOnly.counts).toEqual({ p_drink_acquire: 2 });
    expect(colorOnly.stages[1]).toBeNull();
    expect(colorOnly.stages[2]).toBeNull();
    const noDeco = computeHifPouchCounts(def, { color: 'yellow', plush: 'moja', deco: null }, defaultTurnChoices);
    expect(noDeco.counts).toEqual({ p_drink_acquire: 4 });
    expect(noDeco.stages[2]).toBeNull();
  });

  it('定義なし・未選択・未知 id は回数ゼロ', () => {
    expect(computeHifPouchCounts(null, HANA_MOJA, defaultTurnChoices).counts).toEqual({});
    expect(computeHifPouchCounts(def, emptyHifPouchSelection(), defaultTurnChoices).counts).toEqual({});
    expect(computeHifPouchCounts(def, { color: 'blue', plush: null, deco: null }, defaultTurnChoices).counts).toEqual({});
    expect(computeHifPouchCounts(def, HANA_MOJA, []).counts).toEqual({});
  });

  it('sanitizeHifPouchSelection: 定義に無い id は未選択、親が未選択なら子も未選択', () => {
    expect(sanitizeHifPouchSelection(def, { color: 'yellow', plush: 'kuma', deco: 'flower' }))
      .toEqual({ color: 'yellow', plush: null, deco: null });
    expect(sanitizeHifPouchSelection(def, { color: 'nope', plush: 'moja', deco: 'flower' }))
      .toEqual({ color: null, plush: null, deco: null });
    expect(sanitizeHifPouchSelection(def, undefined)).toEqual(emptyHifPouchSelection());
    expect(sanitizeHifPouchSelection(null, HANA_MOJA)).toEqual(emptyHifPouchSelection());
    expect(describeHifPouchSelection(def, HANA_MOJA)).toBe('黄 → もじゃ → 花');
    expect(describeHifPouchSelection(def, { color: 'green', plush: null, deco: null })).toBe('緑');
  });

  it('mergeAdditionalCounts は元を変更せず加算する', () => {
    const base = templateAdditionalCounts('hif', 'アノマリー(強気)');
    const before = { ...base };
    const merged = mergeAdditionalCounts(base, { p_drink_acquire: 6, skill_delete: 1 });
    expect(base).toEqual(before);
    expect(merged.p_drink_acquire).toBe(before.p_drink_acquire + 6);
    expect(merged.skill_delete).toBe(before.skill_delete + 1);
    expect(merged.skill_change).toBe(before.skill_change);
  });

  it('診断行の書式 (デスクトップ版と同一)', () => {
    const r = computeHifPouchCounts(def, HANA_MOJA, defaultTurnChoices);
    expect(formatHifPouchStageLine(r.stages[0]!)).toBe('未カスタマイズ: レッスン終了時 (×2) W9, W11 → 2回 / Pドリンク獲得+1');
    expect(formatHifPouchStageLine(r.stages[1]!)).toBe('1段階目 もじゃ: 相談選択時 (×1) W19 → 1回 / Pドリンク獲得+2');
    expect(formatHifPouchCountSummary(r.counts)).toBe('Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0');
    const none = computeHifPouchCounts(def, HANA_MOJA, buildHifTurnChoices(plan, buildChoices({ 19: { action: 'special_training' } })));
    expect(formatHifPouchStageLine(none.stages[1]!)).toBe('1段階目 もじゃ: 相談選択時 (×1) 発動なし / Pドリンク獲得+2');
  });
});

// ---------------------------------------------------------------------------
// ストア経由
// ---------------------------------------------------------------------------

function resetStores() {
  useAppStore.setState({
    cards: loadAllCards(),
    plans: loadPlans(),
    templates: loadTemplates(),
    characters: loadCharacters(),
    hifPouch: def,
    inventory: [],
    isLoading: false,
    error: null,
  });
  useHifStore.setState({
    scheduleChoices: buildChoices(),
    examAllocations: {},
    examRatio: { vo: 34, da: 33, vi: 33 },
    bulkLessonDefault: { mainStat: 'da', subStat: 'vi' },
    bulkClassStat: 'vi',
    pouch: emptyHifPouchSelection(),
    pouchPresets: defaultHifPouchPresets(),
    schedulePresets: [],
    conditionPresets: [],
    bonusLevels: defaultHifBonusLevels(),
    deckResults: [],
    selectedPatternIndex: 0,
    calculationResult: null,
    calculationResultWithoutCharacter: null,
    errorMessage: null,
    _lastMainStats: [],
    _lastPlan: null,
    _lastTurnChoices: [],
    shareView: null,
  });
  useCalcStore.setState({
    selectedPlanId: '',
    selectedPlanType: 'anomaly',
    voSpCount: 0,
    daSpCount: 3,
    viSpCount: 0,
    additionalCounts: templateAdditionalCounts('hif', 'アノマリー(強気)'),
    selectedTemplateName: 'アノマリー(強気)',
    ownedOnly: false,
    contestMode: false,
    requiredCardIds: [],
    excludedCardIds: [],
    selectedCharacterId: 'char_saki',
    uncap3BonusEnabled: true,
    step4BonusEnabled: true,
    uncap3BonusByChar: {},
    step4BonusByChar: {},
    memoryBonuses: [emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()],
    deckResults: [],
    selectedPatternIndex: 0,
    calculationResult: null,
    calculationResultWithoutCharacter: null,
    errorMessage: null,
    _lastMainStats: [],
    _lastTurnChoices: [],
    scheduleChoices: {},
    niaAuditionTierByWeek: {},
    shareView: null,
  });
}

describe('ポーチ (ストア経由)', () => {
  beforeEach(resetStores);

  it('setPouchSelection: 親を変えると子はリセット、定義に無い id は未選択', () => {
    const s = () => useHifStore.getState();
    s().setPouchSelection(HANA_MOJA);
    expect(s().pouch).toEqual(HANA_MOJA);
    s().setPouchSelection({ plush: 'robo' });
    expect(s().pouch).toEqual({ color: 'yellow', plush: 'robo', deco: null });
    s().setPouchSelection({ deco: 'ribbon' });
    expect(s().pouch).toEqual({ color: 'yellow', plush: 'robo', deco: 'ribbon' });
    s().setPouchSelection({ color: 'green' });
    expect(s().pouch).toEqual({ color: 'green', plush: null, deco: null });
    s().setPouchSelection({ plush: 'moja' }); // 緑に もじゃ は無い
    expect(s().pouch).toEqual({ color: 'green', plush: null, deco: null });
    s().setPouchSelection({ color: null });
    expect(s().pouch).toEqual(emptyHifPouchSelection());
    // プリセット読込相当: 3段を一括 (同じぬいぐるみでも色が変わる経路で落ちない)
    s().setPouchSelection({ color: 'yellow', plush: 'moja', deco: 'medal' });
    expect(s().pouch).toEqual({ color: 'yellow', plush: 'moja', deco: 'medal' });
  });

  it('ポーチプリセット: 初期値は Wiki の 3 ルート、保存(同名上書き)・読込・削除・上限', () => {
    const s = () => useHifStore.getState();
    expect(s().pouchPresets.map((p) => p.name)).toEqual(['花ロボ 黄', '花もじゃ 黄', 'メダルもじゃ 黄']);

    // 読込で選択に反映 (定義に無い id は整合される)
    s().loadPouchPreset('花もじゃ 黄');
    expect(s().pouch).toEqual(HANA_MOJA);
    s().loadPouchPreset('存在しない');
    expect(s().pouch).toEqual(HANA_MOJA);

    // 保存 (新規) と同名上書き
    s().setPouchSelection({ color: 'green', plush: 'inko', deco: 'feather' });
    s().savePouchPreset('  緑インコ羽  ');
    expect(s().pouchPresets.at(-1)).toEqual({ name: '緑インコ羽', selection: { color: 'green', plush: 'inko', deco: 'feather' } });
    s().setPouchSelection({ deco: 'ribbon' });
    s().savePouchPreset('緑インコ羽');
    expect(s().pouchPresets.filter((p) => p.name === '緑インコ羽')).toHaveLength(1);
    expect(s().pouchPresets.find((p) => p.name === '緑インコ羽')!.selection.deco).toBe('ribbon');
    // 保存後に選択を変えてもプリセット側は変わらない (スナップショット)
    s().setPouchSelection({ color: null });
    expect(s().pouchPresets.find((p) => p.name === '緑インコ羽')!.selection.color).toBe('green');

    // 初期プリセットも削除できる
    s().deletePouchPreset('花ロボ 黄');
    expect(s().pouchPresets.map((p) => p.name)).toEqual(['花もじゃ 黄', 'メダルもじゃ 黄', '緑インコ羽']);
    s().deletePouchPreset('存在しない');
    expect(s().pouchPresets).toHaveLength(3);

    // 上限: 同名上書きは可、新規追加は不可
    for (let i = s().pouchPresets.length; i < MAX_HIF_POUCH_PRESETS; i++) s().savePouchPreset(`p${i}`);
    expect(s().pouchPresets).toHaveLength(MAX_HIF_POUCH_PRESETS);
    s().savePouchPreset('あふれる');
    expect(s().pouchPresets).toHaveLength(MAX_HIF_POUCH_PRESETS);
    s().setPouchSelection(HANA_MOJA);
    s().savePouchPreset('メダルもじゃ 黄');
    expect(s().pouchPresets.find((p) => p.name === 'メダルもじゃ 黄')!.selection).toEqual(HANA_MOJA);
    // 空名は無視
    s().savePouchPreset('   ');
    expect(s().pouchPresets).toHaveLength(MAX_HIF_POUCH_PRESETS);
  });

  it('executeCalculate: ポーチあり ＝ 手入力に同じ回数を足した結果と一致 (補正なしも)', () => {
    // A: 手入力 +6 本 (ポーチ未選択)
    const calc = useCalcStore.getState();
    useCalcStore.setState({
      additionalCounts: mergeAdditionalCounts(calc.additionalCounts, { p_drink_acquire: 6 }),
    });
    useHifStore.getState().executeCalculate();
    const a = useHifStore.getState();
    expect(a.errorMessage).toBeNull();
    expect(a.calculationResult).not.toBeNull();
    const aFinal = a.calculationResult!.final_status;
    const aBase = a.calculationResultWithoutCharacter!.final_status;
    const aPatterns = a.deckResults.map((d) => ({ ids: d.selected_cards.map((c) => c.card.id).sort(), total: d.total_value }));

    // B: 手入力は元のまま、ポーチ 花もじゃ黄 (+6 本)
    resetStores();
    useHifStore.getState().setPouchSelection(HANA_MOJA);
    useHifStore.getState().executeCalculate();
    const b = useHifStore.getState();
    expect(b.errorMessage).toBeNull();
    expect(b.calculationResult!.final_status).toEqual(aFinal);
    expect(b.calculationResultWithoutCharacter!.final_status).toEqual(aBase);
    expect(b.deckResults.map((d) => ({ ids: d.selected_cards.map((c) => c.card.id).sort(), total: d.total_value }))).toEqual(aPatterns);
    // 手入力の値は変わらない (二重計上しない)
    expect(useCalcStore.getState().additionalCounts.p_drink_acquire).toBe(calc.additionalCounts.p_drink_acquire);

    // パターン切替 (applySelectedPatternImpl) でもポーチ分が維持される (最良パターンへ戻して比較)
    if (b.deckResults.length > 1) {
      const best = b.selectedPatternIndex;
      useHifStore.getState().selectPattern((best + 1) % b.deckResults.length);
      useHifStore.getState().selectPattern(best);
      expect(useHifStore.getState().calculationResult!.final_status).toEqual(aFinal);
    }
  });

  it('共有ペイロード: 選択時だけ hif.pouch を載せ、復元で同じ結果になる', () => {
    useHifStore.getState().setPouchSelection(HANA_MOJA);
    useHifStore.getState().executeCalculate();
    const sharer = useHifStore.getState();
    const sharerFinal = sharer.calculationResult!.final_status;
    const payload = buildSharePayload('hif', useCalcStore.getState(), sharer)!;
    expect(payload.hif?.pouch).toEqual(HANA_MOJA);
    // 回数は復元側が日程から再計算するので additionalCounts には混ぜない
    expect(payload.calc.additionalCounts.p_drink_acquire).toBe(useCalcStore.getState().additionalCounts.p_drink_acquire);
    const sanitized = sanitizeSharePayload(JSON.parse(JSON.stringify(payload)))!;
    expect(sanitized.hif?.pouch).toEqual(HANA_MOJA);

    // 開いた側: ポーチ未選択・手入力ゼロの環境で復元
    resetStores();
    useCalcStore.setState({ additionalCounts: emptyAdditionalCounts() });
    expect(useHifStore.getState().applySharedResult(sanitized)).toBeNull();
    const viewer = useHifStore.getState();
    expect(viewer.pouch).toEqual(HANA_MOJA);
    expect(viewer.calculationResult!.final_status).toEqual(sharerFinal);

    // 未選択なら pouch キー自体を載せない
    resetStores();
    useHifStore.getState().executeCalculate();
    const noPouch = buildSharePayload('hif', useCalcStore.getState(), useHifStore.getState())!;
    expect(noPouch.hif?.pouch).toBeUndefined();
    // 不正な pouch (色なし / 親なしの子) は落とす
    const broken = sanitizeSharePayload(JSON.parse(JSON.stringify({ ...payload, hif: { ...payload.hif, pouch: { plush: 'moja', deco: 'flower' } } })))!;
    expect(broken.hif?.pouch).toBeUndefined();
    const orphan = sanitizeSharePayload(JSON.parse(JSON.stringify({ ...payload, hif: { ...payload.hif, pouch: { color: 'yellow', deco: 'flower' } } })))!;
    expect(orphan.hif?.pouch).toEqual({ color: 'yellow', plush: null, deco: null });
  });

  it('条件プリセット: 保存→読込で選択が戻り、pouch の無い旧プリセットは未選択になる', () => {
    const s = () => useHifStore.getState();
    s().setPouchSelection(HANA_ROBO);
    s().saveConditionPreset('ポーチあり');
    expect(s().conditionPresets[0].hif.pouch).toEqual(HANA_ROBO);

    s().setPouchSelection({ color: null });
    s().loadConditionPreset('ポーチあり');
    expect(s().pouch).toEqual(HANA_ROBO);

    // 旧スキーマ (pouch 無し) を読み込むと未選択にリセット
    const old = JSON.parse(JSON.stringify(s().conditionPresets[0])) as HifConditionPreset;
    delete old.hif.pouch;
    old.name = '旧形式';
    useHifStore.setState({ conditionPresets: [...s().conditionPresets, old] });
    s().loadConditionPreset('旧形式');
    expect(s().pouch).toEqual(emptyHifPouchSelection());

    // 定義に無い id を含むプリセットは整合させて読む
    const bad = JSON.parse(JSON.stringify(s().conditionPresets[0])) as HifConditionPreset;
    bad.name = '不正';
    bad.hif.pouch = { color: 'yellow', plush: 'kuma', deco: 'flower' };
    useHifStore.setState({ conditionPresets: [...s().conditionPresets, bad] });
    s().loadConditionPreset('不正');
    expect(s().pouch).toEqual({ color: 'yellow', plush: null, deco: null });
  });

  it('診断情報: 選択時のみ [カスタムPアイテム] ブロックを出す', () => {
    const report0 = buildHifDiagnosticReport(useHifStore.getState(), useCalcStore.getState(), useAppStore.getState());
    expect(report0).not.toContain('[カスタムPアイテム]');

    useHifStore.getState().setPouchSelection(HANA_MOJA);
    const report = buildHifDiagnosticReport(useHifStore.getState(), useCalcStore.getState(), useAppStore.getState());
    const idx = report.indexOf('[カスタムPアイテム]');
    expect(idx).toBeGreaterThan(report.indexOf('[HIFボーナス]'));
    expect(idx).toBeLessThan(report.indexOf('[HIFスケジュール]'));
    const block = report.slice(idx).split('\n').slice(0, 6);
    expect(block).toEqual([
      '[カスタムPアイテム]',
      '選択: 黄 → もじゃ → 花',
      '未カスタマイズ: レッスン終了時 (×2) W9, W11 → 2回 / Pドリンク獲得+1',
      '1段階目 もじゃ: 相談選択時 (×1) W19 → 1回 / Pドリンク獲得+2',
      '2段階目 花: 相談選択時 (×1) W26 → 1回 / Pドリンク獲得+2',
      '加算: Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0',
    ]);
    // [イベントカウント] は手入力値のまま (ポーチ分を混ぜない)
    const counts = useCalcStore.getState().additionalCounts;
    expect(report).toContain(`Pドリンク獲得: ${counts.p_drink_acquire}`);
  });
});
