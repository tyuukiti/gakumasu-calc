using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
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
}
