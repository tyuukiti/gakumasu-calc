import type { SupportCard, TrainingPlan, AdditionalCounts, TurnChoice, Character, MemoryBonus } from '../../types/models';
import { additionalCountsToRecord } from '../../types/models';
import type { CardScore, DeckResult } from '../../types/results';
import { calculate } from '../statusCalculation';
import type { OverflowPenaltyConfig } from './types';
import { calculateCardContribution, computeTriggerBonusInfo, countTriggers, estimateBaseStats, calculateLessonStatTotals } from './contribution';
import { optimizeRentalAssignment } from './rental';
import { buildAbilitySummary, buildTurnChoices, generateLabel, recalculateWithCap, recomputeBreakdownsDeckAware } from './results';
import { selectOptimalDeck } from './selection';

// --- Select multiple patterns ---

export function selectMultiplePatterns(
  plan: TrainingPlan,
  allCards: SupportCard[],
  mainStats: string[],
  subStat: string,
  totalLessonWeeks: number,
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  requiredCardIds?: string[],
): DeckResult[] {
  const results: DeckResult[] = [];

  if (mainStats.length < 2) return results;

  const main1 = mainStats[0];
  const main2 = mainStats[1];

  // SP率カードの必要枚数を属性別に集計
  const spMain1 = spCounts?.[main1] ?? 0;
  const spMain2 = spCounts?.[main2] ?? 0;
  // spSub is available for future use
  void (spCounts?.[subStat] ?? 0);

  // カード枚数パターン (メイン1:メイン2:フリー枠 = 合計6枚)
  const patterns: [number, number, number][] = [
    [3, 2, 1],
    [2, 3, 1],
    [3, 3, 0],
    [2, 2, 2],
    [0, 0, 5], // フリー5 + サブ1 (サブはcardTypeSlotsで指定)
  ];

  for (const [m1, m2, free] of patterns) {
    // レンタルモード(所持5+レンタル1)では、フリー枠なし6枚パターンは
    // 属性枠が所持枠(5)を超えるため [3,2,1] / [2,3,1] と重複する → スキップ
    if (rentalPool != null && free === 0 && m1 + m2 > 5) continue;

    // SP枚数を満たせないパターンはスキップ (フリー枠でSP率カードを吸収できる場合はOK)
    const spShortage =
      Math.max(0, spMain1 - m1) + Math.max(0, spMain2 - m2);
    if (spShortage > free) continue;

    // カード枚数
    const cardTypeSlots: Record<string, number> = {};
    if (m1 > 0) cardTypeSlots[main1] = m1;
    if (m2 > 0) cardTypeSlots[main2] = m2;
    let freeSlots = free;

    // フリー5パターン: サブ属性1枚を固定枠に追加
    if (m1 === 0 && m2 === 0) {
      cardTypeSlots[subStat] = 1;
      freeSlots = 5;
    }

    // レッスン配分: メイン1のレッスン回数が多い
    const lessonAllocation: Record<string, number> = {
      [main1]: 0,
      [main2]: 0,
      [subStat]: 0,
    };
    const remaining = totalLessonWeeks;
    lessonAllocation[main1] += remaining - Math.floor(remaining / 2);
    lessonAllocation[main2] += Math.floor(remaining / 2);

    const result = selectOptimalDeck(
      plan,
      allCards,
      lessonAllocation,
      cardTypeSlots,
      mainStats,
      spCounts,
      planType,
      additionalCounts,
      uncapLevels,
      rentalPool,
      freeSlots,
      requiredCardIds,
    );
    results.push(result);
  }

  return results;
}

// --- HIF mode: 属性別3枚パターン + オールフリー ---

/**
 * HIFモード専用のパターン選出。メイン/サブの概念を捨て、
 * Vo/Da/Vi 各属性で「3枚 + フリー2」と「オールフリー」の合計4パターンを生成する。
 *
 * lessonAllocation はユーザが選んだスケジュールから集計した実際のレッスン回数を渡す。
 */
