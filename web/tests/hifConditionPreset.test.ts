import { describe, it, expect, beforeEach } from 'vitest';
import { useAppStore } from '../src/stores/appStore';
import { useCalcStore } from '../src/stores/calcStore';
import {
  useHifStore,
  defaultChoiceForWeek,
  MAX_HIF_CONDITION_PRESETS,
  type HifConditionPreset,
} from '../src/stores/hifStore';
import { emptyAdditionalCounts, emptyMemoryBonus } from '../src/types/models';
import type { CalculationResult, DeckResult } from '../src/types/results';
import type { TrainingPlan, TurnChoice } from '../src/types/models';
import { loadAllCards, loadPlans, loadPlan, loadCharacters, loadTemplates } from './helpers/loadRealData';

/**
 * HIF条件プリセット (入力条件一式の保存・読込) のストアレベルテスト。
 * node 環境のため localStorage は no-op で、ストアのメモリ内動作を検証する。
 */

function resetStores() {
  useAppStore.setState({
    cards: loadAllCards(),
    plans: loadPlans(),
    templates: loadTemplates(),
    characters: loadCharacters(),
    inventory: [],
    isLoading: false,
    error: null,
  });
  useHifStore.setState({
    scheduleChoices: {},
    examAllocations: {},
    examRatio: { vo: 34, da: 33, vi: 33 },
    bulkLessonDefault: { mainStat: 'vo', subStat: 'da' },
    bulkClassStat: 'vo',
    schedulePresets: [],
    conditionPresets: [],
    deckResults: [],
    selectedPatternIndex: 0,
    calculationResult: null,
    calculationResultWithoutCharacter: null,
    errorMessage: null,
    _lastMainStats: [],
    _lastPlan: null,
    _lastTurnChoices: [],
  });
  useCalcStore.setState({
    selectedPlanType: 'sense',
    voSpCount: 0,
    daSpCount: 0,
    viSpCount: 0,
    additionalCounts: emptyAdditionalCounts(),
    selectedTemplateName: null,
    ownedOnly: false,
    contestMode: false,
    requiredCardIds: [],
    excludedCardIds: [],
    selectedCharacterId: null,
    memoryBonuses: [emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()],
    deckResults: [],
    selectedPatternIndex: 0,
    calculationResult: null,
    calculationResultWithoutCharacter: null,
    errorMessage: null,
  });
}

/** 検証用の不完全プリセットを直接注入する（localStorage 破損・旧スキーマ相当） */
function injectPreset(preset: unknown) {
  useHifStore.setState({ conditionPresets: [preset as HifConditionPreset] });
}

beforeEach(resetStores);

