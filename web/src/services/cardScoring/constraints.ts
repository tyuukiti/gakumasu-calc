import type { SupportCard, StatusValues } from '../../types/models';
import type { CardScore } from '../../types/results';
import { calculateCardContribution } from './contribution';
import type { TriggerBonusEntry } from './contribution';

export function meetsTypeSlots(
  cards: SupportCard[],
  cardTypeSlots: Record<string, number>,
): boolean {
  for (const [type, required] of Object.entries(cardTypeSlots)) {
    if (required <= 0) continue;
    const count = cards.filter(
      (c) => c.type === type || c.type === 'all' || c.type === 'as',
    ).length;
    if (count < required) return false;
  }
  return true;
}

/**
 * deck 確定後に SP カードが spCounts 設定を超過していたら、余剰分の保護を外す。
 * (step 1 ではレンタル枠が SP かどうか不明のため、ここで rental 込みで再評価)
 *
 * 保護が外れたカードは postOptimize で「同属性 SP のみ」のスワップ制限から解放され、
 * 非SPカード (例: いつまでも続けばいいのに) との差し替え候補になる。
 */
export function unprotectExcessSpCards(
  selected: CardScore[],
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
): void {
  if (spCounts == null) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) =>
        e.trigger === 'equip' &&
        e.value_type === 'sp_rate' &&
        (e.stat === stat || e.stat === 'all'),
    );

  for (const stat of ['vo', 'da', 'vi']) {
    const need = spCounts[stat] ?? 0;
    if (need <= 0) continue;

    const spCardsForStat = selected.filter((cs) => coversStat(cs.card, stat));
    if (spCardsForStat.length <= need) continue;
    const excess = spCardsForStat.length - need;

    // 余剰分: 弱い順 (raw 総和の昇順) に保護を外す。
    // ただし rental・必須カード・既に保護されていないカードは対象外。
    const trimCandidates = spCardsForStat
      .filter(
        (cs) => !cs.is_rental && !cs.is_required && protectedIds.has(cs.card.id),
      )
      .sort(
        (a, b) =>
          a.raw_vo + a.raw_da + a.raw_vi - (b.raw_vo + b.raw_da + b.raw_vi),
      );

    for (let i = 0; i < Math.min(excess, trimCandidates.length); i++) {
      protectedIds.delete(trimCandidates[i].card.id);
    }
  }
}

/**
 * デッキ確定後、SP カードが spCounts 設定の枚数に「満たない」場合に、プール内の
 * 余剰 SP カードと差し替えて要求枚数を満たす。
 *
 * ユーザ指定の優先順位「必須カード > SP枚数 > 編成パターン」を保証するための最終強制パス。
 * - 必須カード (is_required) は絶対に外さない
 * - 既に別属性のSP要件を満たしているカードも外さない
 * - 所持枠は所持プール(cardContributions)のSPカードで、レンタル枠はレンタルプールのSPカードで補充
 * - 補充のために編成パターン(cardTypeSlots)を崩すことは許容する (SP枚数 > 編成パターン)
 */
