using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    /// <summary>
    /// 属性ごとの枚数制約+ステータス上限を考慮して最適6枚を選択する (マルチスタート)。
    ///
    /// 貪欲法はカード探索順に依存して別の局所最適へ落ちるため、カードデータ由来の複数順序
    /// (ID昇順/降順/レアリティ順) で選出を試し、実 Calculate の cap 後合計が最大の編成を採る。
    /// 順序はカードデータのみから決まるので呼び出し側の読込順に**非依存** (Web版 cardScoring.ts と
    /// 同挙動・デスクトップ版/Web版の乖離を解消)。各候補を実計算で採点し最大を採るので単調改善。
    /// </summary>
    /// <param name="spCounts">属性ごとのSP率カード必要枚数 (例: {"da":1, "vi":1})</param>
    public DeckResult SelectOptimalDeck(
        TrainingPlan plan,
        List<SupportCard> allCards,
        Dictionary<string, int> lessonAllocation,
        Dictionary<string, int> cardTypeSlots,
        List<string> mainStats,
        Dictionary<string, int>? spCounts = null,
        string? planType = null,
        AdditionalCounts? additionalCounts = null,
        Dictionary<string, int>? uncapLevels = null,
        List<SupportCard>? rentalPool = null,
        int freeSlots = 0,
        List<string>? requiredCardIds = null,
        Character? character = null,
        IReadOnlyList<MemoryBonus>? memoryBonuses = null,
        List<TurnChoice>? turnChoicesOverride = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        var statCap = plan.StatusLimit;
        // レンタルプールも順序依存を排除するため ID 昇順に正準化 (全スタート共通)
        var canonicalRental = rentalPool?.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

        DeckResult? best = null;
        int bestScore = int.MinValue;
        foreach (var pool in CandidateOrderings(allCards))
        {
            var result = SelectOptimalDeckOnce(
                plan, pool, lessonAllocation, cardTypeSlots, mainStats,
                spCounts, planType, additionalCounts, uncapLevels, canonicalRental,
                freeSlots, requiredCardIds, character, memoryBonuses,
                turnChoicesOverride, overflowPenalty);
            int score = EvalDeckScore(
                result, plan, mainStats, uncapLevels, additionalCounts,
                character, memoryBonuses, statCap, turnChoicesOverride, overflowPenalty);
            if (score > bestScore)
            {
                bestScore = score;
                best = result;
            }
        }
        return best!;
    }

    /// <summary>マルチスタート用の候補プール順序集合 (すべてカードデータ由来・入力順非依存)。</summary>
    private static List<List<SupportCard>> CandidateOrderings(List<SupportCard> cards)
    {
        static int RarityRank(string r) => r == "ssr" ? 0 : r == "sr" ? 1 : 2;
        var asc = cards.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var desc = cards.OrderByDescending(c => c.Id, StringComparer.Ordinal).ToList();
        var byRarity = cards
            .OrderBy(c => RarityRank(c.Rarity))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        return new List<List<SupportCard>> { asc, desc, byRarity };
    }

    /// <summary>
    /// 確定デッキを実 Calculate で採点し cap 後合計を返す (overflow罰則込み)。
    /// PostOptimize の評価と同一基準。マルチスタートの優劣比較に使う。
    /// </summary>
    private int EvalDeckScore(
        DeckResult result,
        TrainingPlan plan,
        List<string> mainStats,
        Dictionary<string, int>? uncapLevels,
        AdditionalCounts? additionalCounts,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        int statCap,
        List<TurnChoice>? turnChoicesOverride,
        OverflowPenaltyConfig? overflowPenalty)
    {
        var turnChoices = turnChoicesOverride ?? BuildTurnChoices(plan, mainStats);
        var uc = uncapLevels != null
            ? new Dictionary<string, int>(uncapLevels)
            : new Dictionary<string, int>();
        foreach (var cs in result.SelectedCards)
            if (cs.IsRental) uc[cs.Card.Id] = 4;
        var cards = result.SelectedCards.Select(cs => cs.Card).ToList();
        var calcService = new StatusCalculationService();
        var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
        int total = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
        if (overflowPenalty != null)
        {
            int overflow = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
            if (overflow > overflowPenalty.Threshold) total -= overflow * 2;
        }
        return total;
    }

    private DeckResult SelectOptimalDeckOnce(
        TrainingPlan plan,
        List<SupportCard> allCards,
        Dictionary<string, int> lessonAllocation,
        Dictionary<string, int> cardTypeSlots,
        List<string> mainStats,
        Dictionary<string, int>? spCounts = null,
        string? planType = null,
        AdditionalCounts? additionalCounts = null,
        Dictionary<string, int>? uncapLevels = null,
        List<SupportCard>? rentalPool = null,
        int freeSlots = 0,
        List<string>? requiredCardIds = null,
        Character? character = null,
        IReadOnlyList<MemoryBonus>? memoryBonuses = null,
        List<TurnChoice>? turnChoicesOverride = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        var statCap = plan.StatusLimit;
        var triggerCounts = CountTriggers(plan, lessonAllocation, mainStats, turnChoicesOverride);

        if (additionalCounts != null)
        {
            foreach (var kvp in additionalCounts.ToDictionary())
            {
                if (kvp.Value > 0)
                    triggerCounts[kvp.Key] = triggerCounts.GetValueOrDefault(kvp.Key) + kvp.Value;
            }
        }

        // 育成タイプでフィルタ
        var eligible = allCards;
        if (!string.IsNullOrEmpty(planType))
        {
            eligible = allCards
                .Where(c => string.IsNullOrEmpty(c.Plan)
                            || c.Plan == planType
                            || c.Plan == "free")
                .ToList();
        }

        // レッスン・イベント等のカード無しベースステータスを推定
        var baseStats = EstimateBaseStats(plan, lessonAllocation, turnChoicesOverride);

        // レッスンの属性別合計SpBonusを事前計算
        var lessonStatTotals = CalculateLessonStatTotals(plan, lessonAllocation, turnChoicesOverride);

        // trigger_count_bonus 用、対象トリガーごとの消費側カード集計
        var triggerBonusInfo = ComputeTriggerBonusInfo(eligible, uncapLevels);

        // 全カードの属性別寄与を事前計算
        var cardContributions = eligible
            .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo))
            .ToList();

        // 全カードプール (フィルタ外も補充用に)
        var allContributions = allCards
            .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo))
            .ToList();

        // 属性枠ごとに選択 (上限考慮)
        var selected = new List<CardScore>();
        var usedIds = new HashSet<string>();

        // 現在の累積ステータス (ベース + 選択済みカード)
        // キャラ補正を含めることで、cap-aware なカード選出が character の偏りを反映できるようにする
        int accVo = baseStats.Vo, accDa = baseStats.Da, accVi = baseStats.Vi;
        if (character != null)
        {
            accVo += character.BaseStatusBonus.Vo;
            accDa += character.BaseStatusBonus.Da;
            accVi += character.BaseStatusBonus.Vi;
            // para_bonus はレッスン上昇値に対する%補正 (近似)
            accVo += (int)Math.Floor(lessonStatTotals.Vo * (character.ParaBonus.Vo / 100.0));
            accDa += (int)Math.Floor(lessonStatTotals.Da * (character.ParaBonus.Da / 100.0));
            accVi += (int)Math.Floor(lessonStatTotals.Vi * (character.ParaBonus.Vi / 100.0));
        }
        // キャラの para_bonus はカード貢献にも乗る。accumulator 更新でも同じ倍率を適用する
        double accVoMul = 1.0 + (character?.ParaBonus.Vo ?? 0) / 100.0;
        double accDaMul = 1.0 + (character?.ParaBonus.Da ?? 0) / 100.0;
        double accViMul = 1.0 + (character?.ParaBonus.Vi ?? 0) / 100.0;

        // 属性枠・フリー枠の残数を管理するローカルコピー
        var remainingSlots = new Dictionary<string, int>(cardTypeSlots);
        int remainingFree = freeSlots;

        // ステップ0: 必須カードを強制挿入
        CardScore? requiredRentalCard = null;
        var protectedIds = new HashSet<string>();

        // ステップ1のSP率先取り用に「必須カードで消費した分を減算した」残り必要枚数。
        // UnprotectExcessSpCards / EnforceSpCounts では必須カードを含む元の spCounts(総数)で
        // 判定する必要があるため、減算後のカウントはこのローカル変数にのみ反映し、
        // spCounts 自体は上書きしない (上書きすると SP枚数の最終保証が必須カード分だけ過小評価される)。
        var spCountsForFill = spCounts != null ? new Dictionary<string, int>(spCounts) : null;

        if (requiredCardIds != null && requiredCardIds.Count > 0)
        {

            foreach (var cardId in requiredCardIds)
            {
                // allCards から探す、見つからなければ rentalPool からも探す
                var card = allCards.FirstOrDefault(c => c.Id == cardId)
                    ?? rentalPool?.FirstOrDefault(c => c.Id == cardId);
                if (card == null || usedIds.Contains(cardId)) continue;

                // 所持判定: rentalPool が null なら全カード所持扱い、そうでなければ eligible に含まれるか
                bool isOwned = rentalPool == null || eligible.Any(c => c.Id == cardId);

                // 凸数: 所持なら uncapLevels、未所持なら4凸
                var reqUncap = new Dictionary<string, int>(uncapLevels ?? new Dictionary<string, int>());
                if (!isOwned)
                    reqUncap[cardId] = 4;
                else if (!reqUncap.ContainsKey(cardId))
                    reqUncap[cardId] = 4;

                var contribution = CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, reqUncap, triggerBonusInfo);
                contribution.IsRequired = true;

                if (!isOwned && rentalPool != null)
                {
                    // 未所持 → レンタル枠として保留（selected に入れない）
                    contribution.IsRental = true;
                    requiredRentalCard = contribution;
                    usedIds.Add(cardId);
                    protectedIds.Add(cardId);

                    // レンタル借用する必須カードも SP率を持つならデッキの SP要求を満たすため、
                    // 所持枠向けの先取り枚数 (spCountsForFill) から減算する。減算しないとステップ1が
                    // SPカードを過剰確保して所持枠が6枚に達し、「レンタル1枠」ブロックが発火せず
                    // 必須レンタルカードが編成から漏れる (2026-08 ユーザ報告)。
                    if (spCountsForFill != null)
                    {
                        var rentalSpEffect = card.Effects.FirstOrDefault(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
                        if (rentalSpEffect != null)
                        {
                            foreach (var key in spCountsForFill.Keys.ToList())
                            {
                                if ((card.Type == key || card.Type == "all" || card.Type == "as") && spCountsForFill[key] > 0)
                                {
                                    spCountsForFill[key]--;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 所持 → 所持枠として追加
                    selected.Add(contribution);
                    usedIds.Add(cardId);
                    protectedIds.Add(cardId);
                    accVo += (int)(contribution.RawVo * accVoMul);
                    accDa += (int)(contribution.RawDa * accDaMul);
                    accVi += (int)(contribution.RawVi * accViMul);

                    // スロット消費 ("as" は "all" と同等に扱う)
                    bool isAllLike = card.Type == "all" || card.Type == "as";
                    if (!isAllLike && remainingSlots.ContainsKey(card.Type) && remainingSlots[card.Type] > 0)
                        remainingSlots[card.Type]--;
                    else if (isAllLike)
                    {
                        // "all"/"as" タイプ: 最大残数の属性枠を消費
                        var maxSlot = remainingSlots.OrderByDescending(s => s.Value).FirstOrDefault();
                        if (maxSlot.Value > 0)
                            remainingSlots[maxSlot.Key]--;
                        else
                            remainingFree = Math.Max(0, remainingFree - 1);
                    }
                    else
                        remainingFree = Math.Max(0, remainingFree - 1);

                    // SP率カード判定: 必須カードがSP率エフェクトを持つなら spCountsForFill を減算。
                    // all/as 型のSP率カードは全属性のSP発生率を上げる = da/vi 両方の必要数を1本ずつ満たす。
                    // ここで break すると1属性しか減算されず、ステップ1が残り属性のSPを過剰確保して
                    // 所持枠が膨張し、デッキが6枚を超える (必須+SP補充で7枚になるバグの原因)。
                    // → カバーする全属性を減算する (単一型は自属性のみ一致するので二重減算しない)。
                    if (spCountsForFill != null)
                    {
                        var spEffect = card.Effects.FirstOrDefault(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
                        if (spEffect != null)
                        {
                            foreach (var key in spCountsForFill.Keys.ToList())
                            {
                                if ((card.Type == key || card.Type == "all" || card.Type == "as") && spCountsForFill[key] > 0)
                                {
                                    spCountsForFill[key]--;
                                }
                            }
                        }
                    }
                }
            }
        }

        // ステップ1: SP率カードをユーザ指定枚数分、先に確保
        var spCardSlotStat = new Dictionary<string, string>(); // cardId -> 消費したスロットのstat key
        var spCardUsedFree = new HashSet<string>(); // フリー枠を消費したcardId
        if (spCountsForFill != null)
        {
            // 必須カードで消費済みの分を差し引いた残り枚数のみ先取りする
            foreach (var kvp in spCountsForFill)
            {
                var stat = kvp.Key;
                int need = kvp.Value;
                if (need <= 0) continue;

                // この属性のSP率を持つカードをステータス寄与順で選ぶ ("as" は "all" と同等)
                var spCandidates = cardContributions
                    .Where(cs => (cs.Card.Type == stat || cs.Card.Type == "all" || cs.Card.Type == "as")
                                 && !usedIds.Contains(cs.Card.Id)
                                 && cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"))
                    .ToList();

                for (int i = 0; i < need; i++)
                {
                    var best = SelectBestCard(spCandidates, usedIds, accVo, accDa, accVi, statCap, character, overflowPenalty);
                    if (best == null) break;

                    selected.Add(best);
                    usedIds.Add(best.Card.Id);
                    protectedIds.Add(best.Card.Id); // SP率カードはポスト最適化でスワップしない
                    accVo += (int)(best.RawVo * accVoMul);
                    accDa += (int)(best.RawDa * accDaMul);
                    accVi += (int)(best.RawVi * accViMul);

                    // SP率カードが属性枠にカウントされるか、フリー枠を消費するか判定
                    if (remainingSlots.ContainsKey(stat) && remainingSlots[stat] > 0)
                    {
                        spCardSlotStat[best.Card.Id] = stat;
                        remainingSlots[stat]--;
                    }
                    else
                    {
                        spCardUsedFree.Add(best.Card.Id);
                        remainingFree = Math.Max(0, remainingFree - 1);
                    }
                }
            }
        }

        // レンタルモード: 所持5枠 + レンタル1枠
        int ownedSlots = rentalPool != null ? 5 : 6;

        // チェックポイント保存（レンタルパターンC用）
        var checkpointSelected = new List<CardScore>(selected);
        var checkpointUsedIds = new HashSet<string>(usedIds);
        int checkpointAccVo = accVo, checkpointAccDa = accDa, checkpointAccVi = accVi;
        var checkpointRemainingSlots = new Dictionary<string, int>(remainingSlots);
        int checkpointRemainingFree = remainingFree;

        // ステップ2: グリーディに所持枠を埋める
        // レンタル必須カードがある場合はそのステータスを事前加算して補完的なカードを選ぶ
        int fillAccVo = accVo, fillAccDa = accDa, fillAccVi = accVi;
        if (requiredRentalCard != null)
        {
            fillAccVo += (int)(requiredRentalCard.RawVo * accVoMul);
            fillAccDa += (int)(requiredRentalCard.RawDa * accDaMul);
            fillAccVi += (int)(requiredRentalCard.RawVi * accViMul);
        }
        var fillResult = GreedyFillOwned(cardContributions, selected, usedIds, fillAccVo, fillAccDa, fillAccVi, remainingSlots, remainingFree, ownedSlots, statCap, character, overflowPenalty);
        selected = fillResult.Selected;
        usedIds = fillResult.UsedIds;
        // 事前加算分を差し引いて実際の累積ステータスを得る
        accVo = fillResult.AccVo - (int)((requiredRentalCard?.RawVo ?? 0) * accVoMul);
        accDa = fillResult.AccDa - (int)((requiredRentalCard?.RawDa ?? 0) * accDaMul);
        accVi = fillResult.AccVi - (int)((requiredRentalCard?.RawVi ?? 0) * accViMul);

        // 必須レンタルカードは所持枠が6枚埋まっていても最優先で投入する (必須 > SP枚数 > パターン)。
        // 何らかの経路で所持枠が埋まりきった場合は、最弱の非必須カード (非保護優先) を1枚落として
        // 必ずレンタル枠を空ける。落とした分の SP/属性枠は後続の EnforceSpCounts / EnforceTypeSlots が修復する。
        if (rentalPool != null && requiredRentalCard != null && selected.Count >= 6)
        {
            int victimIdx = -1;
            double victimKey = double.PositiveInfinity;
            for (int i = 0; i < selected.Count; i++)
            {
                var s = selected[i];
                if (s.IsRequired) continue;
                double key = (protectedIds.Contains(s.Card.Id) ? 1e12 : 0) + s.RawVo + s.RawDa + s.RawVi;
                if (key < victimKey)
                {
                    victimKey = key;
                    victimIdx = i;
                }
            }
            if (victimIdx >= 0)
            {
                var victim = selected[victimIdx];
                selected.RemoveAt(victimIdx);
                usedIds.Remove(victim.Card.Id);
                protectedIds.Remove(victim.Card.Id);
                accVo -= (int)(victim.RawVo * accVoMul);
                accDa -= (int)(victim.RawDa * accDaMul);
                accVi -= (int)(victim.RawVi * accViMul);
            }
        }

        // レンタル1枠: 全カードプールから4凸で最良の1枚を選択
        if (rentalPool != null && selected.Count < 6)
        {
            if (requiredRentalCard != null)
            {
                // 必須カードがレンタル枠を使用 → Pattern A/B をスキップ
                selected.Add(requiredRentalCard);
                usedIds.Add(requiredRentalCard.Card.Id);
                accVo += (int)(requiredRentalCard.RawVo * accVoMul);
                accDa += (int)(requiredRentalCard.RawDa * accDaMul);
                accVi += (int)(requiredRentalCard.RawVi * accViMul);
            }
            else
            {
            var rentalUncap = new Dictionary<string, int>();
            foreach (var c in rentalPool)
                rentalUncap[c.Id] = 4;

            // ユーザが4凸所持のカードはレンタル枠に置いても upgrade 恩恵がゼロ
            // (owned 4凸 = rental 4凸 で同値)。レンタル枠は本来「未所持/低凸カードを4凸として
            // 借りる」用途なので、4凸所持カードを意図的に rental に置くのは枠の浪費。→ 除外。
            // ただし全候補が4凸所持で空になる場合はフォールバックで除外しない。
            // 注意: uncapLevels は未所持カードにもエントリを持つ (インベントリは全カードを
            // デフォルト uncap=4 で保存する) ため、uncap だけで判定すると未所持カード全てを
            // 「4凸所持」と誤判定しレンタル候補から除外してしまう。所持集合との積で判定する。
            var ownedIdSet = allCards.Select(c => c.Id).ToHashSet();
            bool IsUserOwned4Star(string cardId) =>
                ownedIdSet.Contains(cardId) && (uncapLevels?.GetValueOrDefault(cardId) ?? 0) >= 4;
            var planFiltered = rentalPool
                .Where(c => string.IsNullOrEmpty(planType)
                            || string.IsNullOrEmpty(c.Plan)
                            || c.Plan == planType
                            || c.Plan == "free")
                .ToList();
            var rentalPoolForCandidates = planFiltered.Where(c => !IsUserOwned4Star(c.Id)).ToList();
            if (rentalPoolForCandidates.Count == 0)
                rentalPoolForCandidates = planFiltered;

            // レンタル候補: 所持で選ばれたカードも含めて全カードから計算
            var allRentalContributions = rentalPoolForCandidates
                .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, rentalUncap, triggerBonusInfo))
                .ToDictionary(cs => cs.Card.Id);

            // パターンA: 従来通り、未使用カードからレンタルを選択
            var unusedRentalCandidates = allRentalContributions.Values
                .Where(cs => !usedIds.Contains(cs.Card.Id))
                .ToList();
            var defaultRental = SelectBestCard(unusedRentalCandidates, usedIds, accVo, accDa, accVi, statCap, character, overflowPenalty);
            int defaultTotal = CalculateCappedTotal(baseStats, selected, defaultRental, statCap);

            // 最良の結果を追跡
            int bestOverallTotal = defaultTotal;
            CardScore? bestOverallRental = defaultRental;
            List<CardScore>? bestOverallSelected = null; // null = 現在のselectedをそのまま使う

            // パターンB: 所持カードXをレンタルX(4凸)に昇格し、空いた所持枠に代替カードを入れる
            foreach (var ownedCard in selected)
            {
                if (ownedCard.IsRequired) continue;

                if (!allRentalContributions.TryGetValue(ownedCard.Card.Id, out var rentalVersion))
                    continue;

                int rentalGain = rentalVersion.RawVo + rentalVersion.RawDa + rentalVersion.RawVi;
                int ownedGain = ownedCard.RawVo + ownedCard.RawDa + ownedCard.RawVi;
                if (rentalGain <= ownedGain) continue;

                int swapAccVo = accVo - (int)(ownedCard.RawVo * accVoMul);
                int swapAccDa = accDa - (int)(ownedCard.RawDa * accDaMul);
                int swapAccVi = accVi - (int)(ownedCard.RawVi * accViMul);

                var swapUsedIds = new HashSet<string>(usedIds);
                var replacementCandidates = cardContributions
                    .Where(cs => !swapUsedIds.Contains(cs.Card.Id))
                    .ToList();
                var replacement = SelectBestCard(replacementCandidates, swapUsedIds, swapAccVo, swapAccDa, swapAccVi, statCap, character, overflowPenalty);

                if (replacement == null) continue;

                var swapSelected = selected.Where(s => s.Card.Id != ownedCard.Card.Id).Append(replacement).ToList();
                int swapTotal = CalculateCappedTotal(baseStats, swapSelected, rentalVersion, statCap);

                if (swapTotal > bestOverallTotal)
                {
                    bestOverallTotal = swapTotal;
                    bestOverallRental = rentalVersion;
                    bestOverallSelected = swapSelected;
                }
            }

            // パターンC: 各レンタル候補に対して所持カードを最適に再選択
            // レンタルのステータスを事前加算し、補完的な所持カードが選ばれるようにする
            foreach (var rentalCandidate in allRentalContributions.Values)
            {
                // 必須カードのみスキップ（SP保護カードは許可）
                var existingOwned = checkpointSelected.FirstOrDefault(cs => cs.Card.Id == rentalCandidate.Card.Id);
                if (existingOwned?.IsRequired == true) continue;

                // チェックポイントに含まれるカード（SP保護等）→除外してスロット復元
                var localSelected = checkpointSelected;
                int localAccVo = checkpointAccVo, localAccDa = checkpointAccDa, localAccVi = checkpointAccVi;
                var localRemainingSlots = checkpointRemainingSlots;
                int localRemainingFree = checkpointRemainingFree;

                if (existingOwned != null)
                {
                    localSelected = checkpointSelected.Where(cs => cs.Card.Id != rentalCandidate.Card.Id).ToList();
                    localAccVo -= (int)(existingOwned.RawVo * accVoMul);
                    localAccDa -= (int)(existingOwned.RawDa * accDaMul);
                    localAccVi -= (int)(existingOwned.RawVi * accViMul);
                    localRemainingSlots = new Dictionary<string, int>(checkpointRemainingSlots);
                    if (spCardSlotStat.TryGetValue(existingOwned.Card.Id, out var slotStat))
                        localRemainingSlots[slotStat]++;
                    else if (spCardUsedFree.Contains(existingOwned.Card.Id))
                        localRemainingFree++;
                }

                // レンタル候補を所持選択から除外
                var excludedUsedIds = new HashSet<string>(checkpointUsedIds) { rentalCandidate.Card.Id };

                // レンタルのステータスを事前加算してグリーディ選択
                var candidateFill = GreedyFillOwned(
                    cardContributions, localSelected, excludedUsedIds,
                    localAccVo + (int)(rentalCandidate.RawVo * accVoMul),
                    localAccDa + (int)(rentalCandidate.RawDa * accDaMul),
                    localAccVi + (int)(rentalCandidate.RawVi * accViMul),
                    localRemainingSlots, localRemainingFree,
                    ownedSlots, statCap, character, overflowPenalty);

                int candidateTotal = CalculateCappedTotal(baseStats, candidateFill.Selected, rentalCandidate, statCap);

                if (candidateTotal > bestOverallTotal)
                {
                    bestOverallTotal = candidateTotal;
                    bestOverallRental = rentalCandidate;
                    bestOverallSelected = candidateFill.Selected;
                }
            }

            // 最良の結果を適用
            if (bestOverallSelected != null)
            {
                selected = bestOverallSelected;
                usedIds = new HashSet<string>(selected.Select(s => s.Card.Id));
                // accumulator はキャラ補正込みのスケールで再構築
                accVo = baseStats.Vo; accDa = baseStats.Da; accVi = baseStats.Vi;
                if (character != null)
                {
                    accVo += character.BaseStatusBonus.Vo + (int)Math.Floor(lessonStatTotals.Vo * (character.ParaBonus.Vo / 100.0));
                    accDa += character.BaseStatusBonus.Da + (int)Math.Floor(lessonStatTotals.Da * (character.ParaBonus.Da / 100.0));
                    accVi += character.BaseStatusBonus.Vi + (int)Math.Floor(lessonStatTotals.Vi * (character.ParaBonus.Vi / 100.0));
                }
                foreach (var s in selected)
                {
                    accVo += (int)(s.RawVo * accVoMul);
                    accDa += (int)(s.RawDa * accDaMul);
                    accVi += (int)(s.RawVi * accViMul);
                }
            }

            CardScore? finalRental = bestOverallRental;
            if (finalRental != null)
            {
                finalRental.IsRental = true;
                selected.Add(finalRental);
                usedIds.Add(finalRental.Card.Id);
                accVo += (int)(finalRental.RawVo * accVoMul);
                accDa += (int)(finalRental.RawDa * accDaMul);
                accVi += (int)(finalRental.RawVi * accViMul);
            }
            } // end else (requiredRentalCard == null)
        }

        // レンタルなしで6枠未満なら全カードから補充
        if (rentalPool == null && selected.Count < 6)
        {
            var fallback = allContributions
                .Where(cs => !usedIds.Contains(cs.Card.Id))
                .ToList();

            while (selected.Count < 6)
            {
                var best = SelectBestCard(fallback, usedIds, accVo, accDa, accVi, statCap, character, overflowPenalty);
                if (best == null) break;

                selected.Add(best);
                usedIds.Add(best.Card.Id);
                accVo += (int)(best.RawVo * accVoMul);
                accDa += (int)(best.RawDa * accDaMul);
                accVi += (int)(best.RawVi * accViMul);
            }
        }

        // 所持カードのみ ON でレンタル枠が1枚も立っていなければ確保する。
        // (必須 + SP補充で所持枠が6枚埋まり「レンタル1枠」ブロックが発火しなかったケース)。
        // 以降の PostOptimize / Enforce* / OptimizeRental* は通常フローと同じく
        // 「レンタル枠が1枚存在する」前提で動く。借用先の最適化は後続パスが実計算で行う。
        if (rentalPool != null)
        {
            EnsureRentalSlot(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo);
        }

        // レンタル含む deck 確定後、SP カードが spCounts 設定を超過しているなら
        // 余剰分の保護を外す → PostOptimize で非SPカードへの差し替えを許可する。
        // (step 1 でレンタルが SP かどうかは未確定のため、ここで補正)
        UnprotectExcessSpCards(selected, protectedIds, spCounts);

        // ポスト最適化: 実際の計算結果を使ってカードスワップを試行
        // (常時実行: trigger_count_bonus のような synergy 効果を greedy 単独では拾えないため)
        PostOptimize(selected, cardContributions, protectedIds,
            plan, lessonAllocation, mainStats, uncapLevels, additionalCounts, statCap,
            character, memoryBonuses, cardTypeSlots, turnChoicesOverride, overflowPenalty);

        // レンタル枠の再最適化: PostOptimize は IsRental を絶対スワップしないため、
        // 所持カードが入れ替わった後にレンタル枠が最適でなくなるケースを実計算で補正する。
        OptimizeRentalCard(selected, rentalPool, planType, triggerCounts,
            lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo, protectedIds, spCounts,
            plan, additionalCounts, statCap, character, memoryBonuses, cardTypeSlots,
            turnChoicesOverride ?? BuildTurnChoices(plan, mainStats), overflowPenalty);

        // SP枚数の強制保証: PostOptimize 後、SP カードが要求枚数に満たない場合は
        // プール内の余剰 SP カードで補充する (優先順位 必須カード > SP枚数 > 編成パターン)。
        // PostOptimize は total を最大化するため非SPカードを優先しうるので、必ずこの後に実行する。
        EnforceSpCounts(selected, cardContributions, rentalPool, triggerCounts,
            lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo, protectedIds, spCounts);

        // 編成パターンの強制保証: EnforceSpCounts 後、属性枠 (cardTypeSlots) が要求枚数に
        // 満たない場合は余剰カードを当該属性カードに差し替える (優先順位の最下位)。
        // SP枚数 (EnforceSpCounts) を崩さない範囲でのみ実行するため、必ずその後に呼ぶ。
        EnforceTypeSlots(selected, cardContributions, rentalPool, planType, triggerCounts,
            lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo, protectedIds,
            spCounts, cardTypeSlots);

        // 局所最適の修復: 所持カード差し替え + レンタル差し替えの「同時手」を試し、
        // 実計算で合計が上がる場合のみ採用する (単調改善・悪化なし)。
        JointSwapRepair(selected, cardContributions, protectedIds, spCounts, rentalPool, planType,
            triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo,
            plan, additionalCounts, statCap, character, memoryBonuses, cardTypeSlots,
            turnChoicesOverride ?? BuildTurnChoices(plan, mainStats), overflowPenalty);

        // 借用アップグレード: デッキ外の低凸所持カード・未所持カードを4凸借用で投入し、
        // 弱い非必須カードを1枚落とすジョイント手を実計算で評価(改善時のみ採用)。
        // 4凸所持カードのレンタル浪費を解消する。
        if (rentalPool != null)
        {
            OptimizeRentalBorrowUpgrade(selected, cardContributions, allCards.Select(c => c.Id).ToHashSet(),
                rentalPool, planType,
                plan, turnChoicesOverride ?? BuildTurnChoices(plan, mainStats), triggerCounts,
                lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo, additionalCounts,
                statCap, character, memoryBonuses, cardTypeSlots, spCounts, overflowPenalty);
        }

        // レンタル枠の割り当て最適化: カード集合を変えず「どの1枚を4凸で借りるか」だけを最適化する。
        // 0凸所持の必須カードが所持枠に固定され、4凸所持カードがレンタル枠(借用恩恵ゼロ)に
        // 入っているケースを、低凸カードへ付け替えて total を上げる (属性枠・SP・必須は不変)。
        if (rentalPool != null)
        {
            OptimizeRentalAssignment(selected, allCards.Select(c => c.Id).ToHashSet(), plan,
                turnChoicesOverride ?? BuildTurnChoices(plan, mainStats), triggerCounts,
                lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo, additionalCounts,
                statCap, character, memoryBonuses, overflowPenalty);
        }

        // デッキ確定後の breakdown 再計算: producer の trigger_count_bonus を deck-aware に反映
        var adjustedCounts = RecomputeBreakdownsDeckAware(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels);

        // キャップ適用後の実効値でTotalValueを再計算
        RecalculateWithCap(selected, baseStats, statCap);

        selected = selected.OrderByDescending(cs => cs.TotalValue).ToList();

        return new DeckResult
        {
            Label = GenerateLabel(cardTypeSlots, freeSlots),
            SelectedCards = selected,
            AbilitySummary = BuildAbilitySummary(selected, adjustedCounts, uncapLevels)
        };
    }
}
