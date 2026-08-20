import type { SupportCard, TrainingPlan, StatusValues, AdditionalCounts, TurnChoice, Character, MemoryBonus } from '../../types/models';
import type { CardScore } from '../../types/results';
import { calculate } from '../statusCalculation';
import { DEFAULT_STAT_CAP } from '../../utils/constants';
import type { OverflowPenaltyConfig } from './types';
import type { TriggerBonusEntry } from './contribution';
import { meetsTypeSlots } from './constraints';
import { optimizeRentalCard } from './rental';
import { buildTurnChoices } from './results';

// --- Post-optimization using actual calculation ---
/**
 * 局所最適の修復: postOptimize(所持カードのみ・レンタル固定) と optimizeRentalCard
 * (レンタルのみ・所持固定) は別々に最適化するため、「所持カードの差し替え」と
 * 「レンタルの差し替え」を“同時”に行わないと届かない最適解を取り逃す。
 * (例: 0023→ふわふわ と レンタル 0027→0069 を同時にやると合計が上がるが、片方ずつでは上がらない)
 *
 * このパスは、有望な未編成カード(SP率 or trigger_count_bonus producer)を1枚強制投入し、
 * その状態でレンタルを再最適化して実計算で評価する。合計が上がる場合のみ採用するため、
 * 結果が悪化することはない (単調改善)。
 */
export function jointSwapRepair(
  selected: CardScore[],
  cardContributions: CardScore[],
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
  rentalPool: SupportCard[] | undefined,
  planType: string | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  plan: TrainingPlan,
  additionalCounts: AdditionalCounts | undefined,
  statCap: number,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  cardTypeSlots: Record<string, number> | undefined,
  turnChoices: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  const hasSpRate = (card: SupportCard) =>
    card.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate');
  const spStat = (card: SupportCard): string | undefined =>
    card.effects.find((e) => e.trigger === 'equip' && e.value_type === 'sp_rate')?.stat;
  const isProducer = (card: SupportCard) =>
    card.effects.some((e) => e.value_type === 'trigger_count_bonus' && e.trigger_target);
  const coversStat = (card: SupportCard, stat: string) =>
    card.effects.some(
      (e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'),
    );
  const rawTotal = (cs: CardScore) => cs.raw_vo + cs.raw_da + cs.raw_vi;

  const evalReal = (cards: SupportCard[], rentalIds: Set<string>): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const id of rentalIds) uc[id] = 4;
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null).final_status;
    let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const overflow = Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (overflow > overflowPenalty.threshold) total -= overflow * 2;
    }
    return total;
  };

  const meetsSp = (cards: SupportCard[]): boolean => {
    if (spCounts == null) return true;
    for (const [stat, need] of Object.entries(spCounts)) {
      if (need <= 0) continue;
      if (cards.filter((c) => coversStat(c, stat)).length < need) return false;
    }
    return true;
  };

  let improved = true;
  let guard = 0;
  while (improved && guard++ < 3) {
    improved = false;
    const rentalIdsNow = new Set(selected.filter((s) => s.is_rental).map((s) => s.card.id));
    const baseTotal = evalReal(selected.map((s) => s.card), rentalIdsNow);
    const inDeck = new Set(selected.map((s) => s.card.id));

    const promising = cardContributions
      .filter((c) => !inDeck.has(c.card.id) && (hasSpRate(c.card) || isProducer(c.card)))
      .sort((a, b) => rawTotal(b) - rawTotal(a))
      .slice(0, 8);

    for (const cand of promising) {
      // 投入先スロットを選ぶ
      const candSp = hasSpRate(cand.card) ? spStat(cand.card) : undefined;
      let slotIdx = -1;
      let weakest = Infinity;
      if (candSp != null) {
        // 同属性SPの保護枠のうち最弱を置換 → SP枚数を維持
        for (let i = 0; i < selected.length; i++) {
          const s = selected[i];
          if (s.is_rental || s.is_required) continue;
          if (protectedIds.has(s.card.id) && hasSpRate(s.card) && spStat(s.card) === candSp) {
            const r = rawTotal(s);
            if (r < weakest) { weakest = r; slotIdx = i; }
          }
        }
      }
      if (slotIdx < 0) {
        // 非保護の最弱枠を置換
        weakest = Infinity;
        for (let i = 0; i < selected.length; i++) {
          const s = selected[i];
          if (s.is_rental || s.is_required || protectedIds.has(s.card.id)) continue;
          const r = rawTotal(s);
          if (r < weakest) { weakest = r; slotIdx = i; }
        }
      }
      if (slotIdx < 0) continue;

      const victim = selected[slotIdx];
      const trial = [...selected];
      trial[slotIdx] = cand;
      const trialProtected = new Set(protectedIds);
      if (protectedIds.has(victim.card.id)) trialProtected.delete(victim.card.id);
      if (candSp != null) trialProtected.add(cand.card.id);

      if (cardTypeSlots != null && !meetsTypeSlots(trial.map((s) => s.card), cardTypeSlots)) continue;
      if (!meetsSp(trial.map((s) => s.card))) continue;

      // 投入した状態でレンタルを再最適化 (同時手)
      optimizeRentalCard(
        trial, rentalPool, planType, triggerCounts, lessonAllocation, lessonStatTotals,
        uncapLevels, triggerBonusInfo, trialProtected, spCounts, plan, additionalCounts,
        statCap, character, memoryBonuses, cardTypeSlots, turnChoices, overflowPenalty,
      );

      const trialRentalIds = new Set(trial.filter((s) => s.is_rental).map((s) => s.card.id));
      const trialTotal = evalReal(trial.map((s) => s.card), trialRentalIds);

      if (trialTotal > baseTotal) {
        selected.splice(0, selected.length, ...trial);
        protectedIds.clear();
        for (const id of trialProtected) protectedIds.add(id);
        improved = true;
        break;
      }
    }
  }
}

