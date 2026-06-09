using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;

namespace GakumasuCalc.Tests;

/// <summary>
/// L4: クロス実装パリティ (C#側)。
/// TS版が生成・コミットした expected.json に対して C#版の編成・合計が一致することを検証する。
/// 不一致 = TS版とC#版のロジックが乖離している証拠 (feedback_fix_both_csharp_and_web 違反)。
/// </summary>
public class ParityTests
{
    private static readonly CardScoringService Svc = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class ParityScenario
    {
        public string Id { get; set; } = "";
        public List<string> MainStats { get; set; } = new();
        public string SubStat { get; set; } = "";
        public Dictionary<string, int>? SpCounts { get; set; }
        public string? Mode { get; set; }   // "hif" なら HIF モード
        public string? PlanId { get; set; } // シナリオ個別プランID (省略時は config.PlanId)
        public string? TemplateName { get; set; } // イベント回数テンプレート名
    }

    private sealed class ParityConfig
    {
        public string PlanId { get; set; } = "";
        public List<ParityScenario> Scenarios { get; set; } = new();
    }

    private sealed class PatternResult
    {
        public List<string> Ids { get; set; } = new();
        public int Total { get; set; }
    }

    private static string ParityDir() => Path.Combine(RepoData.RepoRoot(), "TestFixtures", "parity");

    private static List<PatternResult> ComputeScenario(TrainingPlan plan, List<SupportCard> allCards, ParityScenario sc)
    {
        var counts = sc.TemplateName != null ? RepoData.TemplateCounts(plan.Id, sc.TemplateName) : null;

        if (sc.Mode == "hif")
        {
            var tc = CardScoringService.BuildTurnChoices(plan, sc.MainStats);
            var alloc = LessonAllocationFrom(tc);
            var hifPatterns = Svc.SelectMultiplePatternsHif(
                plan, allCards, sc.MainStats, alloc, sc.SpCounts,
                additionalCounts: counts, turnChoicesOverride: tc);
            return hifPatterns.Select(pat =>
            {
                var cards = pat.SelectedCards.Select(c => c.Card).ToList();
                return new PatternResult
                {
                    Ids = cards.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    Total = DeckScorer.ScoreDeck(plan, cards, sc.MainStats, null, tc, counts),
                };
            }).ToList();
        }

        int lessonWeeks = plan.Schedule.Count(w => w.Lessons.Count > 0 && w.Type != "fixed_event");
        var patterns = Svc.SelectMultiplePatterns(
            plan, allCards, sc.MainStats, sc.SubStat, lessonWeeks, sc.SpCounts,
            additionalCounts: counts);
        return patterns.Select(pat =>
        {
            var cards = pat.SelectedCards.Select(c => c.Card).ToList();
            return new PatternResult
            {
                Ids = cards.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Total = DeckScorer.ScoreDeck(plan, cards, sc.MainStats, null, null, counts),
            };
        }).ToList();
    }

    private static Dictionary<string, int> LessonAllocationFrom(List<TurnChoice> turnChoices)
    {
        var alloc = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            if (tc.ChosenAction == ActionType.VoLesson) alloc["vo"]++;
            else if (tc.ChosenAction == ActionType.DaLesson) alloc["da"]++;
            else if (tc.ChosenAction == ActionType.ViLesson) alloc["vi"]++;
        }
        return alloc;
    }

    [Fact]
    public void TS基準のexpected_jsonとC実装が一致する()
    {
        var configPath = Path.Combine(ParityDir(), "configs.json");
        var expectedPath = Path.Combine(ParityDir(), "expected.json");
        Assert.True(File.Exists(expectedPath),
            "expected.json が無い。先に TS パリティテスト (npm --prefix web test) で生成すること。");

        var config = JsonSerializer.Deserialize<ParityConfig>(File.ReadAllText(configPath), JsonOpts)!;
        var expected = JsonSerializer.Deserialize<Dictionary<string, List<PatternResult>>>(
            File.ReadAllText(expectedPath), JsonOpts)!;

        var allCards = RepoData.LoadAllCards();

        foreach (var sc in config.Scenarios)
        {
            var plan = RepoData.LoadPlan(sc.PlanId ?? config.PlanId);
            var actual = ComputeScenario(plan, allCards, sc);
            var exp = expected[sc.Id];
            Assert.Equal(exp.Count, actual.Count);
            for (int i = 0; i < exp.Count; i++)
            {
                bool sameIds = exp[i].Ids.SequenceEqual(actual[i].Ids);
                Assert.True(sameIds && exp[i].Total == actual[i].Total,
                    $"[{sc.Id}] pattern#{i}: " +
                    $"sameDeck={sameIds} / total TS={exp[i].Total} C#={actual[i].Total} / " +
                    $"TS_ids=[{string.Join(",", exp[i].Ids)}] C#_ids=[{string.Join(",", actual[i].Ids)}]");
            }
        }
    }
}