describe('HIF条件プリセット: 保存・読込ラウンドトリップ', () => {
  it('hif側 (スケジュール・試験配分・比率・一括設定) が復元される', () => {
    const hif = useHifStore.getState();
    hif.setBulkLessonDefault({ mainStat: 'da', subStat: 'vi' });
    useHifStore.getState().applyBulkLessonChoice();
    useHifStore.getState().setBulkClassStat('da');
    useHifStore.getState().applyBulkClassChoice();
    useHifStore.getState().setExamRatio({ vo: 0, da: 50, vi: 50 });

    useHifStore.getState().saveConditionPreset('Da全踏み');
    const saved = useHifStore.getState().conditionPresets[0];
    expect(saved.name).toBe('Da全踏み');

    // 全て別の値に変えてから読込
    useHifStore.getState().resetScheduleChoices();
    useHifStore.getState().setBulkLessonDefault({ mainStat: 'vo', subStat: 'da' });
    useHifStore.getState().setBulkClassStat('vo');
    useHifStore.getState().setExamRatio({ vo: 100, da: 0, vi: 0 });

    useHifStore.getState().loadConditionPreset('Da全踏み');
    const s = useHifStore.getState();
    expect(s.scheduleChoices).toEqual(saved.hif.scheduleChoices);
    expect(s.examAllocations).toEqual(saved.hif.examAllocations);
    expect(s.examRatio).toEqual({ vo: 0, da: 50, vi: 50 });
    expect(s.bulkLessonDefault).toEqual({ mainStat: 'da', subStat: 'vi' });
    expect(s.bulkClassStat).toBe('da');
  });

  it('calc側 (育成タイプ・SP・イベント回数・テンプレ名・トグル・カード指定・メモリー) が復元される', () => {
    const ids = loadAllCards().map((c) => c.id);
    const hifTemplate = loadTemplates().find((t) => t.plan_id === 'hif');
    expect(hifTemplate).toBeDefined();

    useCalcStore.setState({
      selectedPlanType: 'logic',
      voSpCount: 2,
      daSpCount: 0,
      viSpCount: 1,
      selectedTemplateName: hifTemplate!.name,
      ownedOnly: true,
      contestMode: true,
      requiredCardIds: [ids[0], ids[1]],
      excludedCardIds: [ids[2]],
    });
    useCalcStore.getState().setAdditionalCount('skill_acquire', 5);
    useCalcStore.getState().setMemoryBonus(0, 'vo', { value: 100 });
    useCalcStore.getState().setMemoryBonus(1, 'da', { value: 3.5, type: 'para' });

    useHifStore.getState().saveConditionPreset('calc側');

    // 全て別の値に変えてから読込
    useCalcStore.setState({
      selectedPlanType: 'sense',
      voSpCount: 0,
      daSpCount: 9,
      viSpCount: 0,
      additionalCounts: emptyAdditionalCounts(),
      selectedTemplateName: null,
      ownedOnly: false,
      contestMode: false,
      requiredCardIds: [],
      excludedCardIds: [],
      memoryBonuses: [emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()],
    });

    useHifStore.getState().loadConditionPreset('calc側');
    const c = useCalcStore.getState();
    expect(c.selectedPlanType).toBe('logic');
    expect([c.voSpCount, c.daSpCount, c.viSpCount]).toEqual([2, 0, 1]);
    expect(c.additionalCounts.skill_acquire).toBe(5);
    expect(c.selectedTemplateName).toBe(hifTemplate!.name);
    expect(c.ownedOnly).toBe(true);
    expect(c.contestMode).toBe(true);
    expect(c.requiredCardIds).toEqual([ids[0], ids[1]]);
    expect(c.excludedCardIds).toEqual([ids[2]]);
    expect(c.memoryBonuses[0].vo).toEqual({ value: 100, type: 'flat' });
    expect(c.memoryBonuses[1].da).toEqual({ value: 3.5, type: 'para' });
  });
});

describe('HIF条件プリセット: 空スケジュール防御 (既知問題の再発防止)', () => {
  it('空スケジュールで保存→読込しても全週シードされ「スケジュール未設定」にならない', () => {
    // ScheduleConfig 未マウント相当: scheduleChoices が空のまま保存
    expect(Object.keys(useHifStore.getState().scheduleChoices).length).toBe(0);
    useHifStore.getState().saveConditionPreset('空のまま保存');

    // 保存時点でシード済みの完全なプリセットになっている
    const saved = useHifStore.getState().conditionPresets[0];
    const hifPlan = loadPlan('hif');
    for (const w of hifPlan.schedule) {
      if (defaultChoiceForWeek(w)) {
        expect(saved.hif.scheduleChoices[w.week]).toBeDefined();
      }
    }

    useHifStore.getState().loadConditionPreset('空のまま保存');
    useHifStore.getState().executeCalculate();
    expect(useHifStore.getState().errorMessage).toBeNull();
    expect(useHifStore.getState().calculationResult).not.toBeNull();
  });
});

