import type { SupportCard, TrainingPlan, AdditionalCounts, TurnChoice, Character, MemoryBonus } from '../../types/models';
import { additionalCountsToRecord } from '../../types/models';
import type { CardScore, DeckResult } from '../../types/results';
import { calculate } from '../statusCalculation';
import type { OverflowPenaltyConfig } from './types';
import { calculateCardContribution, computeTriggerBonusInfo, countTriggers, estimateBaseStats, calculateLessonStatTotals } from './contribution';
import { greedyFillOwned } from './greedy';
import { enforceSpCounts, enforceTypeSlots, unprotectExcessSpCards } from './constraints';
import { ensureRentalSlot, optimizeRentalAssignment, optimizeRentalBorrowUpgrade, optimizeRentalCard } from './rental';
import { jointSwapRepair, postOptimize } from './postOptimize';
import { buildAbilitySummary, buildTurnChoices, calculateCappedTotal, recalculateWithCap, recomputeBreakdownsDeckAware, selectBestCard, generateLabel } from './results';

// --- Select optimal deck ---

/**
 * カード探索順 (候補プールの順序) に依存して別の局所最適へ落ちる貪欲法を補正するため、
 * カードデータ由来の複数順序で選出を試し、実 calculate の cap 後合計が最大の編成を採用する
 * マルチスタートのラッパ。
 *
 * - 順序はカードデータ (id / レアリティ) のみから決まるので、呼び出し側の読込順に**非依存**。
 *   → デスクトップ版とWeb版でカード読込順が違っても同一結果になる (実装間の乖離を解消)。
 * - 各候補を実 calculate で採点して最大を採るので、単一スタートより悪化しない (単調改善)。
 */
export function selectOptimalDeck(
  plan: TrainingPlan,
  allCards: SupportCard[],
  lessonAllocation: Record<string, number>,
  cardTypeSlots: Record<string, number>,
  mainStats: string[],
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  freeSlots: number = 0,
  requiredCardIds?: string[],
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  turnChoicesOverride?: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): DeckResult {
  const statCap = plan.status_limit;
  // レンタルプールも順序依存を排除するため id 昇順に正規化 (全スタート共通)
  const canonicalRental = rentalPool != null
    ? [...rentalPool].sort(compareById)
    : undefined;

  let best: DeckResult | null = null;
  let bestScore = -Infinity;
  for (const pool of candidateOrderings(allCards)) {
    const result = selectOptimalDeckOnce(
      plan, pool, lessonAllocation, cardTypeSlots, mainStats,
      spCounts, planType, additionalCounts, uncapLevels, canonicalRental,
      freeSlots, requiredCardIds, character, memoryBonuses,
      turnChoicesOverride, overflowPenalty,
    );
    const score = evalDeckScore(
      result, plan, mainStats, uncapLevels, additionalCounts,
      character, memoryBonuses, statCap, turnChoicesOverride, overflowPenalty,
    );
    if (score > bestScore) {
      bestScore = score;
      best = result;
    }
  }
  return best!;
}

