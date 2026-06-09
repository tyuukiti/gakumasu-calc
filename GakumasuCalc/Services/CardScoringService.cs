using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class CardScoringService
{
    public const int DEFAULT_STAT_CAP = 2800;

    public class CardScore
    {
        public SupportCard Card { get; set; } = null!;
        public int TotalValue { get; set; }
        /// <summary>属性別の寄与内訳 (キャップ適用前)</summary>
        public int RawVo { get; set; }
        public int RawDa { get; set; }
        public int RawVi { get; set; }
        /// <summary>trigger_count_bonus 由来で「他カードへ寄与する」推定総量 (表示専用)</summary>
        public int TeamBonusTotal { get; set; }
        /// <summary>trigger_count_bonus 由来の寄与内訳 (UI で全件並べる用)</summary>
        public List<TeamBonusContributor> TeamBonusContributors { get; set; } = new();
        /// <summary>効果別の内訳</summary>
        public List<EffectBreakdown> Breakdowns { get; set; } = new();
        /// <summary>レンタルカードかどうか</summary>
        public bool IsRental { get; set; }
        /// <summary>必須カードかどうか</summary>
        public bool IsRequired { get; set; }
        /// <summary>計算に使われた凸数 (0-4)。レンタルは4凸借用、所持のみOFFの未所持カードは4。</summary>
        public int UncapLevel { get; set; }
    }

    public class TeamBonusContributor
    {
        public string CardName { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class EffectBreakdown
    {
        public string Reason { get; set; } = string.Empty;
        public string Stat { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class DeckResult
    {
        public string Label { get; set; } = string.Empty;
        public List<CardScore> SelectedCards { get; set; } = new();
        public int TotalValue => SelectedCards.Sum(c => c.TotalValue);
    }

    /// <summary>
    /// overflow罰則オプション。指定された場合、合計overflow が Threshold を超えた時のみ
    /// × 2 罰則を適用 (cap を大幅に超過するピックを抑制し、別属性カードへの差し替えを誘導)。
    /// null の場合は罰則無し。
    /// </summary>
    public class OverflowPenaltyConfig
    {
        public int Threshold { get; set; }
    }

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
        var baseStats = EstimateBaseStats(plan, lessonAllocation);

        // レッスンの属性別合計SpBonusを事前計算
        var lessonStatTotals = CalculateLessonStatTotals(plan, lessonAllocation);

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

                    // SP率カード判定: 必須カードがSP率エフェクトを持つなら spCountsForFill を減算
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
                                    break;
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
            bool IsUserOwned4Star(string cardId) =>
                (uncapLevels?.GetValueOrDefault(cardId) ?? 0) >= 4;
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

        // 借用アップグレード: デッキ外の低凸所持カードを4凸借用で投入し、弱い非必須カードを1枚落とす
        // ジョイント手を実計算で評価(改善時のみ採用)。4凸所持カードのレンタル浪費を解消する。
        if (rentalPool != null)
        {
            OptimizeRentalBorrowUpgrade(selected, cardContributions, allCards.Select(c => c.Id).ToHashSet(),
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
        RecomputeBreakdownsDeckAware(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels);

        // キャップ適用後の実効値でTotalValueを再計算
        RecalculateWithCap(selected, baseStats, statCap);

        selected = selected.OrderByDescending(cs => cs.TotalValue).ToList();

        return new DeckResult
        {
            Label = GenerateLabel(cardTypeSlots, freeSlots),
            SelectedCards = selected
        };
    }

    /// <summary>
    /// 実際のStatusCalculationServiceを使い、カードスワップで改善を試みるポスト最適化。
    /// 近似スコアリングでは捉えきれないパラボーナス等の相互作用を補正する。
    /// </summary>
    private static bool MeetsTypeSlots(
        List<SupportCard> cards,
        Dictionary<string, int> cardTypeSlots)
    {
        foreach (var kvp in cardTypeSlots)
        {
            if (kvp.Value <= 0) continue;
            int count = cards.Count(c => c.Type == kvp.Key || c.Type == "all" || c.Type == "as");
            if (count < kvp.Value) return false;
        }
        return true;
    }

    /// <summary>
    /// deck 確定後に SP カードが spCounts 設定を超過していたら、余剰分の保護を外す。
    /// (step 1 ではレンタル枠が SP かどうか不明のため、ここで rental 込みで再評価)
    ///
    /// 保護が外れたカードは PostOptimize で「同属性 SP のみ」のスワップ制限から解放され、
    /// 非SPカードとの差し替え候補になる。
    /// </summary>
    private static void UnprotectExcessSpCards(
        List<CardScore> selected,
        HashSet<string> protectedIds,
        Dictionary<string, int>? spCounts)
    {
        if (spCounts == null) return;

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e =>
                e.Trigger == "equip" &&
                e.ValueType == "sp_rate" &&
                (e.Stat == stat || e.Stat == "all"));

        foreach (var stat in new[] { "vo", "da", "vi" })
        {
            int need = spCounts.GetValueOrDefault(stat);
            if (need <= 0) continue;

            var spCardsForStat = selected.Where(cs => CoversStat(cs.Card, stat)).ToList();
            if (spCardsForStat.Count <= need) continue;
            int excess = spCardsForStat.Count - need;

            // 余剰分: 弱い順 (raw 総和の昇順) に保護を外す。
            // ただし rental・必須カード・既に保護されていないカードは対象外。
            var trimCandidates = spCardsForStat
                .Where(cs => !cs.IsRental && !cs.IsRequired && protectedIds.Contains(cs.Card.Id))
                .OrderBy(cs => cs.RawVo + cs.RawDa + cs.RawVi)
                .ToList();

            for (int i = 0; i < Math.Min(excess, trimCandidates.Count); i++)
            {
                protectedIds.Remove(trimCandidates[i].Card.Id);
            }
        }
    }

    /// <summary>
    /// デッキ確定後、SP カードが spCounts 設定の枚数に「満たない」場合に、プール内の
    /// 余剰 SP カードと差し替えて要求枚数を満たす。
    ///
    /// ユーザ指定の優先順位「必須カード > SP枚数 > 編成パターン」を保証するための最終強制パス。
    /// - 必須カード (IsRequired) は絶対に外さない
    /// - 既に別属性のSP要件を満たしているカードも外さない
    /// - 所持枠は所持プール(cardContributions)のSPカードで、レンタル枠はレンタルプールのSPカードで補充
    /// - 補充のために編成パターン(cardTypeSlots)を崩すことは許容する (SP枚数 > 編成パターン)
    /// </summary>
    private void EnforceSpCounts(
        List<CardScore> selected,
        List<CardScore> cardContributions,
        List<SupportCard>? rentalPool,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        HashSet<string> protectedIds,
        Dictionary<string, int>? spCounts)
    {
        if (spCounts == null) return;

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e =>
                e.Trigger == "equip" &&
                e.ValueType == "sp_rate" &&
                (e.Stat == stat || e.Stat == "all"));

        // このカードが「まだ要求枚数 > 0 のいずれかの属性」のSPをカバーしているか
        // (= 外すと別属性のSP要件を壊しうるカードか)
        bool CoversAnyNeededSp(SupportCard card) =>
            new[] { "vo", "da", "vi" }.Any(s => spCounts.GetValueOrDefault(s) > 0 && CoversStat(card, s));

        static int RawTotal(CardScore cs) => cs.RawVo + cs.RawDa + cs.RawVi;

        foreach (var stat in new[] { "vo", "da", "vi" })
        {
            int need = spCounts.GetValueOrDefault(stat);
            if (need <= 0) continue;

            int current = selected.Count(cs => CoversStat(cs.Card, stat));
            if (current >= need) continue;

            HashSet<string> InDeck() => selected.Select(s => s.Card.Id).ToHashSet();

            // 所持プールから、この属性のSPを持ち、まだデッキに居ないカード (寄与降順)
            var ownedSpCandidates = cardContributions
                .Where(cs => CoversStat(cs.Card, stat) && !InDeck().Contains(cs.Card.Id))
                .OrderByDescending(RawTotal)
                .ToList();

            // 1) 所持枠を所持SPカードで補充
            int ownedIdx = 0;
            while (current < need && ownedIdx < ownedSpCandidates.Count)
            {
                // 外せる犠牲カード: 非レンタル・非必須・他のSP要件を満たしていない、寄与の弱い順
                // 同属性のカードを優先的に外して編成バランスへの影響を抑える
                var victim = selected
                    .Where(cs => !cs.IsRental && !cs.IsRequired && !CoversAnyNeededSp(cs.Card))
                    .OrderBy(cs => cs.Card.Type == stat ? 0 : 1)
                    .ThenBy(RawTotal)
                    .FirstOrDefault();
                if (victim == null) break;
                var sp = ownedSpCandidates[ownedIdx++];
                int idx = selected.FindIndex(cs => cs.Card.Id == victim.Card.Id);
                selected[idx] = sp;
                protectedIds.Remove(victim.Card.Id);
                protectedIds.Add(sp.Card.Id);
                current++;
            }

            // 2) まだ不足 → レンタル枠をこの属性のレンタルSPカードに差し替え
            if (current < need && rentalPool != null)
            {
                int rentalIdx = selected.FindIndex(cs => cs.IsRental);
                if (rentalIdx >= 0 &&
                    !CoversStat(selected[rentalIdx].Card, stat) &&
                    !CoversAnyNeededSp(selected[rentalIdx].Card))
                {
                    var used = InDeck();
                    var rentalSp = rentalPool
                        .Where(c => CoversStat(c, stat) && !used.Contains(c.Id))
                        .Select(c =>
                        {
                            var uc = uncapLevels != null
                                ? new Dictionary<string, int>(uncapLevels)
                                : new Dictionary<string, int>();
                            uc[c.Id] = 4;
                            return CalculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
                        })
                        .OrderByDescending(RawTotal)
                        .ToList();
                    if (rentalSp.Count > 0)
                    {
                        var best = rentalSp[0];
                        best.IsRental = true;
                        selected[rentalIdx] = best;
                        protectedIds.Add(best.Card.Id);
                        current++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// デッキ確定後、編成パターン (cardTypeSlots) の属性枚数が要求に「満たない」場合に、
    /// 余剰カードを当該属性のカードと差し替えて要求枚数を満たす。
    ///
    /// ユーザ指定の優先順位「必須カード > SP枚数 > 編成パターン」の最下位 (編成パターン) を
    /// 保証する最終強制パス。EnforceSpCounts と対になり、必ずその後に実行する。
    /// - 必須カード (IsRequired) は絶対に外さない
    /// - 外すと spCounts を割る SP カードは外さない (SP枚数 > 編成パターン)
    /// - 外すと他属性の枠要件を割るカードも外さない
    /// - 所持枠は所持プール、レンタル枠はレンタルプールから当該属性カードで補充する
    ///
    /// 例: 必須3枚(内1枚DaSP) + DaSP3枚指定 で「Visual 2 / フリー 3」を選ぶと、
    ///     必須(da/vo)とSP補充(da)で所持5枠が埋まり vi 枠が取り逃される。残るレンタル枠が
    ///     da で埋まり vi=1 のままになるのを、この関数がレンタル(またはdaの余剰所持枠)を
    ///     vi カードに差し替えて vi=2 を保証する。
    /// </summary>
    private void EnforceTypeSlots(
        List<CardScore> selected,
        List<CardScore> cardContributions,
        List<SupportCard>? rentalPool,
        string? planType,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        HashSet<string> protectedIds,
        Dictionary<string, int>? spCounts,
        Dictionary<string, int>? cardTypeSlots)
    {
        if (cardTypeSlots == null) return;

        bool IsTypeMatch(SupportCard card, string type) =>
            card.Type == type || card.Type == "all" || card.Type == "as";

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e =>
                e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));

        static int RawTotal(CardScore cs) => cs.RawVo + cs.RawDa + cs.RawVi;

        int CountType(string type) => selected.Count(cs => IsTypeMatch(cs.Card, type));

        // このカードを外すと spCounts のいずれかの属性が要求枚数を割るか
        // (= SP枚数保証を崩しうる、外してはいけないカードか)
        bool BreaksSpCounts(SupportCard card)
        {
            if (spCounts == null) return false;
            foreach (var stat in new[] { "vo", "da", "vi" })
            {
                int need = spCounts.GetValueOrDefault(stat);
                if (need <= 0) continue;
                if (CoversStat(card, stat))
                {
                    int cur = selected.Count(cs => CoversStat(cs.Card, stat));
                    if (cur <= need) return true;
                }
            }
            return false;
        }

        foreach (var kvp in cardTypeSlots)
        {
            string type = kvp.Key;
            int required = kvp.Value;
            if (required <= 0) continue;

            // CountType は毎回 selected を参照する。1スワップで必ず +1 進むが、念のため guard を置く。
            int guard = 0;
            while (CountType(type) < required && guard++ < 6)
            {
                var inDeck = selected.Select(s => s.Card.Id).ToHashSet();

                // 外せる犠牲カード候補 (寄与の弱い順):
                // - 必須でない / この属性のカードでない (外すと逆効果)
                // - 外しても spCounts を割らない (SP枚数 > 編成パターン)
                // - 外しても他属性の枠要件を割らない
                var removables = selected
                    .Select((cs, i) => (cs, i))
                    .Where(t =>
                        !t.cs.IsRequired &&
                        !IsTypeMatch(t.cs.Card, type) &&
                        !BreaksSpCounts(t.cs.Card) &&
                        cardTypeSlots.All(kv2 =>
                            kv2.Key == type ||
                            kv2.Value <= 0 ||
                            !IsTypeMatch(t.cs.Card, kv2.Key) ||
                            CountType(kv2.Key) > kv2.Value))
                    .OrderBy(t => RawTotal(t.cs))
                    .ToList();

                bool swapped = false;
                foreach (var (victim, idx) in removables)
                {
                    CardScore? replacement = null;

                    if (victim.IsRental && rentalPool != null)
                    {
                        // レンタル枠 → レンタルプールから当該属性カード (4凸) で補充
                        IEnumerable<SupportCard> pool = rentalPool;
                        if (!string.IsNullOrEmpty(planType))
                            pool = pool.Where(c =>
                                string.IsNullOrEmpty(c.Plan) || c.Plan == planType || c.Plan == "free");
                        var cand = pool
                            .Where(c => IsTypeMatch(c, type) && !inDeck.Contains(c.Id))
                            .Select(c =>
                            {
                                var uc = uncapLevels != null
                                    ? new Dictionary<string, int>(uncapLevels)
                                    : new Dictionary<string, int>();
                                uc[c.Id] = 4;
                                return CalculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
                            })
                            .OrderByDescending(RawTotal)
                            .FirstOrDefault();
                        if (cand != null)
                        {
                            cand.IsRental = true;
                            replacement = cand;
                        }
                    }
                    else
                    {
                        // 所持枠 → 所持プールから当該属性カードで補充
                        var cand = cardContributions
                            .Where(cs2 => IsTypeMatch(cs2.Card, type) && !inDeck.Contains(cs2.Card.Id))
                            .OrderByDescending(RawTotal)
                            .FirstOrDefault();
                        if (cand != null) replacement = cand;
                    }

                    if (replacement == null) continue;

                    protectedIds.Remove(victim.Card.Id);
                    selected[idx] = replacement;
                    protectedIds.Add(replacement.Card.Id);
                    swapped = true;
                    break;
                }

                // この属性を満たせるカードがプールに無い → これ以上は補充不能
                if (!swapped) break;
            }
        }
    }

    /// <summary>
    /// PostOptimize はレンタル枠 (IsRental) を絶対にスワップしないため、所持カードが
    /// PostOptimize で入れ替わった後に「レンタル枠が最適でなくなる」ケースを補正できない。
    ///
    /// 例: レンタル選出時点で お城(Vo) が所持枠を占有 → レンタルに ほっぺた(Vi) が選ばれる。
    /// その後 PostOptimize が 所持の お城 を 自分と向き合う(Da) に差し替えると お城 が枠から外れるが、
    /// レンタルは ほっぺた のまま固定される。本来は お城 をレンタルに据えた方が合計が高い。
    ///
    /// このパスは PostOptimize 後に、実際の計算(Calculate)でレンタル枠を再評価し、
    /// レンタルプール内の最良カードに差し替える。タイプ枠・SP枚数の制約は維持する。
    /// </summary>
    private void OptimizeRentalCard(
        List<CardScore> selected,
        List<SupportCard>? rentalPool,
        string? planType,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        HashSet<string> protectedIds,
        Dictionary<string, int>? spCounts,
        TrainingPlan plan,
        AdditionalCounts? additionalCounts,
        int statCap,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        Dictionary<string, int>? cardTypeSlots,
        List<TurnChoice> turnChoices,
        OverflowPenaltyConfig? overflowPenalty)
    {
        if (rentalPool == null) return;
        int rentalIdx = selected.FindIndex(cs => cs.IsRental);
        if (rentalIdx < 0) return;
        var current = selected[rentalIdx];
        if (current.IsRequired) return;

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e =>
                e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));
        bool MeetsSpCounts(List<SupportCard> cards)
        {
            if (spCounts == null) return true;
            foreach (var kvp in spCounts)
            {
                if (kvp.Value <= 0) continue;
                if (cards.Count(c => CoversStat(c, kvp.Key)) < kvp.Value) return false;
            }
            return true;
        }

        var calcService = new StatusCalculationService();
        // 評価用: レンタル候補を 4凸 として実際の計算で合計を求める (PostOptimize と同一ロジック)
        int EvaluateFull(List<SupportCard> cards, string rentalCardId)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in selected.Where(c => c.IsRental))
                uc[cs.Card.Id] = 4;
            uc[rentalCardId] = 4;
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int total = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
            if (overflowPenalty != null)
            {
                int overflow = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (overflow > overflowPenalty.Threshold) total -= overflow * 2;
            }
            return total;
        }

        var ownedIds = new HashSet<string>(
            selected.Where((_, i) => i != rentalIdx).Select(s => s.Card.Id));
        var pool = rentalPool.Where(c => !ownedIds.Contains(c.Id));
        if (!string.IsNullOrEmpty(planType))
        {
            pool = pool.Where(c =>
                string.IsNullOrEmpty(c.Plan) || c.Plan == planType || c.Plan == "free");
        }

        var currentCards = selected.Select(s => s.Card).ToList();
        int bestTotal = EvaluateFull(currentCards, current.Card.Id);
        SupportCard? bestCard = null;

        // 全プールに Calculate を回すと重いので、素の寄与上位のみ実評価する。
        var rentalUncap = new Dictionary<string, int>();
        foreach (var c in pool) rentalUncap[c.Id] = 4;
        var ranked = pool
            .Select(c =>
            {
                var cs = CalculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, rentalUncap, triggerBonusInfo);
                return (card: c, score: cs.RawVo + cs.RawDa + cs.RawVi);
            })
            .OrderByDescending(x => x.score)
            .Take(40)
            .Select(x => x.card)
            .ToList();

        foreach (var cand in ranked)
        {
            var testCards = new List<SupportCard>(currentCards);
            testCards[rentalIdx] = cand;
            if (cardTypeSlots != null && !MeetsTypeSlots(testCards, cardTypeSlots)) continue;
            if (!MeetsSpCounts(testCards)) continue;
            int total = EvaluateFull(testCards, cand.Id);
            if (total > bestTotal)
            {
                bestTotal = total;
                bestCard = cand;
            }
        }

        if (bestCard != null)
        {
            var newUncap = new Dictionary<string, int>(uncapLevels ?? new()) { [bestCard.Id] = 4 };
            var cs = CalculateCardContribution(
                bestCard, triggerCounts, lessonAllocation, lessonStatTotals, newUncap, triggerBonusInfo);
            cs.IsRental = true;
            cs.IsRequired = false;
            selected[rentalIdx] = cs;
            protectedIds.Remove(current.Card.Id);
        }
    }

    /// <summary>
    /// 所持カードのみ ON では編成6枚 = 所持5枚 + レンタル1枚(4凸借用) が原則で、6枚中必ず1枚を
    /// レンタル(借用先)として指定する。ところが必須カード + SP補充で所持枠が6枚埋まると、
    /// 「レンタル1枠」選出ブロック (selected.Count &lt; 6) が発火せず IsRental が1枚も立たないまま
    /// になる (= レンタル枠が消える)。必須枚数を増やすとレンタルが消えるバグはこれが原因。
    ///
    /// レンタル枠は「デッキ内のどの1枚を4凸として借りるか」の指定にすぎず、借用は必ず total を
    /// 増やす(または同値)ので、ここで最低凸カードを暫定レンタルに指定して枠を必ず確保する。
    /// 真に最良の借用先への付け替え(別カードへの差し替え含む)は後続の
    /// OptimizeRentalCard / OptimizeRentalAssignment が実計算で行う。
    /// </summary>
    private void EnsureRentalSlot(
        List<CardScore> selected,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo)
    {
        if (selected.Count == 0) return;
        if (selected.Any(cs => cs.IsRental)) return;

        // 最低凸のカードを借用先に選ぶ (借用恩恵が最大)。凸不明は4凸扱い。
        int target = 0;
        int lowest = int.MaxValue;
        for (int i = 0; i < selected.Count; i++)
        {
            int u = uncapLevels != null && uncapLevels.TryGetValue(selected[i].Card.Id, out var v) ? v : 4;
            if (u < lowest)
            {
                lowest = u;
                target = i;
            }
        }

        var uc = new Dictionary<string, int>(uncapLevels ?? new()) { [selected[target].Card.Id] = 4 };
        bool wasRequired = selected[target].IsRequired;
        var recomputed = CalculateCardContribution(
            selected[target].Card, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
        recomputed.IsRental = true;
        recomputed.IsRequired = wasRequired;
        selected[target] = recomputed;
    }

    /// <summary>
    /// レンタル枠は「デッキ内のどの1枚を4凸として借りるか」の指定にすぎない。
    /// 所持カードのみ ON では非レンタル5枚は所持凸数で、レンタル1枚は4凸で評価される。
    /// カード集合を変えずに「どのカードをレンタル(4凸借用)にするか」だけを最適化する。
    ///
    /// バグ例: 0凸所持の必須カードが所持枠(0凸)に固定され、4凸所持カードがレンタル枠
    /// (4凸借用=upgrade恩恵ゼロ)に入ると、レンタルを低凸カードに付け替えるだけで total が上がる。
    /// カード集合は不変なので属性枠・SP枚数・必須はすべて保持される (単調改善・悪化なし)。
    ///
    /// - デッキに未所持カードがあれば、それは必ずレンタル(所持枠に置けない)→ 付け替え不可で何もしない
    /// - 全カード所持なら、各カードをレンタルにした実計算 total を比較し最大の割り当てを採用
    ///
    /// 注: RecomputeBreakdownsDeckAware は producer 不在時に早期 return するため、
    ///     付け替えた2枚の raw 寄与はこの関数内で再計算しておく (フラグ変更だけに頼らない)。
    /// </summary>
    private void OptimizeRentalAssignment(
        List<CardScore> selected,
        HashSet<string> ownedIds,
        TrainingPlan plan,
        List<TurnChoice> turnChoices,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        AdditionalCounts? additionalCounts,
        int statCap,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        OverflowPenaltyConfig? overflowPenalty)
    {
        int rentalIdx = selected.FindIndex(cs => cs.IsRental);
        if (rentalIdx < 0) return; // レンタル枠なし

        // デッキ内の未所持カードは必ずレンタル固定 (所持枠に置けない) → 付け替え不可
        if (selected.Any(cs => !ownedIds.Contains(cs.Card.Id))) return;

        var cards = selected.Select(cs => cs.Card).ToList();
        var calcService = new StatusCalculationService();
        int EvaluateWith(string rentalCardId)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new()) { [rentalCardId] = 4 };
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int total = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
            if (overflowPenalty != null)
            {
                int overflow = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (overflow > overflowPenalty.Threshold) total -= overflow * 2;
            }
            return total;
        }

        // 借用先は「合計が最大」かつ、同点なら「所持凸が最低」のカードを選ぶ。
        // レンタルは4凸借用なので低凸カードほど借用恩恵が大きく、上限張り付き等で合計が
        // 同点になるケースでは、4凸所持カードをレンタルに据える浪費を避けて低凸カードへ寄せる
        // (レンタル枠はデッキ内最低凸の所持カードであるべき、という原則)。
        int UncapOf(string id) => uncapLevels != null && uncapLevels.TryGetValue(id, out var v) ? v : 4;
        string currentId = selected[rentalIdx].Card.Id;
        string bestId = currentId;
        int bestTotal = EvaluateWith(currentId);
        int bestUncap = UncapOf(currentId);
        foreach (var cs in selected)
        {
            if (cs.Card.Id == currentId) continue;
            int t = EvaluateWith(cs.Card.Id);
            int u = UncapOf(cs.Card.Id);
            if (t > bestTotal)
            {
                bestTotal = t;
                bestId = cs.Card.Id;
                bestUncap = u;
            }
            else if (t == bestTotal && u < bestUncap)
            {
                bestId = cs.Card.Id;
                bestUncap = u;
            }
        }

        if (bestId == currentId) return;

        // 付け替え: レンタル状態が変わる2枚の raw 寄与を新しい凸数で再計算する
        for (int i = 0; i < selected.Count; i++)
        {
            bool willBeRental = selected[i].Card.Id == bestId;
            if (willBeRental == selected[i].IsRental) continue;
            var uc = willBeRental
                ? new Dictionary<string, int>(uncapLevels ?? new()) { [selected[i].Card.Id] = 4 }
                : new Dictionary<string, int>(uncapLevels ?? new());
            bool wasRequired = selected[i].IsRequired;
            var recomputed = CalculateCardContribution(
                selected[i].Card, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
            recomputed.IsRental = willBeRental;
            recomputed.IsRequired = wasRequired;
            selected[i] = recomputed;
        }
    }

    /// <summary>
    /// 借用アップグレード（レンタル枠のジョイント最適化）。
    ///
    /// ユーザが低凸(uncap&lt;4)で所持するカードは、所持枠では低凸の弱い寄与しか出ないが、
    /// レンタル枠で4凸借用すれば本来の強さを発揮する。一方、4凸所持カードをレンタルに置くのは
    /// 借用恩恵ゼロの浪費。既存パス(OptimizeRentalCard=所持固定 / OptimizeRentalAssignment=デッキ内再割当)では
    /// 「弱い所持カードを1枚落として、デッキ外の低凸所持カードを4凸借用する」ジョイント手を取り逃す。
    ///
    /// このパスは、デッキ外の低凸所持カードC(4凸寄与上位)を借用枠に投入し、デッキ内の非必須カードVを
    /// 1枚落とす手を実計算で評価し、合計が上がる場合のみ採用する(単調改善・悪化なし)。
    /// 旧レンタルカード(4凸所持等)は所持枠へ移る。属性枠(cardTypeSlots)・SP枚数・必須は維持する。
    /// </summary>
    private void OptimizeRentalBorrowUpgrade(
        List<CardScore> selected,
        List<CardScore> cardContributions,
        HashSet<string> ownedIds,
        TrainingPlan plan,
        List<TurnChoice> turnChoices,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        AdditionalCounts? additionalCounts,
        int statCap,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        Dictionary<string, int>? cardTypeSlots,
        Dictionary<string, int>? spCounts,
        OverflowPenaltyConfig? overflowPenalty)
    {
        int rentalIdx = selected.FindIndex(cs => cs.IsRental);
        if (rentalIdx < 0) return;
        // デッキに未所持カードがある = それがレンタル固定。借用枠は使用中 → 対象外。
        if (selected.Any(cs => !ownedIds.Contains(cs.Card.Id))) return;

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));
        bool MeetsSp(List<SupportCard> cards)
        {
            if (spCounts == null) return true;
            foreach (var kvp in spCounts)
            {
                if (kvp.Value <= 0) continue;
                if (cards.Count(c => CoversStat(c, kvp.Key)) < kvp.Value) return false;
            }
            return true;
        }
        static int RawTotal(CardScore cs) => cs.RawVo + cs.RawDa + cs.RawVi;

        var calcService = new StatusCalculationService();
        int RealTotal(List<SupportCard> cards, string rentalId)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new()) { [rentalId] = 4 };
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int t = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
            if (overflowPenalty != null)
            {
                int o = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (o > overflowPenalty.Threshold) t -= o * 2;
            }
            return t;
        }
        CardScore At4(SupportCard card)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new()) { [card.Id] = 4 };
            return CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
        }

        var inDeck = selected.Select(s => s.Card.Id).ToHashSet();
        // 借用候補: デッキ外・低凸(uncap<4)所持カード。4凸寄与の上位のみ評価しコストを抑える。
        var borrowCands = cardContributions
            .Where(cs => !inDeck.Contains(cs.Card.Id) && (uncapLevels?.GetValueOrDefault(cs.Card.Id) ?? 0) < 4)
            .Select(cs => At4(cs.Card))
            .OrderByDescending(RawTotal)
            .Take(12)
            .ToList();
        if (borrowCands.Count == 0) return;

        var currentCards = selected.Select(s => s.Card).ToList();
        int bestTotal = RealTotal(currentCards, selected[rentalIdx].Card.Id);
        int bestVi = -1;
        CardScore? bestCand = null;

        foreach (var cand in borrowCands)
        {
            for (int vi = 0; vi < selected.Count; vi++)
            {
                if (selected[vi].IsRequired) continue;
                var trial = new List<SupportCard>(currentCards);
                trial[vi] = cand.Card;
                if (cardTypeSlots != null && !MeetsTypeSlots(trial, cardTypeSlots)) continue;
                if (!MeetsSp(trial)) continue;
                int t = RealTotal(trial, cand.Card.Id);
                if (t > bestTotal)
                {
                    bestTotal = t;
                    bestVi = vi;
                    bestCand = cand;
                }
            }
        }

        if (bestCand == null || bestVi < 0) return;

        for (int i = 0; i < selected.Count; i++)
        {
            if (i == bestVi)
            {
                bestCand.IsRental = true;
                bestCand.IsRequired = false;
                selected[i] = bestCand;
            }
            else if (selected[i].IsRental)
            {
                bool wasRequired = selected[i].IsRequired;
                var owned = CalculateCardContribution(
                    selected[i].Card, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels ?? new(), triggerBonusInfo);
                owned.IsRental = false;
                owned.IsRequired = wasRequired;
                selected[i] = owned;
            }
        }
    }

    /// <summary>
    /// 局所最適の修復: PostOptimize(所持のみ・レンタル固定) と OptimizeRentalCard(レンタルのみ・所持固定)
    /// は別々に最適化するため、「所持カード差し替え」と「レンタル差し替え」を“同時”に行わないと
    /// 届かない最適解を取り逃す (例: 所持SP 0023→ふわふわ と レンタル 0027→0069 を同時に行うと合計が上がる)。
    /// 有望な未編成カード(SP率 or trigger_count_bonus producer)を1枚強制投入し、レンタルを再最適化して
    /// 実計算で合計が上がる場合のみ採用する。合計が上がる時しか採用しないため悪化しない (単調改善)。
    /// </summary>
    private void JointSwapRepair(
        List<CardScore> selected,
        List<CardScore> cardContributions,
        HashSet<string> protectedIds,
        Dictionary<string, int>? spCounts,
        List<SupportCard>? rentalPool,
        string? planType,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo,
        TrainingPlan plan,
        AdditionalCounts? additionalCounts,
        int statCap,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        Dictionary<string, int>? cardTypeSlots,
        List<TurnChoice> turnChoices,
        OverflowPenaltyConfig? overflowPenalty)
    {
        bool HasSpRate(SupportCard card) =>
            card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
        string? SpStat(SupportCard card) =>
            card.Effects.FirstOrDefault(e => e.Trigger == "equip" && e.ValueType == "sp_rate")?.Stat;
        bool IsProducer(SupportCard card) =>
            card.Effects.Any(e => e.ValueType == "trigger_count_bonus" && !string.IsNullOrEmpty(e.TriggerTarget));
        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));
        int RawTotal(CardScore cs) => cs.RawVo + cs.RawDa + cs.RawVi;

        var calcService = new StatusCalculationService();
        int EvalReal(List<SupportCard> cards, HashSet<string> rentalIds)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var id in rentalIds) uc[id] = 4;
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int total = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
            if (overflowPenalty != null)
            {
                int overflow = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (overflow > overflowPenalty.Threshold) total -= overflow * 2;
            }
            return total;
        }
        bool MeetsSp(List<SupportCard> cards)
        {
            if (spCounts == null) return true;
            foreach (var kvp in spCounts)
            {
                if (kvp.Value <= 0) continue;
                if (cards.Count(c => CoversStat(c, kvp.Key)) < kvp.Value) return false;
            }
            return true;
        }

        bool improved = true;
        int guard = 0;
        while (improved && guard++ < 3)
        {
            improved = false;
            var rentalIdsNow = new HashSet<string>(selected.Where(s => s.IsRental).Select(s => s.Card.Id));
            int baseTotal = EvalReal(selected.Select(s => s.Card).ToList(), rentalIdsNow);
            var inDeck = new HashSet<string>(selected.Select(s => s.Card.Id));

            var promising = cardContributions
                .Where(c => !inDeck.Contains(c.Card.Id) && (HasSpRate(c.Card) || IsProducer(c.Card)))
                .OrderByDescending(RawTotal)
                .Take(8)
                .ToList();

            foreach (var cand in promising)
            {
                string? candSp = HasSpRate(cand.Card) ? SpStat(cand.Card) : null;
                int slotIdx = -1;
                int weakest = int.MaxValue;
                if (candSp != null)
                {
                    // 同属性SPの保護枠のうち最弱を置換 → SP枚数を維持
                    for (int i = 0; i < selected.Count; i++)
                    {
                        var s = selected[i];
                        if (s.IsRental || s.IsRequired) continue;
                        if (protectedIds.Contains(s.Card.Id) && HasSpRate(s.Card) && SpStat(s.Card) == candSp)
                        {
                            int r = RawTotal(s);
                            if (r < weakest) { weakest = r; slotIdx = i; }
                        }
                    }
                }
                if (slotIdx < 0)
                {
                    // 非保護の最弱枠を置換
                    weakest = int.MaxValue;
                    for (int i = 0; i < selected.Count; i++)
                    {
                        var s = selected[i];
                        if (s.IsRental || s.IsRequired || protectedIds.Contains(s.Card.Id)) continue;
                        int r = RawTotal(s);
                        if (r < weakest) { weakest = r; slotIdx = i; }
                    }
                }
                if (slotIdx < 0) continue;

                var victim = selected[slotIdx];
                var trial = new List<CardScore>(selected);
                trial[slotIdx] = cand;
                var trialProtected = new HashSet<string>(protectedIds);
                if (protectedIds.Contains(victim.Card.Id)) trialProtected.Remove(victim.Card.Id);
                if (candSp != null) trialProtected.Add(cand.Card.Id);

                if (cardTypeSlots != null && !MeetsTypeSlots(trial.Select(s => s.Card).ToList(), cardTypeSlots)) continue;
                if (!MeetsSp(trial.Select(s => s.Card).ToList())) continue;

                // 投入した状態でレンタルを再最適化 (同時手)
                OptimizeRentalCard(trial, rentalPool, planType, triggerCounts, lessonAllocation, lessonStatTotals,
                    uncapLevels, triggerBonusInfo, trialProtected, spCounts, plan, additionalCounts,
                    statCap, character, memoryBonuses, cardTypeSlots, turnChoices, overflowPenalty);

                var trialRentalIds = new HashSet<string>(trial.Where(s => s.IsRental).Select(s => s.Card.Id));
                int trialTotal = EvalReal(trial.Select(s => s.Card).ToList(), trialRentalIds);

                if (trialTotal > baseTotal)
                {
                    selected.Clear();
                    selected.AddRange(trial);
                    protectedIds.Clear();
                    foreach (var id in trialProtected) protectedIds.Add(id);
                    improved = true;
                    break;
                }
            }
        }
    }

    private void PostOptimize(
        List<CardScore> selected,
        List<CardScore> candidates,
        HashSet<string> protectedIds,
        TrainingPlan plan,
        Dictionary<string, int> lessonAllocation,
        List<string> mainStats,
        Dictionary<string, int>? uncapLevels,
        AdditionalCounts? additionalCounts,
        int statCap,
        Character? character = null,
        IReadOnlyList<MemoryBonus>? memoryBonuses = null,
        Dictionary<string, int>? cardTypeSlots = null,
        List<TurnChoice>? turnChoicesOverride = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        var calcService = new StatusCalculationService();
        // HIFモードのようにユーザが明示的にターン選択している場合は実選択を使う
        var turnChoices = turnChoicesOverride ?? BuildTurnChoices(plan, mainStats);

        (int total, int vo, int da, int vi) EvaluateFull(List<SupportCard> cards)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in selected.Where(c => c.IsRental))
                uc[cs.Card.Id] = 4;
            // 最終表示値と一致させるため、キャラ補正・メモリーボーナスを含めて評価
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int cappedVo = Math.Min(fs.Vo, statCap);
            int cappedDa = Math.Min(fs.Da, statCap);
            int cappedVi = Math.Min(fs.Vi, statCap);
            int total = cappedVo + cappedDa + cappedVi;
            // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則を適用
            if (overflowPenalty != null)
            {
                int overflow = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (overflow > overflowPenalty.Threshold)
                {
                    total -= overflow * 2;
                }
            }
            return (total, fs.Vo, fs.Da, fs.Vi);
        }

        bool improved;
        do
        {
            improved = false;
            var currentCards = selected.Select(c => c.Card).ToList();
            var currentEval = EvaluateFull(currentCards);

            foreach (var ownedCard in selected.Where(c => !c.IsRental).ToList())
            {
                // 必須カードは無条件でスワップ不可
                if (ownedCard.IsRequired) continue;

                bool ownedIsProtectedSp = protectedIds.Contains(ownedCard.Card.Id)
                    && ownedCard.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
                bool ownedIsProtectedNonSp = protectedIds.Contains(ownedCard.Card.Id) && !ownedIsProtectedSp;
                // 非SPの保護カードはスキップ
                if (ownedIsProtectedNonSp) continue;

                var ownedType = ownedCard.Card.Type;
                string? ownedSpStat = ownedIsProtectedSp
                    ? ownedCard.Card.Effects.FirstOrDefault(e => e.Trigger == "equip" && e.ValueType == "sp_rate")?.Stat
                    : null;

                foreach (var candidate in candidates)
                {
                    if (selected.Any(c => c.Card.Id == candidate.Card.Id)) continue;

                    // SP率で保護されたカードは、同じ属性のSP率を持つ候補とのみ交換可能
                    // (ユーザ指定の spCounts 分布を PostOptimize で崩さないため)
                    if (ownedIsProtectedSp)
                    {
                        var candStat = candidate.Card.Effects.FirstOrDefault(
                            e => e.Trigger == "equip" && e.ValueType == "sp_rate")?.Stat;
                        if (candStat == null || candStat != ownedSpStat) continue;
                    }

                    var testCards = new List<SupportCard>(currentCards);
                    int idx = testCards.IndexOf(ownedCard.Card);
                    testCards[idx] = candidate.Card;

                    // タイプ制約: cardTypeSlots の最低要件 (例: Da 2枚以上) を満たすスワップのみ許可
                    if (candidate.Card.Type != ownedType
                        && candidate.Card.Type != "all" && candidate.Card.Type != "as"
                        && ownedType != "all" && ownedType != "as"
                        && cardTypeSlots != null
                        && !MeetsTypeSlots(testCards, cardTypeSlots))
                    {
                        continue;
                    }

                    var testEval = EvaluateFull(testCards);
                    // 合計値が同点の場合、raw_total (キャップ前の素の寄与) が大きいカードを優先。
                    // 両方がキャップを張り付かせる場合に「より強いSSR」を採用するためのタイブレーカ。
                    int candRawTotal = candidate.RawVo + candidate.RawDa + candidate.RawVi;
                    int ownedRawTotal = ownedCard.RawVo + ownedCard.RawDa + ownedCard.RawVi;
                    bool isImprovement =
                        testEval.total > currentEval.total ||
                        (testEval.total == currentEval.total && candRawTotal > ownedRawTotal);
                    if (isImprovement)
                    {
                        int selIdx = selected.IndexOf(ownedCard);
                        selected[selIdx] = candidate;
                        // SP率保護を新カードに引き継ぐ
                        if (ownedIsProtectedSp)
                        {
                            protectedIds.Remove(ownedCard.Card.Id);
                            protectedIds.Add(candidate.Card.Id);
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

    /// <summary>
    /// プランとメイン属性からターン選択を生成する。
    /// </summary>
    internal static List<TurnChoice> BuildTurnChoices(TrainingPlan plan, List<string> mainStats)
    {
        var choices = new List<TurnChoice>();
        var subStat = new[] { "vo", "da", "vi" }.First(s => !mainStats.Contains(s));

        static ActionType LessonAction(string stat) => stat switch
        {
            "vo" => ActionType.VoLesson,
            "da" => ActionType.DaLesson,
            _ => ActionType.ViLesson
        };
        static ActionType ClassAction(string stat) => stat switch
        {
            "vo" => ActionType.VoClass,
            "da" => ActionType.DaClass,
            _ => ActionType.ViClass
        };

        var main1Action = LessonAction(mainStats[0]);
        var main2Action = mainStats.Count > 1 ? LessonAction(mainStats[1]) : main1Action;
        var subClassAction = ClassAction(subStat);

        int midExamWeek = plan.Schedule
            .Where(w => w.IsFixedEvent && w.EventName == "中間試験")
            .Select(w => w.Week)
            .FirstOrDefault();
        if (midExamWeek == 0) midExamWeek = 10;

        var lessonWeeks = plan.Schedule
            .Where(w => !w.IsFixedEvent && w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        // 中間前: 交互
        bool toggle = false;
        foreach (var w in lessonWeeks.Where(w => w.Week < midExamWeek))
        {
            choices.Add(new TurnChoice { Week = w.Week, ChosenAction = toggle ? main2Action : main1Action });
            toggle = !toggle;
        }

        // 中間後: メイン1:メイン2 = 2:1
        int afterCount = 0;
        foreach (var w in lessonWeeks.Where(w => w.Week > midExamWeek))
        {
            choices.Add(new TurnChoice { Week = w.Week, ChosenAction = (afterCount % 3 == 1) ? main2Action : main1Action });
            afterCount++;
        }

        // 非レッスン週
        foreach (var w in plan.Schedule)
        {
            if (w.IsFixedEvent || w.Lessons.Count > 0) continue;
            var actions = w.AvailableActions;

            bool hasClass = actions.Any(a => a.Contains("class"));
            if (hasClass)
            {
                var subClassStr = subStat + "_class";
                if (actions.Contains(subClassStr))
                    choices.Add(new TurnChoice { Week = w.Week, ChosenAction = subClassAction });
                else
                {
                    var mainClassStr = mainStats[0] + "_class";
                    if (actions.Contains(mainClassStr))
                        choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ClassAction(mainStats[0]) });
                }
            }
            else if (actions.Contains("activity_supply"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.ActivitySupply });
            else if (actions.Contains("outing"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.Outing });
            else if (actions.Contains("consultation"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.Consultation });
            else if (actions.Contains("special_training"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.SpecialTraining });
        }

        return choices;
    }

    /// <summary>
    /// キャップを考慮して最も有効なカードを選択する。
    /// 各候補について、追加した場合のキャップ後合計の増分が最大のものを選ぶ。
    /// </summary>
    private CardScore? SelectBestCard(
        List<CardScore> candidates,
        HashSet<string> usedIds,
        int currentVo, int currentDa, int currentVi,
        int statCap = DEFAULT_STAT_CAP,
        Character? character = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        CardScore? best = null;
        int bestGain = int.MinValue;

        // キャラの para_bonus はカード貢献にも乗る (calculate 時)
        double voMul = 1.0 + (character?.ParaBonus.Vo ?? 0) / 100.0;
        double daMul = 1.0 + (character?.ParaBonus.Da ?? 0) / 100.0;
        double viMul = 1.0 + (character?.ParaBonus.Vi ?? 0) / 100.0;

        // overflow罰則を適用するなら現在の overflow を計算
        int overflowCurrent = overflowPenalty != null
            ? Math.Max(0, currentVo - statCap) + Math.Max(0, currentDa - statCap) + Math.Max(0, currentVi - statCap)
            : 0;

        foreach (var cs in candidates)
        {
            if (usedIds.Contains(cs.Card.Id)) continue;

            int rawNewVo = currentVo + (int)(cs.RawVo * voMul);
            int rawNewDa = currentDa + (int)(cs.RawDa * daMul);
            int rawNewVi = currentVi + (int)(cs.RawVi * viMul);

            // キャップ適用後の実効増分 (合計stat)
            int cappedNewSum = Math.Min(rawNewVo, statCap) + Math.Min(rawNewDa, statCap) + Math.Min(rawNewVi, statCap);
            int cappedCurrentSum = Math.Min(currentVo, statCap) + Math.Min(currentDa, statCap) + Math.Min(currentVi, statCap);
            int gain = cappedNewSum - cappedCurrentSum;

            // overflow罰則: ピック後の合計overflowが閾値を超える場合のみ、追加overflow分を × 2 罰則
            if (overflowPenalty != null)
            {
                int overflowNew =
                    Math.Max(0, rawNewVo - statCap) + Math.Max(0, rawNewDa - statCap) + Math.Max(0, rawNewVi - statCap);
                if (overflowNew > overflowPenalty.Threshold)
                {
                    int newOverflow = Math.Max(0, overflowNew - overflowCurrent);
                    gain -= newOverflow * 2;
                }
            }

            if (gain > bestGain)
            {
                bestGain = gain;
                best = cs;
            }
        }

        return best;
    }

    /// <summary>
    /// チェックポイント状態からグリーディに所持枠を埋める。
    /// </summary>
    private (List<CardScore> Selected, HashSet<string> UsedIds, int AccVo, int AccDa, int AccVi)
        GreedyFillOwned(
            List<CardScore> contributions,
            List<CardScore> selectedInit,
            HashSet<string> usedIdsInit,
            int accVoInit, int accDaInit, int accViInit,
            Dictionary<string, int> remainingSlotsInit,
            int remainingFreeInit,
            int ownedSlots,
            int statCap,
            Character? character = null,
            OverflowPenaltyConfig? overflowPenalty = null)
    {
        var sel = new List<CardScore>(selectedInit);
        var used = new HashSet<string>(usedIdsInit);
        int aVo = accVoInit, aDa = accDaInit, aVi = accViInit;
        double voMul = 1.0 + (character?.ParaBonus.Vo ?? 0) / 100.0;
        double daMul = 1.0 + (character?.ParaBonus.Da ?? 0) / 100.0;
        double viMul = 1.0 + (character?.ParaBonus.Vi ?? 0) / 100.0;

        // 属性枠
        foreach (var kvp in remainingSlotsInit.OrderByDescending(k => k.Value))
        {
            var type = kvp.Key;
            int count = kvp.Value;
            if (count <= 0) continue;

            var candidates = contributions
                .Where(cs => (cs.Card.Type == type || cs.Card.Type == "all" || cs.Card.Type == "as")
                             && !used.Contains(cs.Card.Id))
                .ToList();

            for (int i = 0; i < count && sel.Count < ownedSlots; i++)
            {
                var best = SelectBestCard(candidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += (int)(best.RawVo * voMul);
                aDa += (int)(best.RawDa * daMul);
                aVi += (int)(best.RawVi * viMul);
            }
        }

        // フリー枠
        for (int i = 0; i < remainingFreeInit && sel.Count < ownedSlots; i++)
        {
            var freeCandidates = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            var best = SelectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
            if (best == null) break;
            sel.Add(best);
            used.Add(best.Card.Id);
            aVo += (int)(best.RawVo * voMul);
            aDa += (int)(best.RawDa * daMul);
            aVi += (int)(best.RawVi * viMul);
        }

        // 補充
        if (sel.Count < ownedSlots)
        {
            var remaining = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            while (sel.Count < ownedSlots)
            {
                var best = SelectBestCard(remaining, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += (int)(best.RawVo * voMul);
                aDa += (int)(best.RawDa * daMul);
                aVi += (int)(best.RawVi * viMul);
            }
        }

        return (sel, used, aVo, aDa, aVi);
    }

    /// <summary>
    /// カードリスト＋レンタル1枚のキャップ適用後の合計ステータスを算出する。
    /// スワップ検証用。
    /// </summary>
    private int CalculateCappedTotal(StatusValues baseStats, List<CardScore> owned, CardScore? rental, int statCap)
    {
        int vo = baseStats.Vo, da = baseStats.Da, vi = baseStats.Vi;
        foreach (var cs in owned)
        {
            vo += cs.RawVo;
            da += cs.RawDa;
            vi += cs.RawVi;
        }
        if (rental != null)
        {
            vo += rental.RawVo;
            da += rental.RawDa;
            vi += rental.RawVi;
        }
        return Math.Min(vo, statCap) + Math.Min(da, statCap) + Math.Min(vi, statCap);
    }

    /// <summary>
    /// 選択完了後、キャップ適用後の実効TotalValueを再計算する。
    /// </summary>
    private void RecalculateWithCap(List<CardScore> selected, StatusValues baseStats, int statCap = DEFAULT_STAT_CAP)
    {
        // カード無しのベースステータスから順に積み上げてキャップ適用
        int accVo = baseStats.Vo, accDa = baseStats.Da, accVi = baseStats.Vi;

        foreach (var cs in selected)
        {
            int prevTotal = Math.Min(accVo, statCap) + Math.Min(accDa, statCap) + Math.Min(accVi, statCap);

            accVo += cs.RawVo;
            accDa += cs.RawDa;
            accVi += cs.RawVi;

            int newTotal = Math.Min(accVo, statCap) + Math.Min(accDa, statCap) + Math.Min(accVi, statCap);

            cs.TotalValue = newTotal - prevTotal;
        }
    }

    /// <summary>
    /// カード無しのベースステータス推定（レッスン＋授業＋イベント等の基礎値）
    /// </summary>
    private StatusValues EstimateBaseStats(TrainingPlan plan, Dictionary<string, int> lessonAllocation)
    {
        int vo = 0, da = 0, vi = 0;

        // レッスンのSPパーフェクト基礎値を配分に従って加算
        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        // 各属性のレッスン回数分、後ろの週(高い値)から割り当て
        var weekQueue = new Queue<WeekSchedule>(lessonWeeks.OrderByDescending(w => w.Week));

        foreach (var stat in lessonAllocation.OrderByDescending(kv => kv.Value))
        {
            int count = stat.Value;
            var tempWeeks = new List<WeekSchedule>();

            // キューから取り出して割り当て
            for (int i = 0; i < count && weekQueue.Count > 0; i++)
            {
                var w = weekQueue.Dequeue();
                var lesson = w.GetLesson(stat.Key);
                if (lesson != null)
                {
                    vo += lesson.SpBonus.Vo;
                    da += lesson.SpBonus.Da;
                    vi += lesson.SpBonus.Vi;
                }
            }
        }

        // 授業の基礎値（メイン属性に全額配分と仮定）
        foreach (var week in plan.Schedule)
        {
            if (week.Classes.Count > 0)
            {
                // 最大値の授業を加算
                var bestClass = week.Classes.OrderByDescending(c => c.SpBonus.Total).First();
                vo += bestClass.SpBonus.Vo;
                da += bestClass.SpBonus.Da;
                vi += bestClass.SpBonus.Vi;
            }

            // 固定イベント
            if (week.IsFixedEvent && week.StatusGain != null)
            {
                vo += week.StatusGain.Vo;
                da += week.StatusGain.Da;
                vi += week.StatusGain.Vi;
            }
        }

        return new StatusValues(vo, da, vi);
    }

    public List<DeckResult> SelectMultiplePatterns(
        TrainingPlan plan,
        List<SupportCard> allCards,
        List<string> mainStats,
        string subStat,
        int totalLessonWeeks,
        Dictionary<string, int>? spCounts = null,
        string? planType = null,
        AdditionalCounts? additionalCounts = null,
        Dictionary<string, int>? uncapLevels = null,
        List<SupportCard>? rentalPool = null,
        List<string>? requiredCardIds = null)
    {
        var results = new List<DeckResult>();

        if (mainStats.Count < 2) return results;

        var main1 = mainStats[0];
        var main2 = mainStats[1];

        // SP率カードの必要枚数を属性別に集計
        int spMain1 = spCounts?.GetValueOrDefault(main1) ?? 0;
        int spMain2 = spCounts?.GetValueOrDefault(main2) ?? 0;
        int spSub = spCounts?.GetValueOrDefault(subStat) ?? 0;

        // カード枚数パターン (メイン1:メイン2:フリー枠 = 合計6枚)
        var patterns = new List<(int m1, int m2, int free)>
        {
            (3, 2, 1),
            (2, 3, 1),
            (3, 3, 0),
            (2, 2, 2),
            (0, 0, 5),  // フリー5 + サブ1 (サブはcardTypeSlotsで指定)
        };

        foreach (var (m1, m2, free) in patterns)
        {
            // レンタルモード(所持5+レンタル1)では、フリー枠なし6枚パターンは
            // 属性枠が所持枠(5)を超えるため [3,2,1] / [2,3,1] と重複する → スキップ
            if (rentalPool != null && free == 0 && m1 + m2 > 5) continue;

            // SP枚数を満たせないパターンはスキップ (フリー枠でSP率カードを吸収できる場合はOK)
            int spShortage = Math.Max(0, spMain1 - m1) + Math.Max(0, spMain2 - m2);
            if (spShortage > free) continue;

            // カード枚数
            var cardTypeSlots = new Dictionary<string, int>();
            if (m1 > 0) cardTypeSlots[main1] = m1;
            if (m2 > 0) cardTypeSlots[main2] = m2;
            int freeSlots = free;

            // フリー5パターン: サブ属性1枚を固定枠に追加
            if (m1 == 0 && m2 == 0)
            {
                cardTypeSlots[subStat] = 1;
                freeSlots = 5;
            }

            // レッスン配分: メイン1のレッスン回数が多い
            var lessonAllocation = new Dictionary<string, int>
            {
                [main1] = 0,
                [main2] = 0,
                [subStat] = 0
            };
            int remaining = totalLessonWeeks;
            lessonAllocation[main1] += remaining - remaining / 2;
            lessonAllocation[main2] += remaining / 2;

            var result = SelectOptimalDeck(
                plan, allCards, lessonAllocation, cardTypeSlots,
                mainStats, spCounts, planType, additionalCounts, uncapLevels, rentalPool, freeSlots, requiredCardIds);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// HIFモード専用のパターン選出。メイン/サブの概念を捨て、
    /// Vo/Da/Vi 各属性で「2枚 + フリー3」とオールフリー の合計4パターンを生成する。
    /// lessonAllocation はユーザのスケジュールから集計した実際のレッスン回数を渡す。
    /// </summary>
    public List<DeckResult> SelectMultiplePatternsHif(
        TrainingPlan plan,
        List<SupportCard> allCards,
        List<string> mainStats,
        Dictionary<string, int> lessonAllocation,
        Dictionary<string, int>? spCounts = null,
        string? planType = null,
        AdditionalCounts? additionalCounts = null,
        Dictionary<string, int>? uncapLevels = null,
        List<SupportCard>? rentalPool = null,
        List<string>? requiredCardIds = null,
        Character? character = null,
        IReadOnlyList<MemoryBonus>? memoryBonuses = null,
        List<TurnChoice>? turnChoicesOverride = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        var results = new List<DeckResult>();

        // HIFパターン: 属性別2枚+フリー3 と オールフリー
        var patterns = new List<(string? stat, int count, int free)>
        {
            ("vo", 2, 3),
            ("da", 2, 3),
            ("vi", 2, 3),
            (null, 0, 5), // オールフリー
        };

        foreach (var (stat, count, free) in patterns)
        {
            var cardTypeSlots = new Dictionary<string, int>();
            if (stat != null && count > 0)
            {
                cardTypeSlots[stat] = count;
            }

            // SP率カードの不足をフリー枠で吸収できるかチェック
            int spShortage = 0;
            foreach (var s in new[] { "vo", "da", "vi" })
            {
                int required = spCounts?.GetValueOrDefault(s) ?? 0;
                int provided = cardTypeSlots.GetValueOrDefault(s);
                spShortage += Math.Max(0, required - provided);
            }
            if (spShortage > free) continue;

            var result = SelectOptimalDeck(
                plan, allCards, lessonAllocation, cardTypeSlots,
                mainStats, spCounts, planType, additionalCounts, uncapLevels, rentalPool, free, requiredCardIds,
                character, memoryBonuses, turnChoicesOverride, overflowPenalty);
            results.Add(result);
        }

        // cross-seed 大域最適化: 各パターンの greedy は属性偏重の局所最適へ収束しやすく、特に型制約の
        // ない「フリー5」は Da偏重 basin に落ちて balanced 最適へ単一スワップで渡れないことがある。
        // 一方 Vo/Vi 偏重パターンのデッキを種に、型制約なし(SP枚数+必須のみ)で joint 単一スワップ
        // 山登りすると balanced 最適へ届く。全パターンのデッキを種に山登りし、得た大域最良を
        // 「フリー5」枠へ反映する (現フリー5を上回る場合のみ・単調改善)。
        CrossSeedFreeDeck(
            results, plan, allCards, lessonAllocation, mainStats, spCounts, planType,
            additionalCounts, uncapLevels, rentalPool, requiredCardIds,
            character, memoryBonuses, turnChoicesOverride, overflowPenalty);

        return results;
    }

    /// <summary>
    /// HIFパターン群の「フリー5」枠を、全パターンのデッキを種にした joint 単一スワップ山登りで
    /// 求めた大域最良デッキに置き換える (改善時のみ)。属性偏重 greedy の basin を跨いで
    /// balanced 最適を拾うための cross-seed。制約は SP枚数 + 必須カードのみ (フリー5は型制約なし)。
    /// レンタル枠は1枚を4凸借用として評価する (rentalPool 指定時)。
    /// </summary>
    private void CrossSeedFreeDeck(
        List<DeckResult> results,
        TrainingPlan plan,
        List<SupportCard> ownedCards,
        Dictionary<string, int> lessonAllocation,
        List<string> mainStats,
        Dictionary<string, int>? spCounts,
        string? planType,
        AdditionalCounts? additionalCounts,
        Dictionary<string, int>? uncapLevels,
        List<SupportCard>? rentalPool,
        List<string>? requiredCardIds,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses,
        List<TurnChoice>? turnChoicesOverride,
        OverflowPenaltyConfig? overflowPenalty)
    {
        if (results.Count == 0) return;
        var statCap = plan.StatusLimit;
        var freeLabel = GenerateLabel(new Dictionary<string, int>(), 5);
        int freeIdx = results.FindIndex(r => r.Label == freeLabel);
        if (freeIdx < 0) return;

        var turnChoices = turnChoicesOverride ?? BuildTurnChoices(plan, mainStats);
        var requiredSet = (requiredCardIds ?? new List<string>()).ToHashSet();

        // 共有コンテキスト (raw寄与ランキング & 最終 DeckResult 生成で使用)
        var triggerCounts = CountTriggers(plan, lessonAllocation, mainStats, turnChoicesOverride);
        if (additionalCounts != null)
        {
            foreach (var kvp in additionalCounts.ToDictionary())
                if (kvp.Value > 0)
                    triggerCounts[kvp.Key] = triggerCounts.GetValueOrDefault(kvp.Key) + kvp.Value;
        }
        var baseStats = EstimateBaseStats(plan, lessonAllocation);
        var lessonStatTotals = CalculateLessonStatTotals(plan, lessonAllocation);
        var triggerBonusInfo = ComputeTriggerBonusInfo(ownedCards, uncapLevels);

        bool PlanOk(SupportCard c) =>
            string.IsNullOrEmpty(planType) || string.IsNullOrEmpty(c.Plan) || c.Plan == planType || c.Plan == "free";

        // 所持枠 / レンタル枠の候補を raw寄与上位に絞る (計算量削減)
        const int TopN = 40;
        int RawTotalOf(SupportCard c, bool asRental)
        {
            var uc = uncapLevels != null ? new Dictionary<string, int>(uncapLevels) : new Dictionary<string, int>();
            if (asRental) uc[c.Id] = 4;
            var cs = CalculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, uc, triggerBonusInfo);
            return cs.RawVo + cs.RawDa + cs.RawVi;
        }
        List<SupportCard> RankTopN(List<SupportCard> pool, bool asRental) =>
            pool.Where(PlanOk).OrderByDescending(c => RawTotalOf(c, asRental)).Take(TopN).ToList();
        var ownedRanked = RankTopN(ownedCards, false);
        var rentalRanked = RankTopN(rentalPool ?? ownedCards, true);

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));
        bool MeetsSp(List<SupportCard> cards)
        {
            if (spCounts == null) return true;
            foreach (var kvp in spCounts)
            {
                if (kvp.Value <= 0) continue;
                if (cards.Count(c => CoversStat(c, kvp.Key)) < kvp.Value) return false;
            }
            return true;
        }
        bool HasRequired(List<SupportCard> cards)
        {
            foreach (var id in requiredSet)
                if (!cards.Any(c => c.Id == id)) return false;
            return true;
        }

        var calcService = new StatusCalculationService();
        int EvalTotal(List<SupportCard> cards, int rentalIdx)
        {
            var uc = uncapLevels != null ? new Dictionary<string, int>(uncapLevels) : new Dictionary<string, int>();
            if (rentalIdx >= 0) uc[cards[rentalIdx].Id] = 4;
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, character, memoryBonuses).FinalStatus;
            int total = Math.Min(fs.Vo, statCap) + Math.Min(fs.Da, statCap) + Math.Min(fs.Vi, statCap);
            if (overflowPenalty != null)
            {
                int o = Math.Max(0, fs.Vo - statCap) + Math.Max(0, fs.Da - statCap) + Math.Max(0, fs.Vi - statCap);
                if (o > overflowPenalty.Threshold) total -= o * 2;
            }
            return total;
        }

        // joint 単一スワップ山登り (所持枠は ownedRanked, レンタル枠は rentalRanked から; 改善時のみ採用)
        (List<SupportCard> cards, int total) HillClimb(List<SupportCard> start, int rentalIdx)
        {
            var cur = new List<SupportCard>(start);
            int curTotal = EvalTotal(cur, rentalIdx);
            bool improved = true;
            int guard = 0;
            while (improved && guard++ < 20)
            {
                improved = false;
                for (int slot = 0; slot < cur.Count; slot++)
                {
                    if (requiredSet.Contains(cur[slot].Id)) continue; // 必須カードは固定
                    var pool = slot == rentalIdx ? rentalRanked : ownedRanked;
                    foreach (var cand in pool)
                    {
                        bool dup = false;
                        for (int i = 0; i < cur.Count; i++)
                            if (i != slot && cur[i].Id == cand.Id) { dup = true; break; }
                        if (dup) continue;
                        var trial = new List<SupportCard>(cur) { [slot] = cand };
                        if (!MeetsSp(trial) || !HasRequired(trial)) continue;
                        int t = EvalTotal(trial, rentalIdx);
                        if (t > curTotal) { cur = trial; curTotal = t; improved = true; }
                    }
                }
            }
            return (cur, curTotal);
        }

        // 全パターンのデッキを種に大域最良を探索
        List<SupportCard>? bestCards = null;
        int bestRentalIdx = -1;
        int bestTotal = int.MinValue;
        foreach (var r in results)
        {
            var cards = r.SelectedCards.Select(cs => cs.Card).ToList();
            int rentalIdx = r.SelectedCards.FindIndex(cs => cs.IsRental);
            var (hcCards, hcTotal) = HillClimb(cards, rentalIdx);
            if (hcTotal > bestTotal)
            {
                bestTotal = hcTotal;
                bestCards = hcCards;
                bestRentalIdx = rentalIdx;
            }
        }
        if (bestCards == null) return;

        var free = results[freeIdx];
        int freeRentalIdx = free.SelectedCards.FindIndex(cs => cs.IsRental);
        int freeTotal = EvalTotal(free.SelectedCards.Select(cs => cs.Card).ToList(), freeRentalIdx);
        if (bestTotal <= freeTotal) return; // 改善なし

        // 大域最良デッキを DeckResult 化 (SelectOptimalDeckOnce の確定処理と同一手順)
        string? rentalId = bestRentalIdx >= 0 ? bestCards[bestRentalIdx].Id : null;
        var ucEff = uncapLevels != null ? new Dictionary<string, int>(uncapLevels) : new Dictionary<string, int>();
        if (rentalId != null) ucEff[rentalId] = 4;
        var selected = bestCards.Select(card =>
        {
            var cs = CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, ucEff, triggerBonusInfo);
            cs.IsRental = card.Id == rentalId;
            cs.IsRequired = requiredSet.Contains(card.Id);
            return cs;
        }).ToList();

        // HillClimb はレンタル枠の位置を固定したままカードを入れ替えるため、最終デッキで
        // レンタルが最低凸カードに乗っていないことがある。借用先を実計算で最適化する
        // (同点時は最低凸カードを優先。SelectOptimalDeck の確定処理と挙動を揃える)。
        if (rentalPool != null)
        {
            OptimizeRentalAssignment(selected, ownedCards.Select(c => c.Id).ToHashSet(), plan,
                turnChoices, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels, triggerBonusInfo,
                additionalCounts, statCap, character, memoryBonuses, overflowPenalty);
        }

        RecomputeBreakdownsDeckAware(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels);
        RecalculateWithCap(selected, baseStats, statCap);
        selected = selected.OrderByDescending(cs => cs.TotalValue).ToList();
        results[freeIdx] = new DeckResult
        {
            Label = free.Label,
            SelectedCards = selected,
        };
    }

    /// <summary>trigger_count_bonus 用、消費側カード1枚分の per-fire 寄与情報</summary>
    public class TriggerBonusContributor
    {
        public string CardId { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public StatusValues PerFire { get; set; } = StatusValues.Zero;
    }

    /// <summary>trigger_count_bonus 用、対象トリガーごとの集計情報</summary>
    public class TriggerBonusEntry
    {
        public StatusValues Total { get; set; } = StatusValues.Zero;
        public List<TriggerBonusContributor> Contributors { get; set; } = new();
    }

    /// <summary>
    /// trigger_count_bonus 効果の単体スコアリングのため、対象トリガーごとに
    /// プール内全ての消費側カードの per-fire ステータスを事前計算する。
    /// </summary>
    private static Dictionary<string, TriggerBonusEntry> ComputeTriggerBonusInfo(
        List<SupportCard> pool,
        Dictionary<string, int>? uncapLevels)
    {
        var targets = new HashSet<string>();
        foreach (var card in pool)
        {
            foreach (var effect in card.Effects)
            {
                if (effect.ValueType == "trigger_count_bonus" && !string.IsNullOrEmpty(effect.TriggerTarget))
                {
                    targets.Add(effect.TriggerTarget);
                }
            }
        }

        var result = new Dictionary<string, TriggerBonusEntry>();
        foreach (var target in targets)
        {
            var candidates = new List<(TriggerBonusContributor c, double total)>();
            foreach (var card in pool)
            {
                int uncap = StatusCalculationService.GetUncapLevel(card, uncapLevels);
                int cVo = 0, cDa = 0, cVi = 0;
                foreach (var effect in card.Effects)
                {
                    if (effect.Trigger != target || effect.ValueType != "flat") continue;
                    int v = (int)Math.Floor(effect.GetValue(uncap));
                    switch (effect.Stat)
                    {
                        case "vo": cVo += v; break;
                        case "da": cDa += v; break;
                        case "vi": cVi += v; break;
                        case "all": cVo += v; cDa += v; cVi += v; break;
                    }
                }
                int total = cVo + cDa + cVi;
                if (total > 0)
                {
                    candidates.Add((new TriggerBonusContributor
                    {
                        CardId = card.Id,
                        CardName = card.Name,
                        PerFire = new StatusValues(cVo, cDa, cVi),
                    }, total));
                }
            }
            candidates.Sort((a, b) => b.total.CompareTo(a.total));
            result[target] = new TriggerBonusEntry
            {
                Total = new StatusValues(
                    candidates.Sum(x => x.c.PerFire.Vo),
                    candidates.Sum(x => x.c.PerFire.Da),
                    candidates.Sum(x => x.c.PerFire.Vi)),
                Contributors = candidates.Select(x => x.c).ToList(),
            };
        }
        return result;
    }

    /// <summary>
    /// カード1枚の属性別寄与を計算
    /// </summary>
    private CardScore CalculateCardContribution(
        SupportCard card,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo = null,
        bool skipTriggerBonusSelfContribution = false)
    {
        int uncap = StatusCalculationService.GetUncapLevel(card, uncapLevels);
        double vo = 0, da = 0, vi = 0;
        double teamBonusTotal = 0;
        var teamBonusContributors = new List<TeamBonusContributor>();
        var breakdowns = new List<EffectBreakdown>();

        foreach (var effect in card.Effects)
        {
            // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
            if (effect.ValueType == "sp_rate") continue;

            // trigger_count_bonus: 自カードは追加でステータスを得ないが、他カードのトリガー発火回数を増やす
            if (effect.ValueType == "trigger_count_bonus")
            {
                var target = effect.TriggerTarget;
                if (string.IsNullOrEmpty(target)) continue;
                double perScale = effect.GetValue(uncap);
                int scaleCount = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? triggerCounts.GetValueOrDefault(effect.ScalesWith)
                    : 1;
                double bonus = perScale * scaleCount;
                if (effect.MaxCount.HasValue) bonus = Math.Min(bonus, effect.MaxCount.Value);
                int bonusFires = (int)Math.Floor(bonus);
                if (bonusFires <= 0) continue;

                if (triggerBonusInfo == null || !triggerBonusInfo.TryGetValue(target, out var entry)) continue;

                // 自カード除外で消費側カードを集計
                double synergyVoSum = 0, synergyDaSum = 0, synergyViSum = 0;
                var contribRows = new List<EffectBreakdown>();
                foreach (var c in entry.Contributors)
                {
                    if (c.CardId == card.Id) continue;
                    int cVo = c.PerFire.Vo * bonusFires;
                    int cDa = c.PerFire.Da * bonusFires;
                    int cVi = c.PerFire.Vi * bonusFires;
                    int cTotal = cVo + cDa + cVi;
                    if (cTotal <= 0) continue;
                    synergyVoSum += cVo;
                    synergyDaSum += cDa;
                    synergyViSum += cVi;
                    var parts = new List<string>();
                    if (c.PerFire.Vo > 0) parts.Add($"Vo+{c.PerFire.Vo}");
                    if (c.PerFire.Da > 0) parts.Add($"Da+{c.PerFire.Da}");
                    if (c.PerFire.Vi > 0) parts.Add($"Vi+{c.PerFire.Vi}");
                    var perFireDesc = string.Join("/", parts);
                    var mainStat = (cVo >= cDa && cVo >= cVi) ? "vo" : (cDa >= cVi ? "da" : "vi");
                    contribRows.Add(new EffectBreakdown
                    {
                        Reason = $"  ↳ {c.CardName} ({perFireDesc}/回)",
                        Stat = mainStat,
                        Value = Math.Round((double)cTotal, 1),
                    });
                    teamBonusContributors.Add(new TeamBonusContributor
                    {
                        CardName = c.CardName,
                        Value = cTotal,
                    });
                }
                if (contribRows.Count == 0) continue;

                teamBonusTotal += synergyVoSum + synergyDaSum + synergyViSum;
                if (!skipTriggerBonusSelfContribution)
                {
                    vo += synergyVoSum;
                    da += synergyDaSum;
                    vi += synergyViSum;
                }

                var targetName = TriggerDisplayName(target);
                var formula = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? $"{TriggerDisplayName(effect.ScalesWith)}×{scaleCount} × {perScale}"
                    : $"×{perScale}";
                var headerSuffix = skipTriggerBonusSelfContribution ? " → 他カードへ寄与" : "";
                breakdowns.Add(new EffectBreakdown
                {
                    Reason = $"[アイテム] {targetName}+{bonusFires}回 ({formula}){headerSuffix}",
                    Stat = "all",
                    Value = 0,
                });
                breakdowns.AddRange(contribRows);
                continue;
            }

            if (effect.ValueType == "para_bonus")
            {
                // パラボは該当属性のレッスン上昇値にのみ適用
                double pct = effect.GetValue(uncap) / 100.0;
                double bonus = 0;
                switch (effect.Stat)
                {
                    case "vo": bonus = lessonStatTotals.Vo * pct; vo += bonus; break;
                    case "da": bonus = lessonStatTotals.Da * pct; da += bonus; break;
                    case "vi": bonus = lessonStatTotals.Vi * pct; vi += bonus; break;
                    case "all":
                        double bVo = lessonStatTotals.Vo * pct;
                        double bDa = lessonStatTotals.Da * pct;
                        double bVi = lessonStatTotals.Vi * pct;
                        vo += bVo; da += bDa; vi += bVi;
                        bonus = bVo + bDa + bVi;
                        break;
                }

                if (Math.Abs(bonus) < 0.01) continue;

                var reason = $"パラボ({effect.Stat.ToUpper()})+{effect.GetValue(uncap)}%";
                breakdowns.Add(new EffectBreakdown
                {
                    Reason = reason,
                    Stat = effect.Stat,
                    Value = Math.Round(bonus, 1)
                });
                continue;
            }

            double value = effect.ValueType switch
            {
                "flat" => CalculateFlatValue(effect, triggerCounts, uncap, card),
                _ => 0
            };

            if (Math.Abs(value) < 0.01) continue;

            // 内訳の理由テキスト生成
            var reason2 = BuildReasonText(effect, triggerCounts, uncap, card);

            switch (effect.Stat)
            {
                case "vo": vo += value; break;
                case "da": da += value; break;
                case "vi": vi += value; break;
                case "all":
                    vo += value / 3.0;
                    da += value / 3.0;
                    vi += value / 3.0;
                    break;
                default:
                    vo += value / 3.0;
                    da += value / 3.0;
                    vi += value / 3.0;
                    break;
            }

            breakdowns.Add(new EffectBreakdown
            {
                Reason = reason2,
                Stat = effect.Stat,
                Value = Math.Round(value, 1)
            });
        }

        int iVo = (int)Math.Floor(vo);
        int iDa = (int)Math.Floor(da);
        int iVi = (int)Math.Floor(vi);

        return new CardScore
        {
            Card = card,
            RawVo = iVo,
            RawDa = iDa,
            RawVi = iVi,
            TeamBonusTotal = (int)Math.Floor(teamBonusTotal),
            TeamBonusContributors = teamBonusContributors,
            TotalValue = iVo + iDa + iVi,
            Breakdowns = breakdowns,
            UncapLevel = uncap
        };
    }

    /// <summary>
    /// デッキ確定後の deck-aware 再計算。
    /// - producer の trigger_count_bonus 効果による消費側カードへのバフ分を triggerCounts に加算
    /// - producer 側では trigger_count_bonus を raw_* に加算しない (二重カウント回避)
    /// - team_bonus_total はデッキ内 consumer のみを対象に計算
    /// </summary>
    private void RecomputeBreakdownsDeckAware(
        List<CardScore> selected,
        Dictionary<string, int> baseTriggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels)
    {
        // レンタル枠は所持凸数に依らず常に4凸として評価する
        var effectiveUncapLevels = uncapLevels != null
            ? new Dictionary<string, int>(uncapLevels)
            : new Dictionary<string, int>();
        foreach (var cs in selected)
        {
            if (cs.IsRental) effectiveUncapLevels[cs.Card.Id] = 4;
        }

        // 1. デッキ内 producer の trigger_count_bonus 集計
        var deckBonuses = new Dictionary<string, int>();
        foreach (var cs in selected)
        {
            int uncap = StatusCalculationService.GetUncapLevel(cs.Card, effectiveUncapLevels);
            foreach (var effect in cs.Card.Effects)
            {
                if (effect.ValueType != "trigger_count_bonus") continue;
                var target = effect.TriggerTarget;
                if (string.IsNullOrEmpty(target)) continue;
                double perScale = effect.GetValue(uncap);
                int scaleCount = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? baseTriggerCounts.GetValueOrDefault(effect.ScalesWith)
                    : 1;
                double bonus = perScale * scaleCount;
                if (effect.MaxCount.HasValue) bonus = Math.Min(bonus, effect.MaxCount.Value);
                int bonusFires = (int)Math.Floor(bonus);
                if (bonusFires > 0)
                {
                    deckBonuses[target] = deckBonuses.GetValueOrDefault(target) + bonusFires;
                }
            }
        }
        if (deckBonuses.Count == 0) return;

        // 2. adjustedCounts = base + producer-derived bonus
        var adjustedCounts = new Dictionary<string, int>(baseTriggerCounts);
        foreach (var kvp in deckBonuses)
        {
            adjustedCounts[kvp.Key] = adjustedCounts.GetValueOrDefault(kvp.Key) + kvp.Value;
        }

        // 3. デッキ内カードのみで TriggerBonusInfo を計算
        var deckCards = selected.Select(cs => cs.Card).ToList();
        var deckTriggerBonusInfo = ComputeTriggerBonusInfo(deckCards, effectiveUncapLevels);

        // 4. 各 selected card を再計算 (skipTriggerBonusSelfContribution=true)
        for (int i = 0; i < selected.Count; i++)
        {
            var cs = selected[i];
            var recomputed = CalculateCardContribution(
                cs.Card,
                adjustedCounts,
                lessonAllocation,
                lessonStatTotals,
                effectiveUncapLevels,
                deckTriggerBonusInfo,
                skipTriggerBonusSelfContribution: true);
            recomputed.IsRental = cs.IsRental;
            recomputed.IsRequired = cs.IsRequired;
            selected[i] = recomputed;
        }
    }

    private static string TriggerDisplayName(string trigger) => trigger switch
    {
        "equip" => "装備",
        "sp_end" => "SP終了",
        "lesson_end" => "レッスン終了",
        "class_end" => "授業終了",
        "outing_end" => "お出かけ終了",
        "consultation" => "相談",
        "activity_supply" => "活動支給",
        "exam_end" => "試験終了",
        "special_training" => "特別指導",
        "skill_acquire" => "スキル獲得",
        "skill_ssr_acquire" => "スキル(SSR)獲得",
        "skill_enhance" => "スキル強化",
        "skill_delete" => "スキル削除",
        "skill_custom" => "スキルカスタム",
        "skill_change" => "スキルチェンジ",
        "active_enhance" => "アクティブ強化",
        "active_delete" => "アクティブ削除",
        "mental_acquire" => "メンタル獲得",
        "mental_enhance" => "メンタル強化",
        "mental_delete" => "メンタル削除",
        "active_acquire" => "アクティブ獲得",
        "genki_acquire" => "元気獲得",
        "good_condition_acquire" => "好調獲得",
        "good_impression_acquire" => "好印象獲得",
        "conserve_acquire" => "温存獲得",
        "concentrate_acquire" => "集中獲得",
        "motivation_acquire" => "やる気獲得",
        "fullpower_acquire" => "全力獲得",
        "aggressive_acquire" => "強気獲得",
        "p_item_acquire" => "Pアイテム獲得",
        "p_drink_acquire" => "Pドリンク獲得",
        "consultation_drink" => "相談ドリンク交換",
        "rest" => "休憩",
        "vo_sp_end" => "VoSP終了",
        "da_sp_end" => "DaSP終了",
        "vi_sp_end" => "ViSP終了",
        "vo_lesson_end" => "Voレッスン終了",
        "da_lesson_end" => "Daレッスン終了",
        "vi_lesson_end" => "Viレッスン終了",
        _ => trigger
    };

    private string BuildReasonText(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel, SupportCard card)
    {
        var prefix = effect.Source == "item" ? "[アイテム] " : "";
        var triggerName = TriggerDisplayName(effect.Trigger);
        var stat = effect.Stat.ToUpper();
        var val = effect.GetValue(uncapLevel);

        if (effect.Trigger == "equip")
        {
            if (effect.ValueType == "flat" && effect.EventParam)
            {
                var boost = card.GetEventParamBoostPercent(uncapLevel);
                var result = (int)(val * (1.0 + boost / 100.0));
                return $"{prefix}{stat} 初期値+{(int)val}(+{(int)boost}%)={result}";
            }
            return effect.ValueType switch
            {
                "sp_rate" => $"{prefix}{stat} SP率+{val}%",
                "para_bonus" => $"{prefix}パラボ+{val}%",
                _ => $"{prefix}{stat} 初期値+{(int)val}"
            };
        }

        int fires = triggerCounts.GetValueOrDefault(effect.Trigger, 0);
        if (effect.MaxCount.HasValue)
            fires = Math.Min(fires, effect.MaxCount.Value);

        var countInfo = effect.MaxCount.HasValue
            ? $"({fires}/{effect.MaxCount}回)"
            : $"(×{fires})";

        return effect.ValueType switch
        {
            "flat" => $"{prefix}{triggerName} {stat}+{(int)val} {countInfo}",
            _ => $"{prefix}{triggerName} {stat}+{val}% {countInfo}"
        };
    }

    private Dictionary<string, int> CountTriggers(
        TrainingPlan plan,
        Dictionary<string, int> lessonAllocation,
        List<string> mainStats,
        List<TurnChoice>? turnChoices = null)
    {
        var counts = new Dictionary<string, int>();

        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        int totalLessons = lessonAllocation.Values.Sum();
        counts["sp_end"] = Math.Min(totalLessons, lessonWeeks.Count);
        counts["lesson_end"] = counts["sp_end"];

        // 属性別SP終了・レッスン終了トリガー
        foreach (var kvp in lessonAllocation)
        {
            if (kvp.Value <= 0) continue;
            counts[$"{kvp.Key}_sp_end"] = kvp.Value;       // vo_sp_end, da_sp_end, vi_sp_end
            counts[$"{kvp.Key}_lesson_end"] = kvp.Value;    // vo_lesson_end, da_lesson_end, vi_lesson_end
        }

        // 試験イベント数はスケジュールから確定
        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent)
                counts["exam_end"] = counts.GetValueOrDefault("exam_end") + 1;
        }

        // HIFモード等、ユーザがターン選択を明示している場合は実選択ベースで集計する。
        // available_actions の優先度ベースだと「Day を 活動支給→お出かけ に変えても活動支給回数が減らない」
        // という不整合が起きるため。
        if (turnChoices != null)
        {
            foreach (var tc in turnChoices)
            {
                switch (tc.ChosenAction)
                {
                    case ActionType.VoLesson:
                    case ActionType.DaLesson:
                    case ActionType.ViLesson:
                        break;
                    case ActionType.VoClass:
                    case ActionType.DaClass:
                    case ActionType.ViClass:
                        counts["class_end"] = counts.GetValueOrDefault("class_end") + 1;
                        break;
                    case ActionType.Outing:
                        counts["outing_end"] = counts.GetValueOrDefault("outing_end") + 1;
                        break;
                    case ActionType.Consultation:
                        counts["consultation"] = counts.GetValueOrDefault("consultation") + 1;
                        break;
                    case ActionType.ActivitySupply:
                        counts["activity_supply"] = counts.GetValueOrDefault("activity_supply") + 1;
                        break;
                    case ActionType.SpecialTraining:
                        counts["special_training"] = counts.GetValueOrDefault("special_training") + 1;
                        break;
                }
            }
            return counts;
        }

        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent) continue;
            if (week.Lessons.Count > 0) continue;

            var actions = week.AvailableActions;
            if (actions.Contains("activity_supply"))
                counts["activity_supply"] = counts.GetValueOrDefault("activity_supply") + 1;
            else if (actions.Contains("outing"))
                counts["outing_end"] = counts.GetValueOrDefault("outing_end") + 1;
            else if (actions.Contains("consultation"))
                counts["consultation"] = counts.GetValueOrDefault("consultation") + 1;
            else if (actions.Contains("special_training"))
                counts["special_training"] = counts.GetValueOrDefault("special_training") + 1;
            else if (actions.Contains("vo_class") || actions.Contains("da_class") || actions.Contains("vi_class"))
                counts["class_end"] = counts.GetValueOrDefault("class_end") + 1;
        }

        return counts;
    }

    private double CalculateFlatValue(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel, SupportCard card)
    {
        var val = effect.GetValue(uncapLevel);
        if (effect.Trigger == "equip")
        {
            if (effect.EventParam)
            {
                val *= 1.0 + card.GetEventParamBoostPercent(uncapLevel) / 100.0;
            }
            return val;
        }

        int fires = triggerCounts.GetValueOrDefault(effect.Trigger, 0);

        if (effect.MaxCount.HasValue)
            fires = Math.Min(fires, effect.MaxCount.Value);

        return val * fires;
    }

    /// <summary>
    /// レッスン配分に基づいて、全レッスンのSpBonusを属性別に合計する。
    /// パラメータボーナスの属性別寄与計算に使用。
    /// </summary>
    private StatusValues CalculateLessonStatTotals(TrainingPlan plan, Dictionary<string, int> lessonAllocation)
    {
        int vo = 0, da = 0, vi = 0;

        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderByDescending(w => w.Week)
            .ToList();

        var weekQueue = new Queue<WeekSchedule>(lessonWeeks);

        foreach (var stat in lessonAllocation.OrderByDescending(kv => kv.Value))
        {
            int count = stat.Value;
            for (int i = 0; i < count && weekQueue.Count > 0; i++)
            {
                var w = weekQueue.Dequeue();
                var lesson = w.GetLesson(stat.Key);
                if (lesson != null)
                {
                    vo += lesson.SpBonus.Vo;
                    da += lesson.SpBonus.Da;
                    vi += lesson.SpBonus.Vi;
                }
            }
        }

        // HIFモードの選抜試験 (基礎値+配分値) もパラボ対象になるので加算する。
        // BuildPlanAndChoices で audition の StatusGain には既に base+alloc が反映されている。
        foreach (var w in plan.Schedule)
        {
            if (w.Type == "audition"
                && (w.HifExamBase != null || w.HifExamDistributed != null)
                && w.StatusGain != null)
            {
                vo += w.StatusGain.Vo;
                da += w.StatusGain.Da;
                vi += w.StatusGain.Vi;
            }
        }

        return new StatusValues(vo, da, vi);
    }

    private string GenerateLabel(Dictionary<string, int> cardTypeSlots, int freeSlots = 0)
    {
        var parts = new List<string>();
        foreach (var kvp in cardTypeSlots.OrderByDescending(k => k.Value))
        {
            if (kvp.Value > 0)
            {
                var name = kvp.Key switch
                {
                    "vo" => "Vocal",
                    "da" => "Dance",
                    "vi" => "Visual",
                    _ => kvp.Key
                };
                parts.Add($"{name} {kvp.Value}");
            }
        }
        if (freeSlots > 0)
            parts.Add($"フリー {freeSlots}");
        return string.Join(" / ", parts) + " 編成";
    }
}