describe('HIF条件プリセット: 読込時の検証', () => {
  it('存在しないカードIDは除去され、必須6枚キャップ・重複除去・相互排他が守られる', () => {
    const ids = loadAllCards().map((c) => c.id);
    injectPreset({
      name: 'cards',
      hif: {},
      calc: {
        requiredCardIds: ['__ghost__', ids[0], ids[1], ids[0], ids[2], ids[3], ids[4], ids[5], ids[6]],
        excludedCardIds: [ids[0], ids[7], '__ghost2__'],
      },
    });
    useHifStore.getState().loadConditionPreset('cards');
    const c = useCalcStore.getState();
    // 実在IDのみ・重複除去 → 7件になり、必須上限6でキャップ
    expect(c.requiredCardIds).toEqual([ids[0], ids[1], ids[2], ids[3], ids[4], ids[5]]);
    // 必須に入った ids[0] と存在しないIDは除外リストから落ちる
    expect(c.excludedCardIds).toEqual([ids[7]]);
  });

  it('additionalCounts は未知キーを破棄し、欠落キーは0、負値は0に矯正される', () => {
    injectPreset({
      name: 'counts',
      hif: {},
      calc: {
        additionalCounts: { skill_acquire: 3, unknown_key: 99, p_drink_acquire: -2 },
      },
    });
    useHifStore.getState().loadConditionPreset('counts');
    const counts = useCalcStore.getState().additionalCounts;
    expect(counts.skill_acquire).toBe(3);
    expect((counts as Record<string, number>)['unknown_key']).toBeUndefined();
    expect(counts.p_drink_acquire).toBe(0);
    expect(counts.skill_enhance).toBe(0);
  });

  it('memoryBonuses は4枠に正規化され、不正な type/value は flat/0 に矯正される', () => {
    injectPreset({
      name: 'memory',
      hif: {},
      calc: {
        memoryBonuses: [
          { vo: { value: 5, type: 'para' }, da: { value: 'x', type: 'bogus' }, vi: { value: 2 } },
          { vo: { value: 1, type: 'flat' } },
        ],
      },
    });
    useHifStore.getState().loadConditionPreset('memory');
    const m = useCalcStore.getState().memoryBonuses;
    expect(m).toHaveLength(4);
    expect(m[0].vo).toEqual({ value: 5, type: 'para' });
    expect(m[0].da).toEqual({ value: 0, type: 'flat' });
    expect(m[0].vi).toEqual({ value: 2, type: 'flat' });
    expect(m[1].vo).toEqual({ value: 1, type: 'flat' });
    expect(m[1].da).toEqual(emptyMemoryBonus().da);
    expect(m[2]).toEqual(emptyMemoryBonus());
    expect(m[3]).toEqual(emptyMemoryBonus());
  });

  it('キャラ選択も復元される (null=解除、実在しないIDやフィールド無しは現状維持)', () => {
    const chars = loadCharacters();
    const c0 = chars[0].id;
    const c1 = chars[1].id;

    // 保存したキャラが復元される
    useCalcStore.setState({ selectedCharacterId: c0 });
    useHifStore.getState().saveConditionPreset('キャラ付き');
    useCalcStore.setState({ selectedCharacterId: c1 });
    useHifStore.getState().loadConditionPreset('キャラ付き');
    expect(useCalcStore.getState().selectedCharacterId).toBe(c0);

    // null はキャラ解除として復元
    injectPreset({ name: 'キャラなし', hif: {}, calc: { selectedCharacterId: null } });
    useHifStore.getState().loadConditionPreset('キャラなし');
    expect(useCalcStore.getState().selectedCharacterId).toBeNull();

    // 実在しないIDは現状維持
    useCalcStore.setState({ selectedCharacterId: c1 });
    injectPreset({ name: 'ghost', hif: {}, calc: { selectedCharacterId: '__ghost__' } });
    useHifStore.getState().loadConditionPreset('ghost');
    expect(useCalcStore.getState().selectedCharacterId).toBe(c1);
  });

  it('examAllocations が空なら保存された examRatio から按分生成される', () => {
    injectPreset({
      name: 'ratioOnly',
      hif: {
        scheduleChoices: {},
        examAllocations: {},
        examRatio: { vo: 100, da: 0, vi: 0 },
      },
      calc: {},
    });
    useHifStore.getState().loadConditionPreset('ratioOnly');
    const s = useHifStore.getState();
    const hifPlan = loadPlan('hif');
    let examWeeks = 0;
    for (const w of hifPlan.schedule) {
      const d = w.hif_exam_distributed ?? 0;
      if (w.type === 'audition' && d > 0) {
        examWeeks++;
        expect(s.examAllocations[w.week]).toEqual({ vo: d, da: 0, vi: 0 });
      }
    }
    expect(examWeeks).toBeGreaterThan(0);
    expect(s.examRatio).toEqual({ vo: 100, da: 0, vi: 0 });
  });

  it('欠落フィールドだらけの旧スキーマ相当プリセットでも throw せず部分適用される', () => {
    injectPreset({ name: 'partial', hif: { scheduleChoices: {} } });
    useCalcStore.setState({ selectedPlanType: 'anomaly', voSpCount: 3 });

    expect(() => useHifStore.getState().loadConditionPreset('partial')).not.toThrow();

    // calc セクション欠落 → calcStore は現状維持
    const c = useCalcStore.getState();
    expect(c.selectedPlanType).toBe('anomaly');
    expect(c.voSpCount).toBe(3);

    // hif 側はシード補完・試験配分はデフォルト比率から生成される
    const s = useHifStore.getState();
    const hifPlan = loadPlan('hif');
    for (const w of hifPlan.schedule) {
      if (defaultChoiceForWeek(w)) {
        expect(s.scheduleChoices[w.week]).toBeDefined();
      }
    }
    expect(Object.keys(s.examAllocations).length).toBeGreaterThan(0);
  });

  it('読込で古い計算結果・内部スナップショットが全てクリアされる', () => {
    useHifStore.getState().saveConditionPreset('A');
    useHifStore.setState({
      errorMessage: 'dirty',
      deckResults: [{} as unknown as DeckResult],
      selectedPatternIndex: 2,
      calculationResult: {} as unknown as CalculationResult,
      calculationResultWithoutCharacter: {} as unknown as CalculationResult,
      _lastMainStats: ['vo', 'da'],
      _lastPlan: {} as unknown as TrainingPlan,
      _lastTurnChoices: [{} as unknown as TurnChoice],
    });

    useHifStore.getState().loadConditionPreset('A');
    const s = useHifStore.getState();
    expect(s.errorMessage).toBeNull();
    expect(s.deckResults).toEqual([]);
    expect(s.selectedPatternIndex).toBe(0);
    expect(s.calculationResult).toBeNull();
    expect(s.calculationResultWithoutCharacter).toBeNull();
    expect(s._lastMainStats).toEqual([]);
    expect(s._lastPlan).toBeNull();
    expect(s._lastTurnChoices).toEqual([]);
  });
});