export function enforceSpCounts(
  selected: CardScore[],
  cardContributions: CardScore[],
  rentalPool: SupportCard[] | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
): void {
  if (spCounts == null) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) =>
        e.trigger === 'equip' &&
        e.value_type === 'sp_rate' &&
        (e.stat === stat || e.stat === 'all'),
    );

  // このカードが「まだ要求枚数 > 0 のいずれかの属性」のSPをカバーしているか
  // (= 外すと別属性のSP要件を壊しうるカードか)
  const coversAnyNeededSp = (card: SupportCard): boolean =>
    (['vo', 'da', 'vi'] as const).some(
      (s) => (spCounts[s] ?? 0) > 0 && coversStat(card, s),
    );

  const rawTotal = (cs: CardScore): number => cs.raw_vo + cs.raw_da + cs.raw_vi;

  for (const stat of ['vo', 'da', 'vi']) {
    const need = spCounts[stat] ?? 0;
    if (need <= 0) continue;

    let current = selected.filter((cs) => coversStat(cs.card, stat)).length;
    if (current >= need) continue;

    // 所持プールから、この属性のSPを持ち、まだデッキに居ないカード (寄与降順)
    const inDeck = () => new Set(selected.map((s) => s.card.id));
    const ownedSpCandidates = cardContributions
      .filter((cs) => coversStat(cs.card, stat) && !inDeck().has(cs.card.id))
      .sort((a, b) => rawTotal(b) - rawTotal(a));

    // 1) 所持枠を所持SPカードで補充
    while (current < need && ownedSpCandidates.length > 0) {
      // 外せる犠牲カード: 非レンタル・非必須・他のSP要件を満たしていない、寄与の弱い順
      // 同属性のカードを優先的に外して編成バランスへの影響を抑える
      const removable = selected.filter(
        (cs) => !cs.is_rental && !cs.is_required && !coversAnyNeededSp(cs.card),
      );
      if (removable.length === 0) break;
      removable.sort((a, b) => {
        const at = a.card.type === stat ? 0 : 1;
        const bt = b.card.type === stat ? 0 : 1;
        if (at !== bt) return at - bt;
        return rawTotal(a) - rawTotal(b);
      });
      const victim = removable[0];
      const sp = ownedSpCandidates.shift()!;
      const idx = selected.findIndex((cs) => cs.card.id === victim.card.id);
      selected[idx] = sp;
      protectedIds.delete(victim.card.id);
      protectedIds.add(sp.card.id);
      current++;
    }

    // 2) まだ不足 → レンタル枠をこの属性のレンタルSPカードに差し替え
    if (current < need && rentalPool != null) {
      const rentalIdx = selected.findIndex((cs) => cs.is_rental);
      if (
        rentalIdx >= 0 &&
        !coversStat(selected[rentalIdx].card, stat) &&
        !coversAnyNeededSp(selected[rentalIdx].card)
      ) {
        const used = inDeck();
        const rentalSp = rentalPool
          .filter((c) => coversStat(c, stat) && !used.has(c.id))
          .map((c) =>
            calculateCardContribution(
              c,
              triggerCounts,
              lessonAllocation,
              lessonStatTotals,
              { ...(uncapLevels ?? {}), [c.id]: 4 },
              triggerBonusInfo,
            ),
          )
          .sort((a, b) => rawTotal(b) - rawTotal(a));
        if (rentalSp.length > 0) {
          const best: CardScore = { ...rentalSp[0], is_rental: true };
          selected[rentalIdx] = best;
          protectedIds.add(best.card.id);
          current++;
        }
      }
    }
  }
}

/**
 * デッキ確定後、編成パターン (cardTypeSlots) の属性枚数が要求に「満たない」場合に、
 * 余剰カードを当該属性のカードと差し替えて要求枚数を満たす。
 *
 * ユーザ指定の優先順位「必須カード > SP枚数 > 編成パターン」の最下位 (編成パターン) を
 * 保証する最終強制パス。enforceSpCounts と対になる存在で、必ずその後に実行する。
 * - 必須カード (is_required) は絶対に外さない
 * - 外すと spCounts を割る SP カードは外さない (SP枚数 > 編成パターン)
 * - 外すと他属性の枠要件を割るカードも外さない
 * - 所持枠は所持プール、レンタル枠はレンタルプールから当該属性カードで補充する
 *
 * 例: 必須3枚(内1枚DaSP) + DaSP3枚指定 で「Visual 2 / フリー 3」を選ぶと、
 *     必須(da/vo)とSP補充(da)で所持5枠が埋まり vi 枠が取り逃される。残るレンタル枠が
 *     da で埋まり vi=1 のままになるのを、この関数がレンタル(またはdaの余剰所持枠)を
 *     vi カードに差し替えて vi=2 を保証する。
 */
