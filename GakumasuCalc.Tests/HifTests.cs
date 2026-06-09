using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using static GakumasuCalc.Services.CardScoringService;

namespace GakumasuCalc.Tests;

/// <summary>
/// HIFモード (SelectMultiplePatternsHif) の実データテスト (TS 版 cardScoringHif.test.ts と対応)。
/// - メイン属性は順序込みの全6通り (vo/da, vo/vi, da/vo, da/vi, vi/vo, vi/da)。
/// - イベント回数は実テンプレート (HIF「センス」) を選択し additionalCounts に渡す。
/// - 選択(turnChoicesOverride)と採点(DeckScorer)で同一 turnChoices / additionalCounts を共有。
/// </summary>
public class HifTests
{
    private static readonly CardScoringService Svc = new();
    private static TrainingPlan Plan => RepoData.LoadPlan("hif");
    private static List<SupportCard> AllCards => RepoData.LoadAllCards();
    private static AdditionalCounts Counts => RepoData.TemplateCounts("hif", "センス");

    // メイン属性の順序込み全6通り
    public static IEnumerable<object[]> OrderedPairs => new[]
    {
        new object[] { new List<string> { "vo", "da" } },
        new object[] { new List<string> { "vo", "vi" } },
        new object[] { new List<string> { "da", "vo" } },
        new object[] { new List<string> { "da", "vi" } },
        new object[] { new List<string> { "vi", "vo" } },
        new object[] { new List<string> { "vi", "da" } },
    };

    private static Dictionary<string, int> LessonAllocationFrom(List<TurnChoice> tc)
    {
        var alloc = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var t in tc)
        {
            if (t.ChosenAction == ActionType.VoLesson) alloc["vo"]++;
            else if (t.ChosenAction == ActionType.DaLesson) alloc["da"]++;
            else if (t.ChosenAction == ActionType.ViLesson) alloc["vi"]++;
        }
        return alloc;
    }

    private static (List<DeckResult> patterns, List<TurnChoice> tc) AutoPatterns(
        List<string> mainStats, Dictionary<string, int>? spCounts = null)
    {
        var plan = Plan;
        var tc = CardScoringService.BuildTurnChoices(plan, mainStats);
        var alloc = LessonAllocationFrom(tc);
        var patterns = Svc.SelectMultiplePatternsHif(
            plan, AllCards, mainStats, alloc, spCounts,
            additionalCounts: Counts, turnChoicesOverride: tc);
        return (patterns, tc);
    }

    [Theory]
    [MemberData(nameof(OrderedPairs))]
    public void 全パターンが6枚_重複なし(List<string> mainStats)
    {
        var (patterns, _) = AutoPatterns(mainStats);
        Assert.NotEmpty(patterns);
        foreach (var p in patterns)
        {
            var cards = Constraints.DeckCards(p);
            Assert.Equal(6, cards.Count);
            Assert.True(Constraints.HasNoDuplicates(cards));
        }
    }

    [Theory]
    [MemberData(nameof(OrderedPairs))]
    public void 自動編成は手動_単体寄与トップ6_に劣らない(List<string> mainStats)
    {
        var plan = Plan;
        var counts = Counts;
        var (patterns, tc) = AutoPatterns(mainStats);

        int auto = int.MinValue;
        foreach (var p in patterns)
        {
            int s = DeckScorer.ScoreDeck(plan, Constraints.DeckCards(p), mainStats, null, tc, counts);
            if (s > auto) auto = s;
        }

        // HIF はオールフリーで単型も組めるため、単体寄与トップ6 を比較対象にできる
        var manual = AllCards
            .OrderByDescending(c => DeckScorer.ScoreDeck(plan, new() { c }, mainStats, null, tc, counts))
            .Take(6)
            .ToList();
        int manualScore = DeckScorer.ScoreDeck(plan, manual, mainStats, null, tc, counts);

        Assert.True(auto >= manualScore, $"main={string.Join("/", mainStats)} auto={auto} < manual={manualScore}");
    }

    [Fact]
    public void SP指定ありで全パターンがSP制約を満たす()
    {
        var (patterns, _) = AutoPatterns(new() { "vo", "da" }, new Dictionary<string, int> { ["vo"] = 2 });
        foreach (var p in patterns)
            Assert.True(Constraints.CountSp(Constraints.DeckCards(p), "vo") >= 2);
    }
}