/** id 昇順比較 (ordinal)。 */
export function compareById(a: SupportCard, b: SupportCard): number {
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/**
 * マルチスタート用の候補プール順序集合。すべてカードデータのみから決まる (入力順非依存)。
 * 貪欲法の出発点を散らして別々の局所最適を探索する。
 */
export function candidateOrderings(cards: SupportCard[]): SupportCard[][] {
  const rarityRank = (r: string): number => (r === 'ssr' ? 0 : r === 'sr' ? 1 : 2);
  const asc = [...cards].sort(compareById);
  const desc = [...cards].sort((a, b) => -compareById(a, b));
  const byRarity = [...cards].sort(
    (a, b) => rarityRank(a.rarity) - rarityRank(b.rarity) || compareById(a, b),
  );
  // すべてカードデータのみから決まる順序 (入力順非依存)。貪欲法の出発点を散らして
  // 別々の局所最適を探索し、実 calculate 採点で最良を採る (単調改善・悪化なし)。
  return [asc, desc, byRarity];
}

/**
 * 確定デッキを実 calculate で採点し cap 後合計を返す (overflow罰則込み)。
 * postOptimize の evaluateFull と同一基準。マルチスタートの優劣比較に使う。
 */
export function evalDeckScore(
  result: DeckResult,
  plan: TrainingPlan,
  mainStats: string[],
  uncapLevels: Record<string, number> | undefined,
  additionalCounts: AdditionalCounts | undefined,
  character: Character | null | undefined,
  memoryBonuses: MemoryBonus[] | null | undefined,
  statCap: number,
  turnChoicesOverride: TurnChoice[] | undefined,
  overflowPenalty: OverflowPenaltyConfig | undefined,
): number {
  const turnChoices = turnChoicesOverride ?? buildTurnChoices(plan, mainStats);
  const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
  for (const cs of result.selected_cards) {
    if (cs.is_rental) uc[cs.card.id] = 4;
  }
  const cards = result.selected_cards.map((cs) => cs.card);
  const fs = calculate(
    plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null,
  ).final_status;
  let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
  if (overflowPenalty) {
    const overflow = Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
    if (overflow > overflowPenalty.threshold) total -= overflow * 2;
  }
  return total;
}

export function selectOptimalDeckOnce(
  plan: TrainingPlan,
  allCards: SupportCard[],
  lessonAllocation: Record<string, number>,
  cardTypeSlots: Record<string, number>,
  mainStats: string[],
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  freeSlots: number = 0,
  requiredCardIds?: string[],
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  /**
   * HIFモードなど、ユーザが明示的にターン選択を指定している場合の override。
   * 渡された場合 postOptimize の評価でもこの選択を使う (デフォルトの buildTurnChoices ではなく)
   */
  turnChoicesOverride?: TurnChoice[],
  /**
   * HIFモードでMAX大幅超過時のみ再抽選を促す optional オプション。
   * 渡された場合、selectBestCard / postOptimize の評価で × 2 overflow罰則が条件付きで適用される。
   */
  overflowPenalty?: OverflowPenaltyConfig,
): DeckResult {
  const statCap = plan.status_limit;
  const triggerCounts = countTriggers(plan, lessonAllocation, mainStats, turnChoicesOverride);

  if (additionalCounts != null) {
    const addRec = additionalCountsToRecord(additionalCounts);
    for (const [key, value] of Object.entries(addRec)) {
      if (value > 0) {
        triggerCounts[key] = (triggerCounts[key] ?? 0) + value;
      }
    }
  }

  // 育成タイプでフィルタ
  let eligible = allCards;
  if (planType != null && planType !== '') {
    eligible = allCards.filter(
      (c) =>
        c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free',
    );
  }

  // レッスン・イベント等のカード無しベースステータスを推定
  const baseStats = estimateBaseStats(plan, lessonAllocation, turnChoicesOverride);

  // レッスンの属性別合計SpBonusを事前計算
  const lessonStatTotals = calculateLessonStatTotals(plan, lessonAllocation, turnChoicesOverride);

  // trigger_count_bonus 効果 (Pアイテム由来でドリンク等を追加生成する効果) の単体スコアリング用情報
  // 「もしこのカードが選ばれた場合、追加で発火する trigger_target は他カードに何ポイント寄与するか」
  // を見積もるため、対象トリガーを持つ消費側カードの per-fire 値を集計しておく
  const triggerBonusInfo = computeTriggerBonusInfo(eligible, uncapLevels);

  // 全カードの属性別寄与を事前計算
  const cardContributions = eligible.map((card) =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    ),
  );

  // 全カードプール (フィルタ外も補充用に)
  const allContributions = allCards.map((card) =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    ),
  );

  // 属性枠ごとに選択 (上限考慮)
  let selected: CardScore[] = [];
  let usedIds = new Set<string>();

  // 現在の累積ステータス (ベース + 選択済みカード)
  // キャラ補正を含めることで、cap-aware なカード選出が character の偏りを反映できるようにする
  let accVo = baseStats.vo,
    accDa = baseStats.da,
    accVi = baseStats.vi;
  if (character != null) {
    accVo += character.base_status_bonus.vo;
    accDa += character.base_status_bonus.da;
    accVi += character.base_status_bonus.vi;
    // para_bonus はレッスン上昇値に対する%補正 (近似)
    accVo += lessonStatTotals.vo * (character.para_bonus.vo / 100);
    accDa += lessonStatTotals.da * (character.para_bonus.da / 100);
    accVi += lessonStatTotals.vi * (character.para_bonus.vi / 100);
  }
  // キャラの para_bonus はカード貢献にも乗る。accumulator 更新でも同じ倍率を適用する
  const accVoMul = 1 + (character?.para_bonus.vo ?? 0) / 100;
  const accDaMul = 1 + (character?.para_bonus.da ?? 0) / 100;
  const accViMul = 1 + (character?.para_bonus.vi ?? 0) / 100;

  // 属性枠・フリー枠の残数を管理するローカルコピー
  const remainingSlots: Record<string, number> = { ...cardTypeSlots };
  let remainingFree = freeSlots;

  // ステップ0: 必須カードを強制挿入
  let requiredRentalCard: CardScore | undefined = undefined;
  const protectedIds = new Set<string>();

  // ステップ1のSP率先取り用に「必須カードで消費した分を減算した」残り必要枚数。
  // unprotectExcessSpCards / enforceSpCounts では必須カードを含む元の spCounts(総数)で
  // 判定する必要があるため、減算後のカウントはこのローカル変数にのみ反映し、
  // spCounts 自体は上書きしない (上書きすると SP枚数の最終保証が必須カード分だけ過小評価される)。
  const spCountsForFill: Record<string, number> = spCounts != null ? { ...spCounts } : {};

  if (requiredCardIds != null && requiredCardIds.length > 0) {

    for (const cardId of requiredCardIds) {
      // allCards から探す、見つからなければ rentalPool からも探す
      const card = allCards.find((c) => c.id === cardId)
        ?? rentalPool?.find((c) => c.id === cardId);
      if (card == null || usedIds.has(cardId)) continue;

      // 所持判定: rentalPool が null なら全カード所持扱い、そうでなければ eligible に含まれるか
      const isOwned = rentalPool == null || eligible.some((c) => c.id === cardId);

      // 凸数: 所持なら uncapLevels、未所持なら4凸
      const reqUncap: Record<string, number> = { ...(uncapLevels ?? {}) };
      if (!isOwned) {
        reqUncap[cardId] = 4;
      } else if (!(cardId in reqUncap)) {
        reqUncap[cardId] = 4;
      }

      const contribution = calculateCardContribution(
        card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        reqUncap,
        triggerBonusInfo,
      );
      contribution.is_required = true;

      if (!isOwned && rentalPool != null) {
        // 未所持 → レンタル枠として保留（selected に入れない）
        contribution.is_rental = true;
        requiredRentalCard = contribution;
        usedIds.add(cardId);
        protectedIds.add(cardId);

        // レンタル借用する必須カードも SP率を持つならデッキの SP要求を満たすため、
        // 所持枠向けの先取り枚数 (spCountsForFill) から減算する。減算しないとステップ1が
        // SPカードを過剰確保して所持枠が6枚に達し、「レンタル1枠」ブロックが発火せず
        // 必須レンタルカードが編成から漏れる (2026-08 ユーザ報告)。
        const rentalSpEffect = card.effects.find(
          (e) => e.trigger === 'equip' && e.value_type === 'sp_rate',
        );
        if (rentalSpEffect != null) {
          for (const key of Object.keys(spCountsForFill)) {
            if (
              (card.type === key || card.type === 'all' || card.type === 'as') &&
              spCountsForFill[key] > 0
            ) {
              spCountsForFill[key]--;
            }
          }
        }
      } else {
        // 所持 → 所持枠として追加
        selected.push(contribution);
        usedIds.add(cardId);
        protectedIds.add(cardId);
        accVo += contribution.raw_vo * accVoMul;
        accDa += contribution.raw_da * accDaMul;
        accVi += contribution.raw_vi * accViMul;

        // スロット消費 ("as" は "all" と同等に扱う)
        const isAllLike = card.type === 'all' || card.type === 'as';
        if (!isAllLike && card.type in remainingSlots && remainingSlots[card.type] > 0) {
          remainingSlots[card.type]--;
        } else if (isAllLike) {
          // "all"/"as" タイプ: 最大残数の属性枠を消費
          const maxSlotKey = Object.entries(remainingSlots)
            .sort((a, b) => b[1] - a[1])[0];
          if (maxSlotKey && maxSlotKey[1] > 0) {
            remainingSlots[maxSlotKey[0]]--;
          } else {
            remainingFree = Math.max(0, remainingFree - 1);
          }
        } else {
          remainingFree = Math.max(0, remainingFree - 1);
        }

        // SP率カード判定: 必須カードがSP率エフェクトを持つなら spCounts を減算。
        // all/as 型のSP率カードは全属性のSP発生率を上げる = da/vi 両方の必要数を1本ずつ満たす。
        // ここで break すると1属性しか減算されず、ステップ1が残り属性のSPを過剰確保して
        // 所持枠が膨張し、デッキが6枚を超える (必須+SP補充で7枚になるバグの原因)。
        // → カバーする全属性を減算する (単一型は自属性のみ一致するので二重減算しない)。
        const spEffect = card.effects.find(
          (e) => e.trigger === 'equip' && e.value_type === 'sp_rate',
        );
        if (spEffect != null) {
          for (const key of Object.keys(spCountsForFill)) {
            if (
              (card.type === key || card.type === 'all' || card.type === 'as') &&
              spCountsForFill[key] > 0
            ) {
              spCountsForFill[key]--;
            }
          }
        }
      }
    }
  }

  // ステップ1: SP率カードをユーザ指定枚数分、先に確保
  const spCardSlotStat: Record<string, string> = {}; // cardId -> 消費したスロットのstat key
  const spCardUsedFree = new Set<string>(); // フリー枠を消費したcardId
  if (spCounts != null) {
    // 必須カードで消費済みの分を差し引いた残り枚数のみ先取りする
    for (const [stat, need] of Object.entries(spCountsForFill)) {
      if (need <= 0) continue;

      // この属性のSP率を持つカードをステータス寄与順で選ぶ ("as" は "all" と同等)
      const spCandidates = cardContributions.filter(
        (cs) =>
          (cs.card.type === stat || cs.card.type === 'all' || cs.card.type === 'as') &&
          !usedIds.has(cs.card.id) &&
          cs.card.effects.some(
            (e) => e.trigger === 'equip' && e.value_type === 'sp_rate',
          ),
      );

      for (let i = 0; i < need; i++) {
        const best = selectBestCard(
          spCandidates,
          usedIds,
          accVo,
          accDa,
          accVi,
          statCap,
          character,
          overflowPenalty,
        );
        if (best == null) break;

        selected.push(best);
        usedIds.add(best.card.id);
        protectedIds.add(best.card.id); // SP率カードはポスト最適化でスワップしない
        accVo += best.raw_vo * accVoMul;
        accDa += best.raw_da * accDaMul;
        accVi += best.raw_vi * accViMul;

        // SP率カードが属性枠にカウントされるか、フリー枠を消費するか判定
        if (stat in remainingSlots && remainingSlots[stat] > 0) {
          spCardSlotStat[best.card.id] = stat;
          remainingSlots[stat]--;
        } else {
          spCardUsedFree.add(best.card.id);
          remainingFree = Math.max(0, remainingFree - 1);
        }
      }
    }
  }

  // レンタルモード: 所持5枠 + レンタル1枠
  const ownedSlots = rentalPool != null ? 5 : 6;

  // チェックポイント保存（レンタルパターンC用）
  const checkpointSelected = [...selected];
  const checkpointUsedIds = new Set(usedIds);
  const checkpointAccVo = accVo,
    checkpointAccDa = accDa,
    checkpointAccVi = accVi;
  const checkpointRemainingSlots = { ...remainingSlots };
  const checkpointRemainingFree = remainingFree;

  // ステップ2: グリーディに所持枠を埋める
  // レンタル必須カードがある場合はそのステータスを事前加算して補完的なカードを選ぶ
  {
    const fillAccVo = accVo + (requiredRentalCard?.raw_vo ?? 0);
    const fillAccDa = accDa + (requiredRentalCard?.raw_da ?? 0);
    const fillAccVi = accVi + (requiredRentalCard?.raw_vi ?? 0);
    const fill = greedyFillOwned(
      cardContributions,
      selected,
      usedIds,
      fillAccVo,
      fillAccDa,
      fillAccVi,
      remainingSlots,
      remainingFree,
      ownedSlots,
      statCap,
      character,
      overflowPenalty,
    );
    selected = fill.selected;
    usedIds = fill.usedIds;
    // 事前加算分を差し引いて実際の累積ステータスを得る
    accVo = fill.accVo - (requiredRentalCard?.raw_vo ?? 0);
    accDa = fill.accDa - (requiredRentalCard?.raw_da ?? 0);
    accVi = fill.accVi - (requiredRentalCard?.raw_vi ?? 0);
  }

  // 必須レンタルカードは所持枠が6枚埋まっていても最優先で投入する (必須 > SP枚数 > パターン)。
  // 何らかの経路で所持枠が埋まりきった場合は、最弱の非必須カード (非保護優先) を1枚落として
  // 必ずレンタル枠を空ける。落とした分の SP/属性枠は後続の enforceSpCounts / enforceTypeSlots が修復する。
  if (rentalPool != null && requiredRentalCard != null && selected.length >= 6) {
    let victimIdx = -1;
    let victimKey = Infinity;
    for (let i = 0; i < selected.length; i++) {
      const s = selected[i];
      if (s.is_required) continue;
      const key =
        (protectedIds.has(s.card.id) ? Number.MAX_SAFE_INTEGER / 2 : 0) +
        s.raw_vo + s.raw_da + s.raw_vi;
      if (key < victimKey) {
        victimKey = key;
        victimIdx = i;
      }
    }
    if (victimIdx >= 0) {
      const victim = selected[victimIdx];
      selected.splice(victimIdx, 1);
      usedIds.delete(victim.card.id);
      protectedIds.delete(victim.card.id);
      accVo -= victim.raw_vo * accVoMul;
      accDa -= victim.raw_da * accDaMul;
      accVi -= victim.raw_vi * accViMul;
    }
  }

  // レンタル1枠: 全カードプールから4凸で最良の1枚を選択
  if (rentalPool != null && selected.length < 6) {
    if (requiredRentalCard != null) {
      // 必須カードがレンタル枠を使用 → Pattern A/B をスキップ
      selected.push(requiredRentalCard);
      usedIds.add(requiredRentalCard.card.id);
      accVo += requiredRentalCard.raw_vo * accVoMul;
      accDa += requiredRentalCard.raw_da * accDaMul;
      accVi += requiredRentalCard.raw_vi * accViMul;
    } else {
    const rentalUncap: Record<string, number> = {};
    for (const c of rentalPool) {
      rentalUncap[c.id] = 4;
    }

    // レンタル候補: 所持で選ばれたカードも含めて全カードから計算
    const filteredRentalPool =
      planType != null && planType !== ''
        ? rentalPool.filter(
            (c) =>
              c.plan == null ||
              c.plan === '' ||
              c.plan === planType ||
              c.plan === 'free',
          )
        : rentalPool;

    // ユーザが4凸所持のカードはレンタル枠に置いても upgrade 恩恵がゼロ
    // (owned 4凸 = rental 4凸 で同値)。レンタル枠は本来「未所持/低凸カードを4凸として
    // 借りる」用途なので、4凸所持カードを意図的に rental に置くのは枠の浪費。→ 除外。
    // ただし全候補が4凸所持で空になる場合はフォールバックで除外しない。
    // 注意: uncapLevels は未所持カードにもエントリを持つ (inventory は全カードを
    // デフォルト uncap=4 で保存する) ため、uncap だけで判定すると未所持カード全てを
    // 「4凸所持」と誤判定しレンタル候補から除外してしまう。所持集合との積で判定する。
    const ownedIdSet = new Set(allCards.map((c) => c.id));
    const isUserOwned4Star = (cardId: string): boolean =>
      ownedIdSet.has(cardId) && (uncapLevels?.[cardId] ?? 0) >= 4;
    const rentalPoolForCandidates = (() => {
      const filtered = filteredRentalPool.filter((c) => !isUserOwned4Star(c.id));
      return filtered.length > 0 ? filtered : filteredRentalPool;
    })();

    const allRentalContributions = new Map<string, CardScore>();
    for (const card of rentalPoolForCandidates) {
      const cs = calculateCardContribution(
        card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        rentalUncap,
        triggerBonusInfo,
      );
      allRentalContributions.set(cs.card.id, cs);
    }

    // パターンA: 従来通り、未使用カードからレンタルを選択
    const unusedRentalCandidates = [...allRentalContributions.values()].filter(
      (cs) => !usedIds.has(cs.card.id),
    );
    const defaultRental = selectBestCard(
      unusedRentalCandidates,
      usedIds,
      accVo,
      accDa,
      accVi,
      statCap,
      character,
      overflowPenalty,
    );
    const defaultTotal = calculateCappedTotal(
      baseStats,
      selected,
      defaultRental,
      statCap,
    );

    // 最良の結果を追跡
    let bestOverallTotal = defaultTotal;
    let bestOverallRental: CardScore | undefined = defaultRental;
    let bestOverallSelected: CardScore[] | undefined = undefined;

    // パターンB: 所持カードXをレンタルX(4凸)に昇格し、空いた所持枠に代替カードを入れる
    for (const ownedCard of selected) {
      if (ownedCard.is_required) continue;

      const rentalVersion = allRentalContributions.get(ownedCard.card.id);
      if (rentalVersion == null) continue;

      const rentalGain =
        rentalVersion.raw_vo + rentalVersion.raw_da + rentalVersion.raw_vi;
      const ownedGain =
        ownedCard.raw_vo + ownedCard.raw_da + ownedCard.raw_vi;
      if (rentalGain <= ownedGain) continue;

      const swapAccVo = accVo - ownedCard.raw_vo * accVoMul;
      const swapAccDa = accDa - ownedCard.raw_da * accDaMul;
      const swapAccVi = accVi - ownedCard.raw_vi * accViMul;

      const swapUsedIds = new Set<string>(usedIds);
      const replacementCandidates = cardContributions.filter(
        (cs) => !swapUsedIds.has(cs.card.id),
      );
      const replacement = selectBestCard(
        replacementCandidates,
        swapUsedIds,
        swapAccVo,
        swapAccDa,
        swapAccVi,
        statCap,
        character,
        overflowPenalty,
      );

      if (replacement == null) continue;

      const swapSelected = selected
        .filter((s) => s.card.id !== ownedCard.card.id)
        .concat([replacement]);
      const swapTotal = calculateCappedTotal(
        baseStats,
        swapSelected,
        rentalVersion,
        statCap,
      );

      if (swapTotal > bestOverallTotal) {
        bestOverallTotal = swapTotal;
        bestOverallRental = rentalVersion;
        bestOverallSelected = swapSelected;
      }
    }

    // パターンC: 各レンタル候補に対して所持カードを最適に再選択
    // レンタルのステータスを事前加算し、補完的な所持カードが選ばれるようにする
    for (const rentalCandidate of allRentalContributions.values()) {
      // 必須カードのみスキップ（SP保護カードは許可）
      const existingOwned = checkpointSelected.find(
        (cs) => cs.card.id === rentalCandidate.card.id,
      );
      if (existingOwned?.is_required) continue;

      // チェックポイントに含まれるカード（SP保護等）→除外してスロット復元
      let localSelected = checkpointSelected;
      let localAccVo = checkpointAccVo;
      let localAccDa = checkpointAccDa;
      let localAccVi = checkpointAccVi;
      let localRemainingSlots = checkpointRemainingSlots;
      let localRemainingFree = checkpointRemainingFree;

      if (existingOwned != null) {
        localSelected = checkpointSelected.filter(
          (cs) => cs.card.id !== rentalCandidate.card.id,
        );
        localAccVo -= existingOwned.raw_vo;
        localAccDa -= existingOwned.raw_da;
        localAccVi -= existingOwned.raw_vi;
        localRemainingSlots = { ...checkpointRemainingSlots };
        if (existingOwned.card.id in spCardSlotStat) {
          localRemainingSlots[spCardSlotStat[existingOwned.card.id]]++;
        } else if (spCardUsedFree.has(existingOwned.card.id)) {
          localRemainingFree++;
        }
      }

      const excludedUsedIds = new Set(checkpointUsedIds);
      excludedUsedIds.add(rentalCandidate.card.id);

      const candidateFill = greedyFillOwned(
        cardContributions,
        localSelected,
        excludedUsedIds,
        localAccVo + rentalCandidate.raw_vo * accVoMul,
        localAccDa + rentalCandidate.raw_da * accDaMul,
        localAccVi + rentalCandidate.raw_vi * accViMul,
        localRemainingSlots,
        localRemainingFree,
        ownedSlots,
        statCap,
        character,
        overflowPenalty,
      );

      const candidateTotal = calculateCappedTotal(
        baseStats,
        candidateFill.selected,
        rentalCandidate,
        statCap,
      );

      if (candidateTotal > bestOverallTotal) {
        bestOverallTotal = candidateTotal;
        bestOverallRental = rentalCandidate;
        bestOverallSelected = candidateFill.selected;
      }
    }

    // 最良の結果を適用
    if (bestOverallSelected != null) {
      selected = bestOverallSelected;
      usedIds = new Set(selected.map((s) => s.card.id));
      // accumulator はキャラ補正込みのスケールで再構築
      accVo = baseStats.vo;
      accDa = baseStats.da;
      accVi = baseStats.vi;
      if (character != null) {
        accVo += character.base_status_bonus.vo + lessonStatTotals.vo * (character.para_bonus.vo / 100);
        accDa += character.base_status_bonus.da + lessonStatTotals.da * (character.para_bonus.da / 100);
        accVi += character.base_status_bonus.vi + lessonStatTotals.vi * (character.para_bonus.vi / 100);
      }
      for (const s of selected) {
        accVo += s.raw_vo * accVoMul;
        accDa += s.raw_da * accDaMul;
        accVi += s.raw_vi * accViMul;
      }
    }

    let finalRental: CardScore | undefined = bestOverallRental;
    if (finalRental != null) {
      finalRental = { ...finalRental, is_rental: true };
      selected.push(finalRental);
      usedIds.add(finalRental.card.id);
      accVo += finalRental.raw_vo * accVoMul;
      accDa += finalRental.raw_da * accDaMul;
      accVi += finalRental.raw_vi * accViMul;
    }
    } // end else (requiredRentalCard == null)
  }

  // レンタルなしで6枠未満なら全カードから補充
  if (rentalPool == null && selected.length < 6) {
    const fallback = allContributions.filter(
      (cs) => !usedIds.has(cs.card.id),
    );

    while (selected.length < 6) {
      const best = selectBestCard(
        fallback,
        usedIds,
        accVo,
        accDa,
        accVi,
        statCap,
        character,
        overflowPenalty,
      );
      if (best == null) break;

      selected.push(best);
      usedIds.add(best.card.id);
      accVo += best.raw_vo * accVoMul;
      accDa += best.raw_da * accDaMul;
      accVi += best.raw_vi * accViMul;
    }
  }

  // 所持カードのみ ON でレンタル枠が1枚も立っていなければ確保する。
  // (必須 + SP補充で所持枠が6枚埋まり「レンタル1枠」ブロックが発火しなかったケース)。
  // 以降の postOptimize / enforce* / optimizeRental* は通常フローと同じく
  // 「レンタル枠が1枚存在する」前提で動く。借用先の最適化は後続パスが実計算で行う。
  if (rentalPool != null) {
    ensureRentalSlot(
      selected,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    );
  }

  // レンタル含む deck 確定後、SP カードが spCounts 設定を超過しているなら
  // 余剰分の保護を外す → postOptimize で非SPカードへの差し替えを許可する。
  // (step 1 でレンタルが SP かどうかは未確定のため、ここで補正)
  unprotectExcessSpCards(selected, protectedIds, spCounts);

  // ポスト最適化: 実際の計算結果を使ってカードスワップを試行
  // (常時実行: trigger_count_bonus のような synergy 効果を greedy 単独では拾えないため)
  postOptimize(
    selected,
    cardContributions,
    protectedIds,
    plan,
    mainStats,
    uncapLevels,
    additionalCounts,
    statCap,
    character ?? null,
    memoryBonuses ?? null,
    cardTypeSlots,
    turnChoicesOverride,
    overflowPenalty,
  );

  // レンタル枠の再最適化: postOptimize は is_rental を絶対スワップしないため、
  // 所持カードが入れ替わった後にレンタル枠が最適でなくなるケースを実計算で補正する。
  optimizeRentalCard(
    selected,
    rentalPool,
    planType,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    protectedIds,
    spCounts,
    plan,
    additionalCounts,
    statCap,
    character ?? null,
    memoryBonuses ?? null,
    cardTypeSlots,
    turnChoicesOverride ?? buildTurnChoices(plan, mainStats),
    overflowPenalty,
  );

  // SP枚数の強制保証: postOptimize 後、SP カードが要求枚数に満たない場合は
  // プール内の余剰 SP カードで補充する (優先順位 必須カード > SP枚数 > 編成パターン)。
  // postOptimize は total を最大化するため非SPカードを優先しうるので、必ずこの後に実行する。
  enforceSpCounts(
    selected,
    cardContributions,
    rentalPool,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    protectedIds,
    spCounts,
  );

  // 編成パターンの強制保証: enforceSpCounts 後、属性枠 (cardTypeSlots) が要求枚数に
  // 満たない場合は余剰カードを当該属性カードに差し替える (優先順位の最下位)。
  // SP枚数 (enforceSpCounts) を崩さない範囲でのみ実行するため、必ずその後に呼ぶ。
  enforceTypeSlots(
    selected,
    cardContributions,
    rentalPool,
    planType,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    protectedIds,
    spCounts,
    cardTypeSlots,
  );

  // 局所最適の修復: 所持カード差し替え + レンタル差し替えの「同時手」を試し、
  // 実計算で合計が上がる場合のみ採用する (単調改善・悪化なし)。
  jointSwapRepair(
    selected,
    cardContributions,
    protectedIds,
    spCounts,
    rentalPool,
    planType,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    plan,
    additionalCounts,
    statCap,
    character ?? null,
    memoryBonuses ?? null,
    cardTypeSlots,
    turnChoicesOverride ?? buildTurnChoices(plan, mainStats),
    overflowPenalty,
  );

  // 借用アップグレード: デッキ外の低凸所持カードを4凸借用で投入し、弱い非必須カードを1枚落とす
  // ジョイント手を実計算で評価(改善時のみ採用)。4凸所持カードのレンタル浪費を解消する。
  if (rentalPool != null) {
    optimizeRentalBorrowUpgrade(
      selected,
      cardContributions,
      new Set(allCards.map((c) => c.id)),
      rentalPool,
      planType,
      plan,
      turnChoicesOverride ?? buildTurnChoices(plan, mainStats),
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
      additionalCounts,
      statCap,
      character ?? null,
      memoryBonuses ?? null,
      cardTypeSlots,
      spCounts,
      overflowPenalty,
    );
  }

  // レンタル枠の割り当て最適化: カード集合を変えず「どの1枚を4凸で借りるか」だけを最適化する。
  // 0凸所持の必須カードが所持枠に固定され、4凸所持カードがレンタル枠(借用恩恵ゼロ)に
  // 入っているケースを、低凸カードへ付け替えて total を上げる (属性枠・SP・必須は不変)。
  if (rentalPool != null) {
    optimizeRentalAssignment(
      selected,
      new Set(allCards.map((c) => c.id)),
      plan,
      turnChoicesOverride ?? buildTurnChoices(plan, mainStats),
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
      additionalCounts,
      statCap,
      character ?? null,
      memoryBonuses ?? null,
      overflowPenalty,
    );
  }

  // デッキ確定後の breakdown 再計算: producer の trigger_count_bonus を deck-aware に反映
  // - producer: trigger_count_bonus を raw_* に加算しない (consumer 側が adjustedCounts 経由で実発火数を加算するため)
  // - consumer: triggerCounts[target] が producer の bonus 分増加 → flat 効果が正しい回数で発火
  const adjustedCounts = recomputeBreakdownsDeckAware(
    selected,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
  );

  // キャップ適用後の実効値でTotalValueを再計算
  recalculateWithCap(selected, baseStats, statCap);

  selected.sort((a, b) => b.total_value - a.total_value);

  const totalValue = selected.reduce((sum, c) => sum + c.total_value, 0);

  return {
    label: generateLabel(cardTypeSlots, freeSlots),
    selected_cards: selected,
    total_value: totalValue,
    ability_summary: buildAbilitySummary(selected, adjustedCounts, uncapLevels),
  };
}

