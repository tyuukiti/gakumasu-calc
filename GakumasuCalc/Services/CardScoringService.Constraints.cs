using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
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
    /// - レンタル枠の補充候補も planType でフィルタする (所持プールは呼び出し側で既にフィルタ済み)。
    ///   ここだけ未フィルタだと、別プランの SP カードが素の寄与順で先頭に来た時にそのまま
    ///   採用され「別プランのサポカが1枚だけ選出される」(2026-09 ユーザ報告)
    /// - レンタル枠に必須カードが載っている (EnsureRentalSlot が低凸の必須カードを借用先に指定した)
    ///   場合は差し替えない。差し替えると必須カードがデッキから消える (必須 > SP枚数)
    /// - 補充のために編成パターン(cardTypeSlots)を崩すことは許容する (SP枚数 > 編成パターン)
    /// </summary>
    private void EnforceSpCounts(
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
        Dictionary<string, int>? spCounts)
    {
        if (spCounts == null) return;

        bool CoversStat(SupportCard card, string stat) =>
            card.Effects.Any(e =>
                e.Trigger == "equip" &&
                e.ValueType == "sp_rate" &&
                (e.Stat == stat || e.Stat == "all"));

        // このカードを外すと、いずれかの属性の SP 充足数が要求枚数を割るか (現在枚数ベース)。
        // 「要求>0の属性をカバーしていたら一律外せない」だと、all/as 型で余剰カバーが
        // ある場合 (例: vi要求1 を all型が充足済みで vi SP がもう1枚居る) にレンタル枠の
        // 差し替えが封じられ、別属性の SP 不足を補充できない。
        bool WouldBreakSpCoverage(SupportCard card)
        {
            foreach (var s in new[] { "vo", "da", "vi" })
            {
                int need = spCounts.GetValueOrDefault(s);
                if (need <= 0) continue;
                if (!CoversStat(card, s)) continue;
                int cur = selected.Count(cs => CoversStat(cs.Card, s));
                if (cur <= need) return true;
            }
            return false;
        }

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
                // 外せる犠牲カード: 非レンタル・非必須・外しても他属性のSP充足を割らない、寄与の弱い順
                // 同属性のカードを優先的に外して編成バランスへの影響を抑える
                var victim = selected
                    .Where(cs => !cs.IsRental && !cs.IsRequired && !WouldBreakSpCoverage(cs.Card))
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
            //    (必須カードが載ったレンタル枠は差し替えない / 候補は planType でフィルタ)
            if (current < need && rentalPool != null)
            {
                int rentalIdx = selected.FindIndex(cs => cs.IsRental);
                if (rentalIdx >= 0 &&
                    !selected[rentalIdx].IsRequired &&
                    !CoversStat(selected[rentalIdx].Card, stat) &&
                    !WouldBreakSpCoverage(selected[rentalIdx].Card))
                {
                    var used = InDeck();
                    bool PlanOk(SupportCard c) =>
                        string.IsNullOrEmpty(planType) || string.IsNullOrEmpty(c.Plan) || c.Plan == planType || c.Plan == "free";
                    var rentalSp = rentalPool
                        .Where(c => CoversStat(c, stat) && PlanOk(c) && !used.Contains(c.Id))
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
}
