import type { SupportCard, TrainingPlan, StatusValues, AdditionalCounts, TurnChoice, Character, MemoryBonus } from '../../types/models';
import type { CardScore } from '../../types/results';
import { calculate } from '../statusCalculation';
import type { OverflowPenaltyConfig } from './types';
import { calculateCardContribution } from './contribution';
import type { TriggerBonusEntry } from './contribution';
import { meetsTypeSlots } from './constraints';

/**
 * postOptimize はレンタル枠 (is_rental) を絶対にスワップしないため、所持カードが
 * postOptimize で入れ替わった後に「レンタル枠が最適でなくなる」ケースを補正できない。
 *
 * 例: レンタル選出時点で お城(Vo) が所持枠を占有 → レンタルに ほっぺた(Vi) が選ばれる。
 * その後 postOptimize が 所持の お城 を 自分と向き合う(Da) に差し替えると お城 が枠から外れるが、
 * レンタルは ほっぺた のまま固定される。本来は お城 をレンタルに据えた方が合計が高い。
 *
 * このパスは postOptimize 後に、実際の計算(calculate)でレンタル枠を再評価し、
 * レンタルプール内の最良カードに差し替える。タイプ枠・SP枚数の制約は維持する。
 */
export function optimizeRentalCard(
  selected: CardScore[],
  rentalPool: SupportCard[] | undefined,
  planType: string | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
  plan: TrainingPlan,
  additionalCounts: AdditionalCounts | undefined,
  statCap: number,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  cardTypeSlots: Record<string, number> | undefined,
  turnChoices: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  if (rentalPool == null) return;
  const rentalIdx = selected.findIndex((cs) => cs.is_rental);
  if (rentalIdx < 0) return;
  const current = selected[rentalIdx];
  if (current.is_required) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'),
    );
  const meetsSpCounts = (cards: SupportCard[]): boolean => {
    if (spCounts == null) return true;
    for (const [stat, need] of Object.entries(spCounts)) {
      if (need <= 0) continue;
      if (cards.filter((c) => coversStat(c, stat)).length < need) return false;
    }
    return true;
  };

  // 評価用: レンタル候補を 4凸 として実際の計算で合計を求める (postOptimize と同一ロジック)
  const evaluateFull = (cards: SupportCard[], rentalCardId: string): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const cs of selected) {
      if (cs.is_rental) uc[cs.card.id] = 4;
    }
    uc[rentalCardId] = 4;
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null)
      .final_status;
    let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const overflow =
        Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (overflow > overflowPenalty.threshold) total -= overflow * 2;
    }
    return total;
  };

  const ownedIds = new Set(
    selected.filter((_, i) => i !== rentalIdx).map((s) => s.card.id),
  );
  let pool = rentalPool.filter((c) => !ownedIds.has(c.id));
  if (planType != null && planType !== '') {
    pool = pool.filter(
      (c) => c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free',
    );
  }

  const currentCards = selected.map((s) => s.card);
  let bestTotal = evaluateFull(currentCards, current.card.id);
  let bestCard: SupportCard | null = null;

  // 全プールに calculate を回すと重いので、素の寄与上位のみ実評価する。
  const rentalUncap: Record<string, number> = {};
  for (const c of pool) rentalUncap[c.id] = 4;
  const ranked = pool
    .map((c) => ({
      card: c,
      score: (() => {
        const cs = calculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, rentalUncap, triggerBonusInfo);
        return cs.raw_vo + cs.raw_da + cs.raw_vi;
      })(),
    }))
    .sort((a, b) => b.score - a.score)
    .slice(0, 40)
    .map((x) => x.card);

  for (const cand of ranked) {
    const testCards = [...currentCards];
    testCards[rentalIdx] = cand;
    if (cardTypeSlots != null && !meetsTypeSlots(testCards, cardTypeSlots)) continue;
    if (!meetsSpCounts(testCards)) continue;
    const total = evaluateFull(testCards, cand.id);
    if (total > bestTotal) {
      bestTotal = total;
      bestCard = cand;
    }
  }

  if (bestCard != null) {
    const cs = calculateCardContribution(
      bestCard,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      { ...(uncapLevels ?? {}), [bestCard.id]: 4 },
      triggerBonusInfo,
    );
    selected[rentalIdx] = { ...cs, is_rental: true, is_required: false };
    protectedIds.delete(current.card.id);
  }
}

