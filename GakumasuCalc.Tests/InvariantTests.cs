using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using static GakumasuCalc.Tests.Helpers.Factories;
using static GakumasuCalc.Services.CardScoringService;

namespace GakumasuCalc.Tests;

/// <summary>
/// L1: 不変条件テスト (合成フィクスチャ + 総当たりオラクル)。TS 版 cardScoring.invariant.test.ts と対応。
/// デッキは (レンタルなしのとき) 常に 6 枚。cardTypeSlots は属性ごとの最低枚数。
/// 採点は実 Calculate の cap 後合計。自動 == 総当たり最適 を検証する。
/// </summary>
public class InvariantTests
{
    private const int DeckSize = 6;
    private static readonly CardScoringService Svc = new();

    private static Dictionary<string, int> Alloc() => new() { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };

    private static DeckResult AutoSelect(
        TrainingPlan plan, List<SupportCard> pool,
        Dictionary<string, int> cardTypeSlots, int freeSlots, List<string> mainStats)
        => Svc.SelectOptimalDeck(
            plan, pool, Alloc(), cardTypeSlots, mainStats,
            null, null, null, null, null, freeSlots);

    private static int AutoScore(
        TrainingPlan plan, List<SupportCard> pool,
        Dictionary<string, int> cardTypeSlots, int freeSlots, List<string> mainStats)
    {
        var deck = AutoSelect(plan, pool, cardTypeSlots, freeSlots, mainStats);
        return DeckScorer.ScoreDeck(plan, deck.SelectedCards.Select(c => c.Card).ToList(), mainStats);
    }

    [Fact]
    public void 上限張り付きトラップ_同属性を積み過ぎず均等配分を選ぶ()
    {
        ResetIds();
        var plan = MakePlan(new PlanSpec { StatusLimit = 250, BaseVo = 0, BaseDa = 0, BaseVi = 0 });
        var pool = new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "VO1", Type = "vo", Vo = 200 }),
            MakeCard(new CardSpec { Id = "VO2", Type = "vo", Vo = 200 }),
            MakeCard(new CardSpec { Id = "VO3", Type = "vo", Vo = 200 }),
            MakeCard(new CardSpec { Id = "DA1", Type = "da", Da = 200 }),
            MakeCard(new CardSpec { Id = "DA2", Type = "da", Da = 200 }),
            MakeCard(new CardSpec { Id = "DA3", Type = "da", Da = 200 }),
            MakeCard(new CardSpec { Id = "VI1", Type = "vi", Vi = 120 }),
            MakeCard(new CardSpec { Id = "VI2", Type = "vi", Vi = 120 }),
        };
        var mainStats = new List<string> { "vo", "da" };
        int auto = AutoScore(plan, pool, new(), DeckSize, mainStats);
        var oracle = BruteForce.FindOptimalDeck(plan, pool, new(), DeckSize, mainStats);
        Assert.Equal(740, oracle.BestTotal); // 250 + 250 + 240
        Assert.Equal(oracle.BestTotal, auto);
    }

    [Fact]
    public void 上限なし_寄与合計が最大の6枚を選ぶ()
    {
        ResetIds();
        var plan = MakePlan(new PlanSpec { StatusLimit = 999999 });
        var pool = new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "A", Type = "vo", Vo = 300 }),
            MakeCard(new CardSpec { Id = "B", Type = "vo", Vo = 250 }),
            MakeCard(new CardSpec { Id = "C", Type = "da", Da = 240 }),
            MakeCard(new CardSpec { Id = "D", Type = "da", Da = 220 }),
            MakeCard(new CardSpec { Id = "E", Type = "vi", Vi = 210 }),
            MakeCard(new CardSpec { Id = "F", Type = "vi", Vi = 80 }),
            MakeCard(new CardSpec { Id = "G", Type = "vo", Vo = 70 }),
            MakeCard(new CardSpec { Id = "H", Type = "da", Da = 60 }),
        };
        var mainStats = new List<string> { "vo", "da" };
        int auto = AutoScore(plan, pool, new(), DeckSize, mainStats);
        var oracle = BruteForce.FindOptimalDeck(plan, pool, new(), DeckSize, mainStats);
        Assert.Equal(oracle.BestTotal, auto);
    }

    [Fact]
    public void 属性枠あり_vo2da2フリー2でも総当たり最適に一致()
    {
        ResetIds();
        var plan = MakePlan(new PlanSpec { StatusLimit = 600, BaseVo = 50, BaseDa = 50, BaseVi = 50 });
        var pool = new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "VO1", Type = "vo", Vo = 180 }),
            MakeCard(new CardSpec { Id = "VO2", Type = "vo", Vo = 120 }),
            MakeCard(new CardSpec { Id = "VO3", Type = "vo", Vo = 90 }),
            MakeCard(new CardSpec { Id = "DA1", Type = "da", Da = 200 }),
            MakeCard(new CardSpec { Id = "DA2", Type = "da", Da = 140 }),
            MakeCard(new CardSpec { Id = "DA3", Type = "da", Da = 60 }),
            MakeCard(new CardSpec { Id = "VI1", Type = "vi", Vi = 300 }),
            MakeCard(new CardSpec { Id = "AS1", Type = "all", Vo = 50, Da = 50, Vi = 50 }),
        };
        var mainStats = new List<string> { "vo", "da" };
        var cardTypeSlots = new Dictionary<string, int> { ["vo"] = 2, ["da"] = 2 };
        int auto = AutoScore(plan, pool, cardTypeSlots, 2, mainStats);
        var oracle = BruteForce.FindOptimalDeck(plan, pool, cardTypeSlots, 2, mainStats);
        Assert.Equal(oracle.BestTotal, auto);
    }

    [Fact]
    public void 自動は総当たりのあらゆる手動編成に劣らない()
    {
        ResetIds();
        var plan = MakePlan(new PlanSpec { StatusLimit = 400, BaseVo = 20, BaseDa = 20, BaseVi = 20 });
        var pool = new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "VO1", Type = "vo", Vo = 220, ParaVo = 10 }),
            MakeCard(new CardSpec { Id = "VO2", Type = "vo", Vo = 130 }),
            MakeCard(new CardSpec { Id = "DA1", Type = "da", Da = 210 }),
            MakeCard(new CardSpec { Id = "DA2", Type = "da", Da = 150 }),
            MakeCard(new CardSpec { Id = "VI1", Type = "vi", Vi = 260 }),
            MakeCard(new CardSpec { Id = "VI2", Type = "vi", Vi = 110 }),
            MakeCard(new CardSpec { Id = "AS1", Type = "all", Vo = 60, Da = 60, Vi = 60 }),
            MakeCard(new CardSpec { Id = "AS2", Type = "all", Vo = 40, Da = 40, Vi = 40 }),
        };
        var mainStats = new List<string> { "vo", "vi" };
        var cardTypeSlots = new Dictionary<string, int> { ["vo"] = 2, ["vi"] = 2 };
        int auto = AutoScore(plan, pool, cardTypeSlots, 2, mainStats);
        var oracle = BruteForce.FindOptimalDeck(plan, pool, cardTypeSlots, 2, mainStats);
        Assert.True(auto >= oracle.BestTotal);
    }
}
