import { describe, it, expect, beforeEach } from 'vitest';
import { useAppStore } from '../src/stores/appStore';
import { useCalcStore } from '../src/stores/calcStore';
import { useHifStore, defaultChoiceForWeek } from '../src/stores/hifStore';
import { buildSharePayload } from '../src/services/sharePayload';
import { decodeSharePayload, encodeSharePayload } from '../src/services/shareState';
import { defaultHifBonusLevels } from '../src/types/hifBonus';
import { emptyAdditionalCounts, emptyMemoryBonus } from '../src/types/models';
import type { CalculationResult, DeckResult } from '../src/types/results';
import type { TrainingPlan, TurnChoice } from '../src/types/models';
import {
  loadAllCards,
  loadPlans,
  loadCharacters,
  loadTemplates,
  templateAdditionalCounts,
} from './helpers/loadRealData';

/**
 * 共有 URL の結果復元 (ストアレベル、実データ)。
 *
 * 共有元: 通常の計算実行 → 表示中パターンからペイロード生成 → エンコード。
 * 開いた側: 所持カードなし・別キャラ・別ボーナスLv の環境で復元 → 共有元と同じ
 * 到達ステータス・編成 (ID/凸/レンタル) が表示されることを確認する。
 * node 環境のため localStorage は no-op で、ストアのメモリ内動作のみを検証する。
 */

const characters = loadCharacters();
const sharerCharacter = characters[0];
const viewerCharacter = characters[1] ?? characters[0];