/**
 * 所持カードのみ ON では編成6枚 = 所持5枚 + レンタル1枚(4凸借用) が原則で、6枚中必ず1枚を
 * レンタル(借用先)として指定する。ところが必須カード + SP補充で所持枠が6枚埋まると、
 * 「レンタル1枠」選出ブロック (selected.length < 6) が発火せず is_rental が1枚も立たないまま
 * になる (= レンタル枠が消える)。必須枚数を増やすとレンタルが消えるバグはこれが原因。
 *
 * レンタル枠は「デッキ内のどの1枚を4凸として借りるか」の指定にすぎず、借用は必ず total を
 * 増やす(または同値)ので、ここで最低凸カードを暫定レンタルに指定して枠を必ず確保する。
 * 真に最良の借用先への付け替え(別カードへの差し替え含む)は後続の
 * optimizeRentalCard / optimizeRentalAssignment が実計算で行う。
 *
 * - 既にレンタルがあれば何もしない
 * - デッキに未所持カードがあれば本来 requiredRentalCard 経由で指定済みのはず。ここに来る = 全所持
 * - 指定する1枚は4凸に再計算してから is_rental を立てる (raw 寄与を凸数と整合させる)
 */
export function ensureRentalSlot(
  selected: CardScore[],
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
): void {
  if (selected.length === 0) return;
  if (selected.some((cs) => cs.is_rental)) return;

  // 最低凸のカードを借用先に選ぶ (借用恩恵が最大)。凸不明は4凸扱い。
  let target = 0;
  let lowest = Infinity;
  for (let i = 0; i < selected.length; i++) {
    const u = uncapLevels?.[selected[i].card.id] ?? 4;
    if (u < lowest) {
      lowest = u;
      target = i;
    }
  }

  const recomputed = calculateCardContribution(
    selected[target].card,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    { ...(uncapLevels ?? {}), [selected[target].card.id]: 4 },
    triggerBonusInfo,
  );
  selected[target] = {
    ...recomputed,
    is_rental: true,
    is_required: selected[target].is_required,
  };
}

/**
 * レンタル枠は「デッキ内のどの1枚を4凸として借りるか」の指定にすぎない。
 * 所持カードのみ ON では非レンタル5枚は所持凸数で、レンタル1枚は4凸で評価される。
 * カード集合を変えずに「どのカードをレンタル(4凸借用)にするか」だけを最適化する。
 *
 * バグ例: 0凸所持の必須カードが所持枠(0凸)に固定され、4凸所持カードがレンタル枠
 * (4凸借用=upgrade恩恵ゼロ)に入ると、レンタルを低凸カードに付け替えるだけで total が上がる。
 * カード集合は不変なので属性枠・SP枚数・必須はすべて保持される (単調改善・悪化なし)。
 *
 * - デッキに未所持カードがあれば、それは必ずレンタル(所持枠に置けない)→ 付け替え不可で何もしない
 * - 全カード所持なら、各カードをレンタルにした実計算 total を比較し最大の割り当てを採用
 *
 * 注: recomputeBreakdownsDeckAware は producer 不在時に早期 return するため、
 *     付け替えた2枚の raw 寄与はこの関数内で再計算しておく (フラグ変更だけに頼らない)。
 */