export function selectMultiplePatternsHif(
  plan: TrainingPlan,
  allCards: SupportCard[],
  mainStats: string[],
  lessonAllocation: Record<string, number>,
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  requiredCardIds?: string[],
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  turnChoicesOverride?: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): DeckResult[] {
  const results: DeckResult[] = [];

  // HIFパターン: 属性別2枚+フリー3 と オールフリー
  const patterns: Array<{ stat: 'vo' | 'da' | 'vi' | null; count: number; free: number }> = [
    { stat: 'vo', count: 2, free: 3 },
    { stat: 'da', count: 2, free: 3 },
    { stat: 'vi', count: 2, free: 3 },
    { stat: null, count: 0, free: 5 }, // オールフリー
  ];

  for (const p of patterns) {
    const cardTypeSlots: Record<string, number> = {};
    if (p.stat != null && p.count > 0) {
      cardTypeSlots[p.stat] = p.count;
    }

    // SP率必要枚数の検査: フリー枠でも吸収できるかチェック。
    // レンタル枠(6枚目)にもSPカードを1枚置けるため容量に加算する。
    // 加算しないと SP合計6 (例: Vo4+Da2) で全パターンがスキップされ
    // 「有効な編成パターンが見つかりませんでした」になる (issue #145)。
    let spShortage = 0;
    for (const stat of ['vo', 'da', 'vi'] as const) {
      const required = spCounts?.[stat] ?? 0;
      const provided = cardTypeSlots[stat] ?? 0;
      spShortage += Math.max(0, required - provided);
    }
    if (spShortage > p.free + (rentalPool != null ? 1 : 0)) continue;

    const result = selectOptimalDeck(
      plan,
      allCards,
      lessonAllocation,
      cardTypeSlots,
      mainStats,
      spCounts,
      planType,
      additionalCounts,
      uncapLevels,
      rentalPool,
      p.free,
      requiredCardIds,
      character ?? null,
      memoryBonuses ?? null,
      turnChoicesOverride,
      overflowPenalty,
    );
    results.push(result);
  }

  // cross-seed 大域最適化: 各パターンの greedy は属性偏重の局所最適へ収束しやすく、特に型制約の
  // ない「フリー5」は Da偏重 basin に落ちて balanced 最適へ単一スワップで渡れないことがある。
  // 一方 Vo/Vi 偏重パターンのデッキを種に、型制約なし(SP枚数+必須のみ)で joint 単一スワップ
  // 山登りすると balanced 最適へ届く。全パターンのデッキを種に山登りし、得た大域最良を
  // 「フリー5」枠へ反映する (現フリー5を上回る場合のみ・単調改善)。
  crossSeedFreeDeck(
    results, plan, allCards, lessonAllocation, mainStats, spCounts, planType,
    additionalCounts, uncapLevels, rentalPool, requiredCardIds,
    character ?? null, memoryBonuses ?? null, turnChoicesOverride, overflowPenalty,
  );

  return results;
}

/**
 * HIFパターン群の「フリー5」枠を、全パターンのデッキを種にした joint 単一スワップ山登りで
 * 求めた大域最良デッキに置き換える (改善時のみ)。属性偏重 greedy の basin を跨いで
 * balanced 最適を拾うための cross-seed。制約は SP枚数 + 必須カードのみ (フリー5は型制約なし)。
 * レンタル枠は1枚を4凸借用として評価する (rentalPool 指定時)。
 */