export function enforceTypeSlots(
  selected: CardScore[],
  cardContributions: CardScore[],
  rentalPool: SupportCard[] | undefined,
  planType: string | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
  cardTypeSlots: Record<string, number> | undefined,
): void {
  if (cardTypeSlots == null) return;

  const isTypeMatch = (card: SupportCard, type: string): boolean =>
    card.type === type || card.type === 'all' || card.type === 'as';

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) =>
        e.trigger === 'equip' &&
        e.value_type === 'sp_rate' &&
        (e.stat === stat || e.stat === 'all'),
    );

  const rawTotal = (cs: CardScore): number => cs.raw_vo + cs.raw_da + cs.raw_vi;

  const countType = (type: string): number =>
    selected.filter((cs) => isTypeMatch(cs.card, type)).length;

  // このカードを外すと spCounts のいずれかの属性が要求枚数を割るか
  // (= SP枚数保証を崩しうる、外してはいけないカードか)
  const breaksSpCounts = (card: SupportCard): boolean => {
    if (spCounts == null) return false;
    for (const stat of ['vo', 'da', 'vi']) {
      const need = spCounts[stat] ?? 0;
      if (need <= 0) continue;
      if (coversStat(card, stat)) {
        const cur = selected.filter((cs) => coversStat(cs.card, stat)).length;
        if (cur <= need) return true;
      }
    }
    return false;
  };

  for (const [type, required] of Object.entries(cardTypeSlots)) {
    if (required <= 0) continue;

    // countType は毎回 selected を参照する。1スワップで必ず +1 進むが、念のため guard を置く。
    let guard = 0;
    while (countType(type) < required && guard++ < 6) {
      const inDeck = new Set(selected.map((s) => s.card.id));

      // 外せる犠牲カード候補 (寄与の弱い順):
      // - 必須でない / この属性のカードでない (外すと逆効果)
      // - 外しても spCounts を割らない (SP枚数 > 編成パターン)
      // - 外しても他属性の枠要件を割らない
      const removables = selected
        .map((cs, i) => ({ cs, i }))
        .filter(({ cs }) =>
          !cs.is_required &&
          !isTypeMatch(cs.card, type) &&
          !breaksSpCounts(cs.card) &&
          Object.entries(cardTypeSlots).every(
            ([t2, r2]) =>
              t2 === type ||
              r2 <= 0 ||
              !isTypeMatch(cs.card, t2) ||
              countType(t2) > r2,
          ),
        )
        .sort((a, b) => rawTotal(a.cs) - rawTotal(b.cs));

      let swapped = false;
      for (const { cs: victim, i } of removables) {
        let replacement: CardScore | null = null;

        if (victim.is_rental && rentalPool != null) {
          // レンタル枠 → レンタルプールから当該属性カード (4凸) で補充
          const pool =
            planType != null && planType !== ''
              ? rentalPool.filter(
                  (c) =>
                    c.plan == null ||
                    c.plan === '' ||
                    c.plan === planType ||
                    c.plan === 'free',
                )
              : rentalPool;
          const cand = pool
            .filter((c) => isTypeMatch(c, type) && !inDeck.has(c.id))
            .map((c) =>
              calculateCardContribution(
                c,
                triggerCounts,
                lessonAllocation,
                lessonStatTotals,
                { ...(uncapLevels ?? {}), [c.id]: 4 },
                triggerBonusInfo,
              ),
            )
            .sort((a, b) => rawTotal(b) - rawTotal(a))[0];
          if (cand != null) replacement = { ...cand, is_rental: true };
        } else {
          // 所持枠 → 所持プールから当該属性カードで補充
          const cand = cardContributions
            .filter(
              (cs2) => isTypeMatch(cs2.card, type) && !inDeck.has(cs2.card.id),
            )
            .sort((a, b) => rawTotal(b) - rawTotal(a))[0];
          if (cand != null) replacement = cand;
        }

        if (replacement == null) continue;

        protectedIds.delete(victim.card.id);
        selected[i] = replacement;
        protectedIds.add(replacement.card.id);
        swapped = true;
        break;
      }

      // この属性を満たせるカードがプールに無い → これ以上は補充不能
      if (!swapped) break;
    }
  }
}