export function postOptimize(
  selected: CardScore[],
  candidates: CardScore[],
  protectedIds: Set<string>,
  plan: TrainingPlan,
  mainStats: string[],
  uncapLevels?: Record<string, number>,
  additionalCounts?: AdditionalCounts,
  statCap?: number,
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  cardTypeSlots?: Record<string, number>,
  turnChoicesOverride?: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  // HIFモードのようにユーザが明示的にターン選択している場合は、合成 turnChoices ではなく
  // 実選択を使って評価する。これをやらないと postOptimize の評価が実際のデッキ計算と食い違う。
  const turnChoices = turnChoicesOverride ?? buildTurnChoices(plan, mainStats);
  const cap = statCap ?? plan.status_limit ?? DEFAULT_STAT_CAP;

  function evaluateFull(cards: SupportCard[]): { total: number; vo: number; da: number; vi: number } {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const cs of selected) {
      if (cs.is_rental) uc[cs.card.id] = 4;
    }
    // 最終表示値と一致させるため、キャラ補正・メモリーボーナスを含めて評価する
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null)
      .final_status;
    const cappedVo = Math.min(fs.vo, cap);
    const cappedDa = Math.min(fs.da, cap);
    const cappedVi = Math.min(fs.vi, cap);
    let total = cappedVo + cappedDa + cappedVi;
    // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則を適用
    if (overflowPenalty) {
      const overflow = Math.max(0, fs.vo - cap) + Math.max(0, fs.da - cap) + Math.max(0, fs.vi - cap);
      if (overflow > overflowPenalty.threshold) {
        total -= overflow * 2;
      }
    }
    return { total, vo: fs.vo, da: fs.da, vi: fs.vi };
  }

  let improved: boolean;
  do {
    improved = false;
    const currentCards = selected.map((c) => c.card);
    let currentEval = evaluateFull(currentCards);

    for (let si = 0; si < selected.length; si++) {
      const ownedCard = selected[si];
      if (ownedCard.is_rental) continue;
      // 必須カードは無条件でスワップ不可
      if (ownedCard.is_required) continue;

      const hasSpRate = (card: SupportCard) =>
        card.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate');
      const getSpRateStat = (card: SupportCard): string | undefined =>
        card.effects.find((e) => e.trigger === 'equip' && e.value_type === 'sp_rate')?.stat;
      const ownedIsProtectedSp =
        protectedIds.has(ownedCard.card.id) && hasSpRate(ownedCard.card);
      const ownedIsProtectedNonSp =
        protectedIds.has(ownedCard.card.id) && !ownedIsProtectedSp;
      // 非SPの保護カードはスキップ
      if (ownedIsProtectedNonSp) continue;

      const ownedType = ownedCard.card.type;
      const ownedSpStat = ownedIsProtectedSp ? getSpRateStat(ownedCard.card) : undefined;

      for (const candidate of candidates) {
        if (selected.some((c) => c.card.id === candidate.card.id)) continue;

        // SP率で保護されたカードは、同じ属性のSP率を持つ候補とのみ交換可能
        // (ユーザ指定の spCounts 分布を postOptimize で崩さないため)
        if (ownedIsProtectedSp) {
          const candStat = getSpRateStat(candidate.card);
          if (candStat == null || candStat !== ownedSpStat) continue;
        }

        const testCards = [...currentCards];
        testCards[si] = candidate.card;

        // タイプ制約: cardTypeSlots の最低要件 (例: Da 2枚以上) を満たすスワップのみ許可
        if (
          candidate.card.type !== ownedType &&
          candidate.card.type !== 'all' &&
          candidate.card.type !== 'as' &&
          ownedType !== 'all' &&
          ownedType !== 'as' &&
          cardTypeSlots != null &&
          !meetsTypeSlots(testCards, cardTypeSlots)
        ) {
          continue;
        }

        const testEval = evaluateFull(testCards);
        // 合計値が同点の場合、raw_total (キャップ前の素の寄与) が大きいカードを優先。
        // 両方がキャップを張り付かせる場合に「より強いSSR」を採用するためのタイブレーカ。
        const candRawTotal = candidate.raw_vo + candidate.raw_da + candidate.raw_vi;
        const ownedRawTotal = ownedCard.raw_vo + ownedCard.raw_da + ownedCard.raw_vi;
        const isImprovement =
          testEval.total > currentEval.total ||
          (testEval.total === currentEval.total && candRawTotal > ownedRawTotal);
        if (isImprovement) {
          selected[si] = candidate;
          // SP率保護を新カードに引き継ぐ
          if (ownedIsProtectedSp) {
            protectedIds.delete(ownedCard.card.id);
            protectedIds.add(candidate.card.id);
          }
          currentEval = testEval;
          improved = true;
          break;
        }
      }
      if (improved) break;
    }
  } while (improved);
}

