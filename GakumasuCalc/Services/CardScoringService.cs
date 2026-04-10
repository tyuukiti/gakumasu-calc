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
        /// <summary>効果別の内訳</summary>
        public List<EffectBreakdown> Breakdowns { get; set; } = new();
        /// <summary>レンタルカードかどうか</summary>
        public bool IsRental { get; set; }
        /// <summary>必須カードかどうか</summary>
        public bool IsRequired { get; set; }
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
    /// 属性ごとの枚数制約+ステータス上限2800を考慮して最適6枚を選択する。
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
        List<string>? requiredCardIds = null)
    {
        var statCap = plan.StatusLimit;
        var triggerCounts = CountTriggers(plan, lessonAllocation, mainStats);

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

        // 全カードの属性別寄与を事前計算
        var cardContributions = eligible
            .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels))
            .ToList();

        // 全カードプール (フィルタ外も補充用に)
        var allContributions = allCards
            .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels))
            .ToList();

        // 属性枠ごとに選択 (上限考慮)
        var selected = new List<CardScore>();
        var usedIds = new HashSet<string>();

        // 現在の累積ステータス (ベース + 選択済みカード)
        int accVo = baseStats.Vo, accDa = baseStats.Da, accVi = baseStats.Vi;

        // 属性枠・フリー枠の残数を管理するローカルコピー
        var remainingSlots = new Dictionary<string, int>(cardTypeSlots);
        int remainingFree = freeSlots;

        // ステップ0: 必須カードを強制挿入
        CardScore? requiredRentalCard = null;
        var protectedIds = new HashSet<string>();

        if (requiredCardIds != null && requiredCardIds.Count > 0)
        {
            // spCounts のローカルコピー（必須カードでSP率を消費するため）
            var spCountsCopy = spCounts != null ? new Dictionary<string, int>(spCounts) : null;

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

                var contribution = CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, reqUncap);
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
                    accVo += contribution.RawVo;
                    accDa += contribution.RawDa;
                    accVi += contribution.RawVi;

                    // スロット消費
                    if (card.Type != "all" && remainingSlots.ContainsKey(card.Type) && remainingSlots[card.Type] > 0)
                        remainingSlots[card.Type]--;
                    else if (card.Type == "all")
                    {
                        // "all" タイプ: 最大残数の属性枠を消費
                        var maxSlot = remainingSlots.OrderByDescending(s => s.Value).FirstOrDefault();
                        if (maxSlot.Value > 0)
                            remainingSlots[maxSlot.Key]--;
                        else
                            remainingFree = Math.Max(0, remainingFree - 1);
                    }
                    else
                        remainingFree = Math.Max(0, remainingFree - 1);

                    // SP率カード判定: 必須カードがSP率エフェクトを持つなら spCounts を減算
                    if (spCountsCopy != null)
                    {
                        var spEffect = card.Effects.FirstOrDefault(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
                        if (spEffect != null)
                        {
                            var spStat = card.Type == "all" ? card.Type : card.Type;
                            foreach (var key in spCountsCopy.Keys.ToList())
                            {
                                if ((card.Type == key || card.Type == "all") && spCountsCopy[key] > 0)
                                {
                                    spCountsCopy[key]--;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // spCounts を更新（必須カードで消費した分を反映）
            if (spCountsCopy != null)
                spCounts = spCountsCopy;
        }

        // ステップ1: SP率カードをユーザ指定枚数分、先に確保
        var spCardSlotStat = new Dictionary<string, string>(); // cardId -> 消費したスロットのstat key
        var spCardUsedFree = new HashSet<string>(); // フリー枠を消費したcardId
        if (spCounts != null)
        {
            foreach (var kvp in spCounts)
            {
                var stat = kvp.Key;
                int need = kvp.Value;
                if (need <= 0) continue;

                // この属性のSP率を持つカードをステータス寄与順で選ぶ
                var spCandidates = cardContributions
                    .Where(cs => (cs.Card.Type == stat || cs.Card.Type == "all")
                                 && !usedIds.Contains(cs.Card.Id)
                                 && cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"))
                    .ToList();

                for (int i = 0; i < need; i++)
                {
                    var best = SelectBestCard(spCandidates, usedIds, accVo, accDa, accVi, statCap);
                    if (best == null) break;

                    selected.Add(best);
                    usedIds.Add(best.Card.Id);
                    protectedIds.Add(best.Card.Id); // SP率カードはポスト最適化でスワップしない
                    accVo += best.RawVo;
                    accDa += best.RawDa;
                    accVi += best.RawVi;

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
            fillAccVo += requiredRentalCard.RawVo;
            fillAccDa += requiredRentalCard.RawDa;
            fillAccVi += requiredRentalCard.RawVi;
        }
        var fillResult = GreedyFillOwned(cardContributions, selected, usedIds, fillAccVo, fillAccDa, fillAccVi, remainingSlots, remainingFree, ownedSlots, statCap);
        selected = fillResult.Selected;
        usedIds = fillResult.UsedIds;
        // 事前加算分を差し引いて実際の累積ステータスを得る
        accVo = fillResult.AccVo - (requiredRentalCard?.RawVo ?? 0);
        accDa = fillResult.AccDa - (requiredRentalCard?.RawDa ?? 0);
        accVi = fillResult.AccVi - (requiredRentalCard?.RawVi ?? 0);

        // レンタル1枠: 全カードプールから4凸で最良の1枚を選択
        if (rentalPool != null && selected.Count < 6)
        {
            if (requiredRentalCard != null)
            {
                // 必須カードがレンタル枠を使用 → Pattern A/B をスキップ
                selected.Add(requiredRentalCard);
                usedIds.Add(requiredRentalCard.Card.Id);
                accVo += requiredRentalCard.RawVo;
                accDa += requiredRentalCard.RawDa;
                accVi += requiredRentalCard.RawVi;
            }
            else
            {
            var rentalUncap = new Dictionary<string, int>();
            foreach (var c in rentalPool)
                rentalUncap[c.Id] = 4;

            // レンタル候補: 所持で選ばれたカードも含めて全カードから計算
            var allRentalContributions = rentalPool
                .Where(c => string.IsNullOrEmpty(planType)
                            || string.IsNullOrEmpty(c.Plan)
                            || c.Plan == planType
                            || c.Plan == "free")
                .Select(card => CalculateCardContribution(card, triggerCounts, lessonAllocation, lessonStatTotals, rentalUncap))
                .ToDictionary(cs => cs.Card.Id);

            // パターンA: 従来通り、未使用カードからレンタルを選択
            var unusedRentalCandidates = allRentalContributions.Values
                .Where(cs => !usedIds.Contains(cs.Card.Id))
                .ToList();
            var defaultRental = SelectBestCard(unusedRentalCandidates, usedIds, accVo, accDa, accVi, statCap);
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

                int swapAccVo = accVo - ownedCard.RawVo;
                int swapAccDa = accDa - ownedCard.RawDa;
                int swapAccVi = accVi - ownedCard.RawVi;

                var swapUsedIds = new HashSet<string>(usedIds);
                var replacementCandidates = cardContributions
                    .Where(cs => !swapUsedIds.Contains(cs.Card.Id))
                    .ToList();
                var replacement = SelectBestCard(replacementCandidates, swapUsedIds, swapAccVo, swapAccDa, swapAccVi, statCap);

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
                    localAccVo -= existingOwned.RawVo;
                    localAccDa -= existingOwned.RawDa;
                    localAccVi -= existingOwned.RawVi;
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
                    localAccVo + rentalCandidate.RawVo,
                    localAccDa + rentalCandidate.RawDa,
                    localAccVi + rentalCandidate.RawVi,
                    localRemainingSlots, localRemainingFree,
                    ownedSlots, statCap);

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
                accVo = baseStats.Vo; accDa = baseStats.Da; accVi = baseStats.Vi;
                foreach (var s in selected) { accVo += s.RawVo; accDa += s.RawDa; accVi += s.RawVi; }
            }

            CardScore? finalRental = bestOverallRental;
            if (finalRental != null)
            {
                finalRental.IsRental = true;
                selected.Add(finalRental);
                usedIds.Add(finalRental.Card.Id);
                accVo += finalRental.RawVo;
                accDa += finalRental.RawDa;
                accVi += finalRental.RawVi;
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
                var best = SelectBestCard(fallback, usedIds, accVo, accDa, accVi, statCap);
                if (best == null) break;

                selected.Add(best);
                usedIds.Add(best.Card.Id);
                accVo += best.RawVo;
                accDa += best.RawDa;
                accVi += best.RawVi;
            }
        }

        // ポスト最適化: 実際の計算結果を使ってカードスワップを試行
        if (rentalPool != null)
        {
            PostOptimize(selected, cardContributions, protectedIds,
                plan, lessonAllocation, mainStats, uncapLevels, additionalCounts);
        }

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
    private void PostOptimize(
        List<CardScore> selected,
        List<CardScore> candidates,
        HashSet<string> protectedIds,
        TrainingPlan plan,
        Dictionary<string, int> lessonAllocation,
        List<string> mainStats,
        Dictionary<string, int>? uncapLevels,
        AdditionalCounts? additionalCounts)
    {
        var calcService = new StatusCalculationService();
        var turnChoices = BuildTurnChoices(plan, mainStats);

        int Evaluate(List<SupportCard> cards)
        {
            var uc = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in selected.Where(c => c.IsRental))
                uc[cs.Card.Id] = 4;
            return calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts).FinalStatus.Total;
        }

        bool improved;
        do
        {
            improved = false;
            var currentCards = selected.Select(c => c.Card).ToList();
            int currentTotal = Evaluate(currentCards);

            foreach (var ownedCard in selected.Where(c => !c.IsRental).ToList())
            {
                // 必須カードは無条件でスワップ不可
                if (ownedCard.IsRequired) continue;

                bool ownedIsProtectedSp = protectedIds.Contains(ownedCard.Card.Id)
                    && ownedCard.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate");
                bool ownedIsProtectedNonSp = protectedIds.Contains(ownedCard.Card.Id) && !ownedIsProtectedSp;
                // 非SPの保護カードはスキップ
                if (ownedIsProtectedNonSp) continue;

                foreach (var candidate in candidates)
                {
                    if (selected.Any(c => c.Card.Id == candidate.Card.Id)) continue;
                    // タイプ分布を維持: 同じタイプ同士、または all タイプとの交換のみ許可
                    if (candidate.Card.Type != ownedCard.Card.Type
                        && candidate.Card.Type != "all"
                        && ownedCard.Card.Type != "all") continue;
                    // SP率で保護されたカードは、SP率持ちの候補とのみ交換可能
                    if (ownedIsProtectedSp
                        && !candidate.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"))
                        continue;

                    var testCards = new List<SupportCard>(currentCards);
                    int idx = testCards.IndexOf(ownedCard.Card);
                    testCards[idx] = candidate.Card;

                    int testTotal = Evaluate(testCards);
                    if (testTotal > currentTotal)
                    {
                        int selIdx = selected.IndexOf(ownedCard);
                        selected[selIdx] = candidate;
                        // SP率保護を新カードに引き継ぐ
                        if (ownedIsProtectedSp)
                        {
                            protectedIds.Remove(ownedCard.Card.Id);
                            protectedIds.Add(candidate.Card.Id);
                        }
                        currentTotal = testTotal;
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
    private static List<TurnChoice> BuildTurnChoices(TrainingPlan plan, List<string> mainStats)
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
        int statCap = DEFAULT_STAT_CAP)
    {
        CardScore? best = null;
        int bestGain = int.MinValue;

        foreach (var cs in candidates)
        {
            if (usedIds.Contains(cs.Card.Id)) continue;

            // キャップ適用後の実効増分
            int newVo = Math.Min(currentVo + cs.RawVo, statCap);
            int newDa = Math.Min(currentDa + cs.RawDa, statCap);
            int newVi = Math.Min(currentVi + cs.RawVi, statCap);

            int cappedVo = Math.Min(currentVo, statCap);
            int cappedDa = Math.Min(currentDa, statCap);
            int cappedVi = Math.Min(currentVi, statCap);

            int gain = (newVo - cappedVo) + (newDa - cappedDa) + (newVi - cappedVi);

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
            int statCap)
    {
        var sel = new List<CardScore>(selectedInit);
        var used = new HashSet<string>(usedIdsInit);
        int aVo = accVoInit, aDa = accDaInit, aVi = accViInit;

        // 属性枠
        foreach (var kvp in remainingSlotsInit.OrderByDescending(k => k.Value))
        {
            var type = kvp.Key;
            int count = kvp.Value;
            if (count <= 0) continue;

            var candidates = contributions
                .Where(cs => (cs.Card.Type == type || cs.Card.Type == "all")
                             && !used.Contains(cs.Card.Id))
                .ToList();

            for (int i = 0; i < count && sel.Count < ownedSlots; i++)
            {
                var best = SelectBestCard(candidates, used, aVo, aDa, aVi, statCap);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += best.RawVo;
                aDa += best.RawDa;
                aVi += best.RawVi;
            }
        }

        // フリー枠
        for (int i = 0; i < remainingFreeInit && sel.Count < ownedSlots; i++)
        {
            var freeCandidates = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            var best = SelectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap);
            if (best == null) break;
            sel.Add(best);
            used.Add(best.Card.Id);
            aVo += best.RawVo;
            aDa += best.RawDa;
            aVi += best.RawVi;
        }

        // 補充
        if (sel.Count < ownedSlots)
        {
            var remaining = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            while (sel.Count < ownedSlots)
            {
                var best = SelectBestCard(remaining, used, aVo, aDa, aVi, statCap);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += best.RawVo;
                aDa += best.RawDa;
                aVi += best.RawVi;
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
    /// カード1枚の属性別寄与を計算
    /// </summary>
    private CardScore CalculateCardContribution(
        SupportCard card,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels)
    {
        int uncap = StatusCalculationService.GetUncapLevel(card, uncapLevels);
        double vo = 0, da = 0, vi = 0;
        var breakdowns = new List<EffectBreakdown>();

        foreach (var effect in card.Effects)
        {
            // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
            if (effect.ValueType == "sp_rate") continue;

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
                "flat" => CalculateFlatValue(effect, triggerCounts, uncap),
                _ => 0
            };

            if (Math.Abs(value) < 0.01) continue;

            // 内訳の理由テキスト生成
            var reason2 = BuildReasonText(effect, triggerCounts, uncap);

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
            TotalValue = iVo + iDa + iVi,
            Breakdowns = breakdowns
        };
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
        "skill_ssr_acquire" => "スキル(SSR)獲得",
        "skill_enhance" => "スキル強化",
        "skill_delete" => "スキル削除",
        "skill_custom" => "スキルカスタム",
        "skill_change" => "スキルチェンジ",
        "active_enhance" => "アクティブ強化",
        "active_delete" => "アクティブ削除",
        "mental_acquire" => "メンタル獲得",
        "mental_enhance" => "メンタル強化",
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

    private string BuildReasonText(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel)
    {
        var prefix = effect.Source == "item" ? "[アイテム] " : "";
        var triggerName = TriggerDisplayName(effect.Trigger);
        var stat = effect.Stat.ToUpper();
        var val = effect.GetValue(uncapLevel);

        if (effect.Trigger == "equip")
        {
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
        List<string> mainStats)
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

        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent)
            {
                counts["exam_end"] = counts.GetValueOrDefault("exam_end") + 1;
                continue;
            }

            if (week.Lessons.Count > 0)
                continue;

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

    private double CalculateFlatValue(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel)
    {
        var val = effect.GetValue(uncapLevel);
        if (effect.Trigger == "equip")
            return val;

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
