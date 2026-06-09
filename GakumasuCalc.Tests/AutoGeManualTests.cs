using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;

namespace GakumasuCalc.Tests;

/// <summary>
/// 自動編成 ≧ 手動編成 (通常モード・実データ)。TS 版 cardScoring.autoGeManual.test.ts と対応。
/// メイン属性の意図に沿うバランス編成 (最適化器が探索する設計空間内) に対して自動が劣らないことを検証。
/// イベント回数は実テンプレート (hatsu_legend「センス（活動支給軸）」) を選択し additionalCounts に渡す。
/// 単一属性6枚のような型偏重編成は意図的に生成しない (確定仕様: 6枚同属性だとメイン2/サブが
/// 必要ステータスに届かずゲーム的に成立しない) ため比較対象にしない。
/// </summary>
public class AutoGeManualTests
{
    private static readonly CardScoringService Svc = new();
    private static readonly HashSet<string> Wild = new() { "all", "as" };

    private static TrainingPlan Plan => RepoData.LoadPlan("hatsu_legend");
    private static List<SupportCard> AllCards => RepoData.LoadAllCards();
    private static AdditionalCounts Counts => RepoData.TemplateCounts("hatsu_legend", "センス（活動支給軸）");
    private static int LessonWeeks =>
        Plan.Schedule.Count(w => w.Lessons.Count > 0 && w.Type != "fixed_event");

    private static List<SupportCard> TopByType(TrainingPlan plan, string stat, int n) =>
        AllCards
            .Where(c => c.Type == stat || Wild.Contains(c.Type))
            .OrderByDescending(c => DeckScorer.ScoreDeck(plan, new() { c }, new() { stat }, null, null, Counts))
            .Take(n)
            .ToList();

    private static List<SupportCard> FillDistinct(IEnumerable<SupportCard> primary, IEnumerable<SupportCard> pool, int size)
    {
        var outp = new List<SupportCard>();
        var used = new HashSet<string>();
        foreach (var c in primary.Concat(pool))
        {
            if (used.Contains(c.Id)) continue;
            outp.Add(c);
            used.Add(c.Id);
            if (outp.Count == size) break;
        }
        return outp;
    }

    private static int AutoBest(List<string> mainStats, string subStat, Dictionary<string, int>? spCounts = null)
    {
        var patterns = Svc.SelectMultiplePatterns(
            Plan, AllCards, mainStats, subStat, LessonWeeks, spCounts, additionalCounts: Counts);
        int best = int.MinValue;
        foreach (var pat in patterns)
        {
            int s = DeckScorer.ScoreDeck(Plan, pat.SelectedCards.Select(c => c.Card).ToList(), mainStats, null, null, Counts);
            if (s > best) best = s;
        }
        return best;
    }

    private static List<(string label, List<SupportCard> cards)> BalancedManuals(List<string> mainStats, string subStat)
    {
        var plan = Plan;
        var m1 = mainStats[0];
        var m2 = mainStats[1];
        var ranked = AllCards
            .OrderByDescending(c => DeckScorer.ScoreDeck(plan, new() { c }, mainStats, null, null, Counts))
            .ToList();

        var deck33 = FillDistinct(TopByType(plan, m1, 3).Concat(TopByType(plan, m2, 3)), ranked, 6);
        var deck222 = FillDistinct(
            TopByType(plan, "vo", 2).Concat(TopByType(plan, "da", 2)).Concat(TopByType(plan, "vi", 2)), ranked, 6);
        var deck321 = FillDistinct(
            TopByType(plan, m1, 3).Concat(TopByType(plan, m2, 2)).Concat(TopByType(plan, subStat, 1)), ranked, 6);

        return new()
        {
            ("バランス3+3", deck33),
            ("バランス2+2+2", deck222),
            ("バランス3+2+1", deck321),
        };
    }

    public static IEnumerable<object[]> Combos => new[]
    {
        new object[] { new List<string> { "vo", "da" }, "vi" },
        new object[] { new List<string> { "da", "vi" }, "vo" },
        new object[] { new List<string> { "vo", "vi" }, "da" },
    };

    [Theory]
    [MemberData(nameof(Combos))]
    public void 自動はバランス手動編成に劣らない(List<string> mainStats, string subStat)
    {
        int auto = AutoBest(mainStats, subStat);
        foreach (var (label, cards) in BalancedManuals(mainStats, subStat))
        {
            int manual = DeckScorer.ScoreDeck(Plan, cards, mainStats, null, null, Counts);
            Assert.True(auto >= manual, $"main={string.Join("/", mainStats)} 手動[{label}] manual={manual} > auto={auto}");
        }
    }
}