function resetStores() {
  useAppStore.setState({
    cards: loadAllCards(),
    plans: loadPlans(),
    templates: loadTemplates(),
    characters,
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
    uncap3BonusEnabled: false,
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

/** HIF プランの最初の公開レッスン日 (一括設定の反映確認用) */
function hifPlanPublicLessonWeek(): number {
  const hifPlan = useAppStore.getState().plans.find((p) => p.id === 'hif')!;
  return hifPlan.schedule.find((w) => w.type === 'public_lesson')!.week;
}

/** HIF の UI (ScheduleConfig) と同じく全週にデフォルト選択を入れ、試験配分を materialize する */
function seedHifSchedule() {
  const hifPlan = useAppStore.getState().plans.find((p) => p.id === 'hif')!;
  for (const week of hifPlan.schedule) {
    const def = defaultChoiceForWeek(week);
    if (def) useHifStore.getState().setScheduleChoice(week.week, def);
  }
  useHifStore.getState().ensureExamAllocations();
}

interface Snapshot {
  result: CalculationResult;
  deck: DeckResult;
  plan: TrainingPlan | null;
  turnChoices: TurnChoice[];
}

function deckSignature(deck: DeckResult) {
  return deck.selected_cards.map((cs) => ({
    id: cs.card.id,
    uncap: cs.is_rental ? 4 : cs.uncap_level,
    rental: cs.is_rental,
    required: cs.is_required,
    total: cs.total_value,
  }));
}

beforeEach(resetStores);

describe('共有結果の復元: HIF', () => {
  it('所持カードなし・別キャラ・別ボーナスLv の環境でも共有元と同じ結果になる', async () => {
    // --- 共有元 ---
    seedHifSchedule();
    useHifStore.getState().setBulkLessonDefault({ mainStat: 'da', subStat: 'vi' });
    useHifStore.getState().applyBulkLessonChoice();
    useHifStore.getState().setBulkClassStat('vi');
    useHifStore.getState().applyBulkClassChoice();
    useHifStore.getState().applyExamAllocationPreset('vo_da');
    useHifStore.getState().setBonusLevel('viUpLevel', 0);
    useHifStore.getState().setBonusLevel('finalStatLimitLevel', 3);
    useCalcStore.setState({
      selectedPlanType: 'sense',
      selectedCharacterId: sharerCharacter.id,
      uncap3BonusEnabled: true,
      step4BonusEnabled: false,
      voSpCount: 1,
      additionalCounts: templateAdditionalCounts('hif', 'センス'),
      memoryBonuses: [
        { vo: { value: 200, type: 'flat' }, da: { value: 0, type: 'flat' }, vi: { value: 5, type: 'para' } },
        emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus(),
      ],
    });
    useHifStore.getState().executeCalculate();
    const hif = useHifStore.getState();
    expect(hif.errorMessage).toBeNull();
    expect(hif.calculationResult).not.toBeNull();
    const sharer: Snapshot = {
      result: hif.calculationResult!,
      deck: hif.deckResults[hif.selectedPatternIndex],
      plan: hif._lastPlan,
      turnChoices: hif._lastTurnChoices,
    };

    const payload = buildSharePayload('hif', useCalcStore.getState(), hif);
    expect(payload).not.toBeNull();
    expect(payload!.plan).toBe('hif');
    expect(payload!.deck).toHaveLength(sharer.deck.selected_cards.length);
    const decoded = await decodeSharePayload(await encodeSharePayload(payload!));
    expect(decoded).not.toBeNull();

    // --- 開いた側: 別の環境 ---
    resetStores();
    useCalcStore.setState({
      selectedPlanType: 'logic',
      selectedCharacterId: viewerCharacter.id,
      uncap3BonusEnabled: false,
      step4BonusEnabled: true,
    });
    useHifStore.setState({ bonusLevels: { ...defaultHifBonusLevels(), voUpLevel: 1, daUpLevel: 1 } });

    const error = useHifStore.getState().applySharedResult(decoded!);
    expect(error).toBeNull();

    const viewer = useHifStore.getState();
    expect(viewer.shareView).not.toBeNull();
    expect(viewer.shareView!.planId).toBe('hif');
    expect(viewer.deckResults).toHaveLength(1);
    expect(viewer.calculationResult).not.toBeNull();

    // 到達ステータス (cap前の生値) が一致
    expect(viewer.calculationResult!.final_status).toEqual(sharer.result.final_status);
    // 編成 (ID・凸・レンタル・必須・カード別寄与) が表示順ごと一致
    expect(deckSignature(viewer.deckResults[0])).toEqual(deckSignature(sharer.deck));
    // パターン合計 (cap後) も一致
    expect(viewer.deckResults[0].total_value).toBe(sharer.deck.total_value);
    // 計算に使った日程・プラン (status_limit 込み) が一致
    expect(viewer._lastTurnChoices).toEqual(sharer.turnChoices);
    expect(viewer._lastPlan?.status_limit).toBe(sharer.plan?.status_limit);

    // 条件も共有元の値に置き換わっている (永続化はしない)
    const calc = useCalcStore.getState();
    expect(calc.selectedPlanType).toBe('sense');
    expect(calc.selectedCharacterId).toBe(sharerCharacter.id);
    expect(calc.uncap3BonusEnabled).toBe(true);
    expect(calc.step4BonusEnabled).toBe(false);
    expect(calc.voSpCount).toBe(1);
    expect(viewer.bonusLevels.viUpLevel).toBe(0);
    expect(viewer.bonusLevels.finalStatLimitLevel).toBe(3);
    // 一括設定の表示値 (計算には使わないが UI の見た目を共有元に揃える)
    expect(viewer.bulkLessonDefault).toEqual({ mainStat: 'da', subStat: 'vi' });
    expect(viewer.bulkClassStat).toBe('vi');
    expect(viewer.scheduleChoices[hifPlanPublicLessonWeek()]).toEqual({ action: 'da_lesson', sub_stat: 'vi' });
  });

  it('exitShareView でボーナスLv・凸トグルが自分の設定に戻り、計算実行で共有ビューが終わる', async () => {
    seedHifSchedule();
    useHifStore.getState().setBonusLevel('viUpLevel', 0);
    useCalcStore.setState({ selectedCharacterId: sharerCharacter.id, uncap3BonusEnabled: true });
    useHifStore.getState().executeCalculate();
    const payload = buildSharePayload('hif', useCalcStore.getState(), useHifStore.getState())!;

    resetStores();
    // 開いた側は 3凸トグルをこのキャラで OFF (マップに無い=既定OFF) にしている
    expect(useHifStore.getState().applySharedResult(payload)).toBeNull();
    expect(useCalcStore.getState().uncap3BonusEnabled).toBe(true);
    expect(useHifStore.getState().bonusLevels.viUpLevel).toBe(0);

    useHifStore.getState().exitShareView(false);
    expect(useHifStore.getState().shareView).toBeNull();
    // node では localStorage が無いので既定 (全パネル MAX) に戻る
    expect(useHifStore.getState().bonusLevels).toEqual(defaultHifBonusLevels());
    expect(useCalcStore.getState().uncap3BonusEnabled).toBe(false);
    // 結果自体は残る (再計算するまで表示は維持)
    expect(useHifStore.getState().calculationResult).not.toBeNull();

    // 再度復元してから計算実行 → 共有ビューは終了し、通常の複数パターン結果になる
    resetStores();
    expect(useHifStore.getState().applySharedResult(payload)).toBeNull();
    useHifStore.getState().executeCalculate();
    expect(useHifStore.getState().shareView).toBeNull();
    expect(useHifStore.getState().deckResults.length).toBeGreaterThan(1);
  });

  it('現在のデータに無いカード・別タブのペイロードはエラーメッセージを返す', async () => {
    seedHifSchedule();
    useHifStore.getState().executeCalculate();
    const payload = buildSharePayload('hif', useCalcStore.getState(), useHifStore.getState())!;

    resetStores();
    const missing = { ...payload, deck: [{ ...payload.deck[0], id: '__missing__' }] };
    expect(useHifStore.getState().applySharedResult(missing)).toMatch(/存在しないカード/);
    expect(useHifStore.getState().shareView).toBeNull();

    const otherTab = { ...payload, plan: 'nia' };
    expect(useHifStore.getState().applySharedResult(otherTab)).toMatch(/このタブでは表示できません/);
  });
});

describe('共有結果の復元: 日程方式 (初レジェンド / NIA)', () => {
  for (const planId of ['hatsu_legend', 'nia'] as const) {
    it(`${planId}: 所持カードなし・別キャラの環境でも共有元と同じ結果になる`, async () => {
      // --- 共有元 ---
      useCalcStore.getState().setSelectedPlanId(planId);
      useCalcStore.getState().seedScheduleDefaults(planId);
      useCalcStore.setState({
        selectedPlanType: 'logic',
        selectedCharacterId: sharerCharacter.id,
        daSpCount: 1,
      });
      useCalcStore.getState().executeCalculate();
      const calc = useCalcStore.getState();
      expect(calc.errorMessage).toBeNull();
      expect(calc.calculationResult).not.toBeNull();
      const sharer: Snapshot = {
        result: calc.calculationResult!,
        deck: calc.deckResults[calc.selectedPatternIndex],
        plan: null,
        turnChoices: calc._lastTurnChoices,
      };

      const payload = buildSharePayload('calc', calc, useHifStore.getState());
      expect(payload).not.toBeNull();
      expect(payload!.plan).toBe(planId);
      expect(payload!.sched).toBeDefined();
      const decoded = await decodeSharePayload(await encodeSharePayload(payload!));
      expect(decoded).not.toBeNull();

      // --- 開いた側 ---
      resetStores();
      useCalcStore.getState().setSelectedPlanId(planId);
      useCalcStore.setState({ selectedPlanType: 'sense', selectedCharacterId: viewerCharacter.id });

      const error = useCalcStore.getState().applySharedResult(decoded!);
      expect(error).toBeNull();

      const viewer = useCalcStore.getState();
      expect(viewer.shareView?.planId).toBe(planId);
      expect(viewer.selectedPlanId).toBe(planId);
      expect(viewer.deckResults).toHaveLength(1);
      expect(viewer.calculationResult!.final_status).toEqual(sharer.result.final_status);
      expect(deckSignature(viewer.deckResults[0])).toEqual(deckSignature(sharer.deck));
      expect(viewer.deckResults[0].total_value).toBe(sharer.deck.total_value);
      expect(viewer._lastTurnChoices).toEqual(sharer.turnChoices);
      expect(viewer.selectedPlanType).toBe('logic');
      expect(viewer.selectedCharacterId).toBe(sharerCharacter.id);

      // タブ切替 (setSelectedPlanId) で共有ビューは終了する
      useCalcStore.getState().setSelectedPlanId(planId === 'nia' ? 'hatsu_legend' : 'nia');
      expect(useCalcStore.getState().shareView).toBeNull();
    });
  }
});
