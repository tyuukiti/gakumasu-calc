using GakumasuCalc.Models;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>
/// 総当たり最適オラクル (合成・小プール専用)。TS 版 bruteForce.ts と対応。
/// スロット制約を満たす全カード組合せを列挙し ScoreDeck で採点して真の最大を返す。
/// </summary>
public static class BruteForce
{
    private static readonly HashSet<string> WildcardTypes = new() { "all", "as" };
    private const int MaxCombinations = 20000;

    public sealed class Result
    {
        public int BestTotal;
        public List<SupportCard> BestDeck = new();
        public int Evaluated;
    }

    private static long NCk(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        double r = 1;
        for (int i = 0; i < k; i++) r = r * (n - i) / (i + 1);
        return (long)Math.Round(r);
    }

    private static IEnumerable<int[]> Combinations(int n, int size)
    {
        if (size == 0) { yield return Array.Empty<int>(); yield break; }
        if (size > n) yield break;
        var idx = new int[size];
        for (int i = 0; i < size; i++) idx[i] = i;
        while (true)
        {
            yield return (int[])idx.Clone();
            int i = size - 1;
            while (i >= 0 && idx[i] == i + n - size) i--;
            if (i < 0) yield break;
            idx[i]++;
            for (int j = i + 1; j < size; j++) idx[j] = idx[j - 1] + 1;
        }
    }

    /// <summary>
    /// 部分集合がスロット要件を満たせるか。
    /// 実現可能 ⇔ Σ max(0, required[stat] - specific[stat]) ≤ ワイルド(all/as)数。
    /// </summary>
    private static bool IsFeasible(List<SupportCard> cards, Dictionary<string, int> cardTypeSlots)
    {
        var specific = new Dictionary<string, int>();
        int wild = 0;
        foreach (var c in cards)
        {
            if (WildcardTypes.Contains(c.Type)) wild++;
            else specific[c.Type] = specific.GetValueOrDefault(c.Type) + 1;
        }
        int shortfall = 0;
        foreach (var kv in cardTypeSlots)
            shortfall += Math.Max(0, kv.Value - specific.GetValueOrDefault(kv.Key));
        return shortfall <= wild;
    }

    public static Result FindOptimalDeck(
        TrainingPlan plan,
        List<SupportCard> pool,
        Dictionary<string, int> cardTypeSlots,
        int freeSlots,
        List<string> mainStats,
        Dictionary<string, int>? uncapLevels = null)
    {
        int totalSlots = cardTypeSlots.Values.Sum() + freeSlots;
        long combos = NCk(pool.Count, totalSlots);
        if (combos > MaxCombinations)
            throw new InvalidOperationException(
                $"brute force too large: C({pool.Count},{totalSlots})={combos} > {MaxCombinations}");

        int bestTotal = int.MinValue;
        var bestDeck = new List<SupportCard>();
        int evaluated = 0;

        foreach (var idxs in Combinations(pool.Count, totalSlots))
        {
            var deck = idxs.Select(i => pool[i]).ToList();
            if (!IsFeasible(deck, cardTypeSlots)) continue;
            evaluated++;
            int total = DeckScorer.ScoreDeck(plan, deck, mainStats, uncapLevels);
            if (total > bestTotal)
            {
                bestTotal = total;
                bestDeck = deck;
            }
        }

        return new Result { BestTotal = bestTotal, BestDeck = bestDeck, Evaluated = evaluated };
    }

    public sealed class RentalResult
    {
        public int BestTotal;
        public List<SupportCard> BestDeck = new();
        public string? BestRentalId;
        public int Evaluated;
    }

    /// <summary>
    /// レンタル枠対応の総当たり最適オラクル (実データ・要キュレーション小プール)。TS 版 bruteForceOptimalRental と対応。
    /// 「非レンタル(deckSize-1)枚 (所持凸数) + レンタル1枚 (4凸借用)」を全列挙し、外部 score 関数
    /// (実 Calculate / cap後合計) で採点して真の最大を返す。プールは寄与上位等にキュレーションして
    /// 組合せ爆発を防ぐこと。findOptimalDeck がモデル化しないレンタル枠を扱う。
    /// </summary>
    public static RentalResult FindOptimalDeckWithRental(
        List<SupportCard> ownedPool,
        List<SupportCard> rentalPool,
        int deckSize,
        Func<List<SupportCard>, string, int> score,
        Func<List<SupportCard>, bool> isValid,
        int maxCombinations = MaxCombinations)
    {
        int ownedSlots = deckSize - 1;
        long combos = (long)rentalPool.Count * NCk(ownedPool.Count, ownedSlots);
        if (combos > maxCombinations)
            throw new InvalidOperationException(
                $"rental brute force too large: {rentalPool.Count} × C({ownedPool.Count},{ownedSlots}) ≈ {combos} > {maxCombinations}");

        int bestTotal = int.MinValue;
        var bestDeck = new List<SupportCard>();
        string? bestRentalId = null;
        int evaluated = 0;

        foreach (var rental in rentalPool)
        {
            var ownedCandidates = ownedPool.Where(c => c.Id != rental.Id).ToList();
            foreach (var idxs in Combinations(ownedCandidates.Count, ownedSlots))
            {
                var deck = idxs.Select(i => ownedCandidates[i]).Append(rental).ToList();
                if (!isValid(deck)) continue;
                evaluated++;
                int total = score(deck, rental.Id);
                if (total > bestTotal)
                {
                    bestTotal = total;
                    bestDeck = deck;
                    bestRentalId = rental.Id;
                }
            }
        }

        return new RentalResult { BestTotal = bestTotal, BestDeck = bestDeck, BestRentalId = bestRentalId, Evaluated = evaluated };
    }
}