export function optimizeRentalAssignment(
  selected: CardScore[],
  ownedIds: Set<string>,
  plan: TrainingPlan,
  turnChoices: TurnChoice[],
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  additionalCounts: AdditionalCounts | undefined,
  statCap: number,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  const rentalIdx = selected.findIndex((cs) => cs.is_rental);
  if (rentalIdx < 0) return; // レンタル枠なし

  // デッキ内の未所持カードは必ずレンタル固定 (所持枠に置けない) → 付け替え不可
  const hasUnowned = selected.some((cs) => !ownedIds.has(cs.card.id));
  if (hasUnowned) return;

  const cards = selected.map((cs) => cs.card);
  const evalWith = (rentalCardId: string): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}), [rentalCardId]: 4 };
    const fs = calculate(
      plan,
      cards,
      turnChoices,
      uc,
      additionalCounts,
      character ?? null,
      memoryBonuses ?? null,
    ).final_status;
    let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const overflow =
        Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (overflow > overflowPenalty.threshold) total -= overflow * 2;
    }
    return total;
  };

  // 借用先は「合計が最大」かつ、同点なら「所持凸が最低」のカードを選ぶ。
  // レンタルは4凸借用なので低凸カードほど借用恩恵が大きく、上限張り付き等で合計が
  // 同点になるケースでは、4凸所持カードをレンタルに据える浪費を避けて低凸カードへ寄せる
  // (レンタル枠はデッキ内最低凸の所持カードであるべき、という原則)。
  const uncapOf = (id: string): number => uncapLevels?.[id] ?? 4;
  const currentId = selected[rentalIdx].card.id;
  let bestId = currentId;
  let bestTotal = evalWith(currentId);
  let bestUncap = uncapOf(currentId);
  for (const cs of selected) {
    if (cs.card.id === currentId) continue;
    const t = evalWith(cs.card.id);
    const u = uncapOf(cs.card.id);
    if (t > bestTotal) {
      bestTotal = t;
      bestId = cs.card.id;
      bestUncap = u;
    } else if (t === bestTotal && u < bestUncap) {
      bestId = cs.card.id;
      bestUncap = u;
    }
  }

  if (bestId === currentId) return;

  // 付け替え: レンタル状態が変わる2枚の raw 寄与を新しい凸数で再計算する
  for (let i = 0; i < selected.length; i++) {
    const willBeRental = selected[i].card.id === bestId;
    if (willBeRental === selected[i].is_rental) continue;
    const uc: Record<string, number> = willBeRental
      ? { ...(uncapLevels ?? {}), [selected[i].card.id]: 4 }
      : { ...(uncapLevels ?? {}) };
    const recomputed = calculateCardContribution(
      selected[i].card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uc,
      triggerBonusInfo,
    );
    selected[i] = {
      ...recomputed,
      is_rental: willBeRental,
      is_required: selected[i].is_required,
    };
  }
}

/**
 * 借用アップグレード（レンタル枠のジョイント最適化）。
 *
 * ユーザが低凸(uncap<4)で所持するカードは、所持枠では低凸の弱い寄与しか出ないが、
 * レンタル枠で4凸借用すれば本来の強さを発揮する。一方、4凸所持カードをレンタルに置くのは
 * 借用恩恵ゼロの浪費。既存パスは「所持5枚を固定してレンタルを選ぶ(optimizeRentalCard)」
 * 「デッキ内で借用先を再割当(optimizeRentalAssignment)」しかできず、
 * 「弱い所持カードを1枚落として、デッキ外の低凸所持カードを4凸借用する」ジョイント手を取り逃す。
 *
 * このパスは、デッキ外の低凸所持カードC(4凸寄与上位)を借用枠に投入し、デッキ内の非必須カードVを
 * 1枚落とす手を実計算で評価し、合計が上がる場合のみ採用する(単調改善・悪化なし)。
 * 旧レンタルカード(4凸所持等)は所持枠へ移る。属性枠(cardTypeSlots)・SP枚数・必須は維持する。
 *
 * 例: Vocal2 で 0069(da,4凸所持)がレンタル浪費 → 0069を所持に戻し、弱い4凸カードを1枚落として
 *     0072(vo,1凸所持)を4凸借用する方が合計が高い、というケースを拾う。
 *
 * 借用候補は低凸所持カードに加えて rentalPool 内の未所持カードも含める。旧レンタルが
 * SP要員 (例: 0071 0凸所持を4凸借用) だと optimizeRentalCard の単手入替は SP枚数不足で
 * 全滅するため、「未所持カードを借用し、旧レンタルを所持0凸のSP要員に戻し、弱い1枚を落とす」
 * 複合手はこのパスでしか到達できない。
 */
