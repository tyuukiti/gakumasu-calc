using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
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

            // SP率カードの不足をフリー枠で吸収できるかチェック。
            // レンタル枠(6枚目)にもSPカードを1枚置けるため容量に加算する。
            // 加算しないと SP合計6 (例: Vo4+Da2) で全パターンがスキップされ
            // 「有効な編成パターンが見つかりませんでした」になる (issue #145)。
            int spShortage = 0;
            foreach (var s in new[] { "vo", "da", "vi" })
            {
                int required = spCounts?.GetValueOrDefault(s) ?? 0;
                int provided = cardTypeSlots.GetValueOrDefault(s);
                spShortage += Math.Max(0, required - provided);
            }
            if (spShortage > free + (rentalPool != null ? 1 : 0)) continue;

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
        var baseStats = EstimateBaseStats(plan, lessonAllocation, turnChoicesOverride);
        var lessonStatTotals = CalculateLessonStatTotals(plan, lessonAllocation, turnChoicesOverride);
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

        var adjustedCounts = RecomputeBreakdownsDeckAware(selected, triggerCounts, lessonAllocation, lessonStatTotals, uncapLevels);
        RecalculateWithCap(selected, baseStats, statCap);
        selected = selected.OrderByDescending(cs => cs.TotalValue).ToList();
        results[freeIdx] = new DeckResult
        {
            Label = free.Label,
            SelectedCards = selected,
            AbilitySummary = BuildAbilitySummary(selected, adjustedCounts, uncapLevels),
        };
    }
}
