import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { useAppStore } from '../src/stores/appStore';
import { useCalcStore } from '../src/stores/calcStore';
import { useHifStore } from '../src/stores/hifStore';
import { sanitizeSharePayload, type SharePayload } from '../src/services/shareState';
import { calculate } from '../src/services/statusCalculation';
import { defaultHifBonusLevels } from '../src/types/hifBonus';
import { emptyAdditionalCounts, emptyMemoryBonus } from '../src/types/models';
import type { StatusValues } from '../src/types/models';
import {
  loadAllCards,
  loadPlans,
  loadPlan,
  loadCharacters,
  loadTemplates,
  REPO_ROOT,
} from './helpers/loadRealData';
import {
  buildHifPlanAndChoices,
  resolveHifBonusLevels,
  applyHifFinalCap,
  buildHifEffectiveCharacter,
} from './helpers/hifScenario';

/**
 * 回帰テスト: ユーザ報告 (2026-09) HIF「もうすぐ本番ですね (SP_SSR_0110) を採用した方が数字が下がる」
 * (C# 版 ReproHifTriggerCounterShareTests と対)。
 *
 * 報告に添えられた共有 URL 2本 (アノマリー / 花海咲季 / 3凸OFF / STEP4 ON / ボーナスLv全MAX、
 * 日程・試験配分・イベント回数・メモリーは同一) を復号したフィクスチャを使う。
 * 差は編成 1 枚だけ: 0110 (1凸) が入るか、代わりに 佑芽ソリレース、疾走！ (SP_SR_0063) が入るか。
 *
 * バグ: fireTrigger の発動回数カウンタが `カードID_トリガー_属性` キーで、0110 の通常アビリティと
 *       Pアイテム効果 (どちらもスキルチェンジ / Vi / 3回) が上限回数を共有し、3回で 27+25+27 しか出なかった。
 *       最適化側は効果ごとに独立に見積もる (156) ため 0110 を採用し、実計算では 0063 の編成を下回った。
 *
 * 検証: (1) フィクスチャからの手動再構築 (calculate) がストア復元 (applySharedResult) と一致する
 *          = 両版共通の再構築規則が本番経路と同じであることの担保。
 *       (2) 0110 (1凸) を採用した編成の cap 後合計が、0063 の編成を上回る (自動ピックの判断が正しい)。
 * teeth: 修正前は 6705 < 6720 で赤。修正後は 6782 > 6720 で緑。
 */

const fixture = JSON.parse(
  readFileSync(resolve(REPO_ROOT, 'TestFixtures', 'hif_trigger_counter_share.json'), 'utf-8'),
) as Record<string, unknown>;
const payloadWith = sanitizeSharePayload(fixture);
const payloadWithout = sanitizeSharePayload({ ...fixture, deck: fixture.deck_without_0110 });

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

interface Outcome {
  /** cap 後合計 (パターン表示の合計と同じ) */
  total: number;
  final: StatusValues;
  cap: number;
}

/** 本番経路: ストアで共有結果を復元して計算 */
function viaStore(payload: SharePayload): Outcome {
  resetStores();
  const err = useHifStore.getState().applySharedResult(payload);
  expect(err).toBeNull();
  const s = useHifStore.getState();
  return {
    total: s.deckResults[0].total_value,
    final: s.calculationResult!.final_status,
    cap: s._lastPlan!.status_limit,
  };
}

/** 両版共通の再構築規則 (C# 版と同じ手順) で calculate を直接呼ぶ */
function viaCalculate(payload: SharePayload): Outcome {
  const cards = loadAllCards();
  const hif = payload.hif!;
  const levels = resolveHifBonusLevels(hif.bonusLevels);
  const built = buildHifPlanAndChoices(loadPlan('hif'), hif.scheduleChoices, hif.examAllocations);
  const plan = applyHifFinalCap(built.plan, levels);
  const character = loadCharacters().find((c) => c.id === payload.calc.characterId) ?? null;
  const effectiveChar = buildHifEffectiveCharacter(character, payload.calc.uncap3, payload.calc.step4, levels);
  const deckCards = payload.deck.map((d) => cards.find((c) => c.id === d.id)!);
  const uncapLevels: Record<string, number> = {};
  for (const d of payload.deck) uncapLevels[d.id] = d.rental ? 4 : d.uncap;
  const additionalCounts = { ...emptyAdditionalCounts(), ...payload.calc.additionalCounts };
  const final = calculate(
    plan, deckCards, built.turnChoices, uncapLevels, additionalCounts, effectiveChar, payload.calc.memoryBonuses,
  ).final_status;
  const cap = plan.status_limit;
  return {
    total: Math.min(final.vo, cap) + Math.min(final.da, cap) + Math.min(final.vi, cap),
    final,
    cap,
  };
}

describe('HIF 回帰 (ユーザ報告 2026-09: もうすぐ本番ですねを採用した方が数字が下がる)', () => {
  it('フィクスチャは共有ペイロードとして妥当で、差は 0110 (1凸) ↔ SR_0063 の1枚だけ', () => {
    expect(payloadWith).not.toBeNull();
    expect(payloadWithout).not.toBeNull();
    const w = payloadWith!.deck.find((d) => d.id === 'SP_SSR_0110');
    expect(w).toEqual({ id: 'SP_SSR_0110', uncap: 1, rental: false, required: false });
    expect(payloadWithout!.deck.some((d) => d.id === 'SP_SSR_0110')).toBe(false);
    const idsWith = payloadWith!.deck.map((d) => d.id).filter((id) => id !== 'SP_SSR_0110').sort();
    const idsWithout = payloadWithout!.deck.map((d) => d.id).filter((id) => id !== 'SP_SR_0063').sort();
    expect(idsWith).toEqual(idsWithout);
  });

  it('手動再構築 (calculate) がストア復元 (applySharedResult) と一致する', () => {
    for (const payload of [payloadWithout!, payloadWith!]) {
      const store = viaStore(payload);
      const manual = viaCalculate(payload);
      expect(manual.final).toEqual(store.final);
      expect(manual.cap).toBe(store.cap);
      expect(manual.total).toBe(store.total);
    }
  });

  it('0110 (1凸) を採用した編成の cap 後合計は、SR_0063 が入った編成を上回る', () => {
    const without = viaStore(payloadWithout!);
    const withCard = viaStore(payloadWith!);
    // 比較が Vi の差を反映するよう、両編成とも Vi が上限に張り付いていないこと
    expect(without.final.vi).toBeLessThan(without.cap);
    expect(withCard.final.vi).toBeLessThan(withCard.cap);
    expect(
      withCard.total,
      `0110 採用 (${withCard.total}) が不採用 (${without.total}) を下回った: ` +
        '同一カード内の通常アビリティと Pアイテム効果が発動回数の上限を共有している',
    ).toBeGreaterThan(without.total);
  });
});