export function optimizeRentalBorrowUpgrade(
  selected: CardScore[],
  cardContributions: CardScore[],
  ownedIds: Set<string>,
  rentalPool: SupportCard[] | undefined,
  planType: string | undefined,
  plan: TrainingPlan,
  turnChoices: TurnChoice[],
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  additionalCounts: AdditionalCounts | undefined,
  statCap: number,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  cardTypeSlots: Record<string, number> | undefined,
  spCounts: Record<string, number> | undefined,
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  const rentalIdx = selected.findIndex((cs) => cs.is_rental);
  if (rentalIdx < 0) return; // レンタル枠なし
  // デッキに未所持カードがある = それがレンタル固定。借用枠は既に未所持カードが使用中 → 対象外。
  if (selected.some((cs) => !ownedIds.has(cs.card.id))) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'),
    );
  const meetsSp = (cards: SupportCard[]): boolean => {
    if (spCounts == null) return true;
    for (const [stat, need] of Object.entries(spCounts)) {
      if (need <= 0) continue;
      if (cards.filter((c) => coversStat(c, stat)).length < need) return false;
    }
    return true;
  };
  const rawTotal = (cs: CardScore): number => cs.raw_vo + cs.raw_da + cs.raw_vi;

  const realTotal = (cards: SupportCard[], rentalId: string): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}), [rentalId]: 4 };
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null).final_status;
    let t = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const o = Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (o > overflowPenalty.threshold) t -= o * 2;
    }
    return t;
  };

  const at4 = (card: SupportCard): CardScore =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      { ...(uncapLevels ?? {}), [card.id]: 4 },
      triggerBonusInfo,
    );

  const inDeck = new Set(selected.map((s) => s.card.id));
  // 借用候補: デッキ外の (a) 低凸(uncap<4)所持カード + (b) rentalPool 内の未所持カード。
  // 4凸寄与の上位のみ評価しコストを抑える。
  const planOk = (c: SupportCard): boolean =>
    planType == null || planType === '' || c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free';
  const ownedCands = cardContributions
    .filter((cs) => !inDeck.has(cs.card.id) && (uncapLevels?.[cs.card.id] ?? 0) < 4)
    .map((cs) => at4(cs.card));
  const unownedCands = (rentalPool ?? [])
    .filter((c) => !inDeck.has(c.id) && !ownedIds.has(c.id) && planOk(c))
    .map((c) => at4(c));
  const borrowCands = [...ownedCands, ...unownedCands]
    .sort((a, b) => rawTotal(b) - rawTotal(a))
    .slice(0, 12);
  if (borrowCands.length === 0) return;

  const currentCards = selected.map((s) => s.card);
  let bestTotal = realTotal(currentCards, selected[rentalIdx].card.id);
  let bestVi = -1;
  let bestCand: CardScore | null = null;

  for (const cand of borrowCands) {
    for (let vi = 0; vi < selected.length; vi++) {
      if (selected[vi].is_required) continue; // 必須は落とさない
      const trial = currentCards.map((c, i) => (i === vi ? cand.card : c));
      if (cardTypeSlots != null && !meetsTypeSlots(trial, cardTypeSlots)) continue;
      if (!meetsSp(trial)) continue;
      const t = realTotal(trial, cand.card.id);
      if (t > bestTotal) {
        bestTotal = t;
        bestVi = vi;
        bestCand = cand;
      }
    }
  }

  if (bestCand == null || bestVi < 0) return;

  // 採用: bestVi を借用カード(4凸レンタル)に置換。旧レンタル等は所持(所持凸)で再計算。
  for (let i = 0; i < selected.length; i++) {
    if (i === bestVi) {
      selected[i] = { ...bestCand, is_rental: true, is_required: false };
    } else if (selected[i].is_rental) {
      const owned = calculateCardContribution(
        selected[i].card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        uncapLevels ?? {},
        triggerBonusInfo,
      );
      selected[i] = { ...owned, is_rental: false, is_required: selected[i].is_required };
    }
  }
}