export function crossSeedFreeDeck(
  results: DeckResult[],
  plan: TrainingPlan,
  ownedCards: SupportCard[],
  lessonAllocation: Record<string, number>,
  mainStats: string[],
  spCounts: Record<string, number> | undefined,
  planType: string | undefined,
  additionalCounts: AdditionalCounts | undefined,
  uncapLevels: Record<string, number> | undefined,
  rentalPool: SupportCard[] | undefined,
  requiredCardIds: string[] | undefined,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  turnChoicesOverride: TurnChoice[] | undefined,
  overflowPenalty: OverflowPenaltyConfig | undefined,
): void {
  if (results.length === 0) return;
  const freeLabel = generateLabel({}, 5);
  const freeIdx = results.findIndex((r) => r.label === freeLabel);
  if (freeIdx < 0) return;

  const statCap = plan.status_limit;
  const turnChoices = turnChoicesOverride ?? buildTurnChoices(plan, mainStats);
  const requiredSet = new Set(requiredCardIds ?? []);

  // 共有コンテキスト (raw寄与ランキング & 最終 DeckResult 生成で使用)
  const triggerCounts = countTriggers(plan, lessonAllocation, mainStats, turnChoicesOverride);
  if (additionalCounts != null) {
    for (const [k, v] of Object.entries(additionalCountsToRecord(additionalCounts))) {
      if (v > 0) triggerCounts[k] = (triggerCounts[k] ?? 0) + v;
    }
  }
  const baseStats = estimateBaseStats(plan, lessonAllocation, turnChoicesOverride);
  const lessonStatTotals = calculateLessonStatTotals(plan, lessonAllocation, turnChoicesOverride);
  const triggerBonusInfo = computeTriggerBonusInfo(ownedCards, uncapLevels);

  const planOk = (c: SupportCard) =>
    planType == null || planType === '' || c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free';

  // 所持枠 / レンタル枠の候補を raw寄与上位に絞る (計算量削減)
  const TOP_N = 40;
  const rawTotalOf = (c: SupportCard, asRental: boolean): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    if (asRental) uc[c.id] = 4;
    const cs = calculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
    return cs.raw_vo + cs.raw_da + cs.raw_vi;
  };
  const rankTopN = (pool: SupportCard[], asRental: boolean): SupportCard[] =>
    pool.filter(planOk).map((c) => ({ c, s: rawTotalOf(c, asRental) }))
      .sort((a, b) => b.s - a.s).slice(0, TOP_N).map((x) => x.c);
  const ownedRanked = rankTopN(ownedCards, false);
  const rentalRanked = rankTopN(rentalPool ?? ownedCards, true);

  const coversStat = (card: SupportCard, stat: string) =>
    card.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'));
  const meetsSp = (cards: SupportCard[]) => {
    if (spCounts == null) return true;
    for (const [s, n] of Object.entries(spCounts)) {
      if (n <= 0) continue;
      if (cards.filter((c) => coversStat(c, s)).length < n) return false;
    }
    return true;
  };
  const hasRequired = (cards: SupportCard[]) => {
    for (const id of requiredSet) if (!cards.some((c) => c.id === id)) return false;
    return true;
  };

  const evalTotal = (cards: SupportCard[], rentalIdx: number): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    if (rentalIdx >= 0) uc[cards[rentalIdx].id] = 4;
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).final_status;
    let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const o = Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (o > overflowPenalty.threshold) total -= o * 2;
    }
    return total;
  };

  // joint 単一スワップ山登り (所持枠は ownedRanked, レンタル枠は rentalRanked から; 改善時のみ採用)
  const hillClimb = (start: SupportCard[], rentalIdx: number): { cards: SupportCard[]; total: number } => {
    let cur = [...start];
    let curTotal = evalTotal(cur, rentalIdx);
    let improved = true;
    let guard = 0;
    while (improved && guard++ < 20) {
      improved = false;
      for (let slot = 0; slot < cur.length; slot++) {
        if (requiredSet.has(cur[slot].id)) continue; // 必須カードは固定
        const pool = slot === rentalIdx ? rentalRanked : ownedRanked;
        for (const cand of pool) {
          if (cur.some((c, i) => i !== slot && c.id === cand.id)) continue;
          const trial = [...cur];
          trial[slot] = cand;
          if (!meetsSp(trial) || !hasRequired(trial)) continue;
          const t = evalTotal(trial, rentalIdx);
          if (t > curTotal + 1e-6) { cur = trial; curTotal = t; improved = true; }
        }
      }
    }
    return { cards: cur, total: curTotal };
  };

  // 全パターンのデッキを種に大域最良を探索
  let best: { cards: SupportCard[]; rentalIdx: number; total: number } | null = null;
  for (const r of results) {
    const cards = r.selected_cards.map((cs) => cs.card);
    const rentalIdx = r.selected_cards.findIndex((cs) => cs.is_rental);
    const hc = hillClimb(cards, rentalIdx);
    if (best == null || hc.total > best.total) best = { cards: hc.cards, rentalIdx, total: hc.total };
  }
  if (best == null) return;

  const free = results[freeIdx];
  const freeRentalIdx = free.selected_cards.findIndex((cs) => cs.is_rental);
  const freeTotal = evalTotal(free.selected_cards.map((cs) => cs.card), freeRentalIdx);
  if (best.total <= freeTotal + 1e-6) return; // 改善なし

  // 大域最良デッキを DeckResult 化 (selectOptimalDeckOnce の確定処理と同一手順)
  const rentalId = best.rentalIdx >= 0 ? best.cards[best.rentalIdx].id : null;
  const ucEff: Record<string, number> = { ...(uncapLevels ?? {}) };
  if (rentalId != null) ucEff[rentalId] = 4;
  const selected: CardScore[] = best.cards.map((card) => {
    const cs = calculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, ucEff, triggerBonusInfo);
    return { ...cs, is_rental: card.id === rentalId, is_required: requiredSet.has(card.id) };
  });

  // hillClimb はレンタル枠の位置を固定したままカードを入れ替えるため、最終デッキで
  // レンタルが最低凸カードに乗っていないことがある。借用先を実計算で最適化する
  // (同点時は最低凸カードを優先。selectOptimalDeck の確定処理と挙動を揃える)。
  if (rentalPool != null) {
    optimizeRentalAssignment(
      selected,
      new Set(ownedCards.map((c) => c.id)),
      plan,
      turnChoices,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
      additionalCounts,
      statCap,
      character,
      memoryBonuses,
      overflowPenalty,
    );
  }

  const adjustedCounts = recomputeBreakdownsDeckAware(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels);
  recalculateWithCap(selected, baseStats, statCap);
  selected.sort((a, b) => b.total_value - a.total_value);
  results[freeIdx] = {
    label: free.label,
    selected_cards: selected,
    total_value: selected.reduce((s, c) => s + c.total_value, 0),
    ability_summary: buildAbilitySummary(selected, adjustedCounts, uncapLevels),
  };
}