describe('HIF条件プリセット: 上限・上書き・削除', () => {
  it('空名・空白名は保存されない', () => {
    useHifStore.getState().saveConditionPreset('');
    useHifStore.getState().saveConditionPreset('   ');
    expect(useHifStore.getState().conditionPresets).toHaveLength(0);
  });

  it('上限10件で新規は拒否されるが、同名上書きは上限時も成功する', () => {
    for (let i = 1; i <= MAX_HIF_CONDITION_PRESETS; i++) {
      useHifStore.getState().saveConditionPreset(`P${i}`);
    }
    expect(useHifStore.getState().conditionPresets).toHaveLength(MAX_HIF_CONDITION_PRESETS);

    useHifStore.getState().saveConditionPreset('P11');
    expect(useHifStore.getState().conditionPresets).toHaveLength(MAX_HIF_CONDITION_PRESETS);
    expect(useHifStore.getState().conditionPresets.some((p) => p.name === 'P11')).toBe(false);

    // 同名上書きは成功し、内容が更新される
    useCalcStore.setState({ voSpCount: 7 });
    useHifStore.getState().saveConditionPreset('P5');
    expect(useHifStore.getState().conditionPresets).toHaveLength(MAX_HIF_CONDITION_PRESETS);
    const p5 = useHifStore.getState().conditionPresets.find((p) => p.name === 'P5');
    expect(p5?.calc.voSpCount).toBe(7);
  });

  it('削除で消え、未知の名前の削除は no-op', () => {
    useHifStore.getState().saveConditionPreset('A');
    useHifStore.getState().saveConditionPreset('B');
    useHifStore.getState().deleteConditionPreset('A');
    expect(useHifStore.getState().conditionPresets.map((p) => p.name)).toEqual(['B']);
    useHifStore.getState().deleteConditionPreset('__nope__');
    expect(useHifStore.getState().conditionPresets.map((p) => p.name)).toEqual(['B']);
  });
});
