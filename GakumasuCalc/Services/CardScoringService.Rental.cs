using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
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
    ///
    /// 借用候補は低凸所持カードに加えて rentalPool 内の未所持カードも含める。旧レンタルが
    /// SP要員 (例: 0071 0凸所持を4凸借用) だと OptimizeRentalCard の単手入替は SP枚数不足で
    /// 全滅するため、「未所持カードを借用し、旧レンタルを所持0凸のSP要員に戻し、弱い1枚を落とす」
    /// 複合手はこのパスでしか到達できない。
    /// </summary>
    private void OptimizeRentalBorrowUpgrade(
        List<CardScore> selected,
        List<CardScore> cardContributions,
        HashSet<string> ownedIds,
        List<SupportCard>? rentalPool,
        string? planType,
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
        // 借用候補: デッキ外の (a) 低凸(uncap<4)所持カード + (b) rentalPool 内の未所持カード。
        // 4凸寄与の上位のみ評価しコストを抑える。
        bool PlanOk(SupportCard c) =>
            string.IsNullOrEmpty(planType) || string.IsNullOrEmpty(c.Plan) || c.Plan == planType || c.Plan == "free";
        var ownedCands = cardContributions
            .Where(cs => !inDeck.Contains(cs.Card.Id) && (uncapLevels?.GetValueOrDefault(cs.Card.Id) ?? 0) < 4)
            .Select(cs => At4(cs.Card));
        var unownedCands = (rentalPool ?? new List<SupportCard>())
            .Where(c => !inDeck.Contains(c.Id) && !ownedIds.Contains(c.Id) && PlanOk(c))
            .Select(At4);
        var borrowCands = ownedCands.Concat(unownedCands)
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
}
