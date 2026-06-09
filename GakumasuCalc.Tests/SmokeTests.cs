using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;

namespace GakumasuCalc.Tests;

public class SmokeTests
{
    [Fact]
    public void 実データを読み込める()
    {
        var cards = RepoData.LoadAllCards();
        var plans = RepoData.LoadPlans();
        Assert.True(cards.Count > 50);
        Assert.Contains(plans, p => p.Id == "hatsu_legend");
    }

    [Fact]
    public void SelectMultiplePatternsが編成を返しScoreDeckで採点できる()
    {
        var plan = RepoData.LoadPlan("hatsu_legend");
        var cards = RepoData.LoadAllCards();
        var mainStats = new List<string> { "vo", "da" };
        int lessonWeeks = plan.Schedule.Count(w => w.Lessons.Count > 0 && w.Type != "fixed_event");

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatterns(plan, cards, mainStats, "vi", lessonWeeks);
        Assert.NotEmpty(patterns);

        var best = patterns.OrderByDescending(p => p.TotalValue).First();
        int score = DeckScorer.ScoreDeck(plan, best.SelectedCards.Select(c => c.Card).ToList(), mainStats);
        Assert.True(score > 0);
    }
}
