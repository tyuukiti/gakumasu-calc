using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using static GakumasuCalc.Tests.Helpers.Factories;
using static GakumasuCalc.Services.CardScoringService;

namespace GakumasuCalc.Tests;

/// <summary>
/// L1: 制約遵守・決定性の不変条件テスト (TS 版 cardScoring.constraints.test.ts と対応)。
/// 採点に依存しない構造的性質なので、スコア調整があっても壊れない広いセーフティネット。
/// </summary>
public class ConstraintTests
{
    private static readonly CardScoringService Svc = new();
    private static Dictionary<string, int> Alloc() => new() { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };

    private static DeckResult Select(
        TrainingPlan plan, List<SupportCard> pool,
        Dictionary<string, int> cardTypeSlots, int freeSlots, List<string> mainStats,
        Dictionary<string, int>? spCounts = null, List<string>? requiredCardIds = null)
        => Svc.SelectOptimalDeck(
            plan, pool, Alloc(), cardTypeSlots, mainStats,
            spCounts, null, null, null, null, freeSlots, requiredCardIds);

    private static List<SupportCard> SyntheticPool()
    {
        ResetIds();
        return new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "VO1", Type = "vo", Vo = 200, Sp = new[] { "vo" } }),
            MakeCard(new CardSpec { Id = "VO2", Type = "vo", Vo = 180, Sp = new[] { "vo" } }),
            MakeCard(new CardSpec { Id = "VO3", Type = "vo", Vo = 160 }),
            MakeCard(new CardSpec { Id = "VO4", Type = "vo", Vo = 140 }),
            MakeCard(new CardSpec { Id = "DA1", Type = "da", Da = 190 }),
            MakeCard(new CardSpec { Id = "DA2", Type = "da", Da = 170 }),
            MakeCard(new CardSpec { Id = "DA3", Type = "da", Da = 150 }),
            MakeCard(new CardSpec { Id = "VI1", Type = "vi", Vi = 130 }),
            MakeCard(new CardSpec { Id = "VI2", Type = "vi", Vi = 120 }),
            MakeCard(new CardSpec { Id = "AS1", Type = "all", Vo = 60, Da = 60, Vi = 60 }),
        };
    }

    [Fact]
    public void デッキは常に6枚_重複なし()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 3, ["da"] = 2 }, 1, new() { "vo", "da" });
        var cards = Constraints.DeckCards(deck);
        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards));
    }

    [Fact]
    public void 必須カードは必ず編成に含まれる()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 3, ["da"] = 2 }, 1, new() { "vo", "da" },
            null, new List<string> { "VI2" });
        var ids = Constraints.DeckCards(deck).Select(c => c.Id).ToList();
        Assert.Contains("VI2", ids);
    }

    [Fact]
    public void 属性枠を満たす()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 3, ["da"] = 2 }, 1, new() { "vo", "da" });
        var cards = Constraints.DeckCards(deck);
        Assert.True(Constraints.CountTypeSlotFillable(cards, "vo") >= 3);
        Assert.True(Constraints.CountTypeSlotFillable(cards, "da") >= 2);
    }

    [Fact]
    public void SP枚数を満たせるプールでは要求枚数を満たす()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 2, ["da"] = 2 }, 2, new() { "vo", "da" },
            new Dictionary<string, int> { ["vo"] = 2 });
        Assert.True(Constraints.CountSp(Constraints.DeckCards(deck), "vo") >= 2);
    }

    [Fact]
    public void 必須とSP枚数を同時に満たす()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 2, ["da"] = 2 }, 2, new() { "vo", "da" },
            new Dictionary<string, int> { ["vo"] = 2 }, new List<string> { "DA3" });
        var ids = Constraints.DeckCards(deck).Select(c => c.Id).ToList();
        Assert.Contains("DA3", ids);
        Assert.True(Constraints.CountSp(Constraints.DeckCards(deck), "vo") >= 2);
    }

    // 要望 #138: W先生×Pアイテム3つ等でサポカ5枚が確定する運用向けに、必須カードは
    // デッキ枠と同じ6枚まで登録できる (5枚固定+自動1枠、または全6枠固定の直接評価)。
    [Fact]
    public void 必須カード5枚がすべて編成に含まれ6枚に収まる()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        // 寄与下位を含む5枚を必須指定しても全部入り、自動選出は残り1枠のみ
        var required = new List<string> { "VO3", "VO4", "DA3", "VI1", "VI2" };
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 2, ["da"] = 2 }, 2, new() { "vo", "da" },
            null, required);
        var cards = Constraints.DeckCards(deck);
        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards));
        foreach (var id in required)
            Assert.Contains(id, cards.Select(c => c.Id));
    }

    [Fact]
    public void レンタルありの必須5枚は全必須含有_レンタル1枚_自動選出枠が1枚残る()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var pool = SyntheticPool();
        var required = new List<string> { "VO3", "VO4", "DA3", "VI1", "VI2" };
        var deck = Svc.SelectOptimalDeck(
            plan, pool, Alloc(), new() { ["vo"] = 2, ["da"] = 2 }, new() { "vo", "da" },
            null, null, null, null, pool, 2, required);
        var cards = Constraints.DeckCards(deck);
        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards));
        foreach (var id in required)
            Assert.Contains(id, cards.Select(c => c.Id));
        // 所持のみ運用ではレンタル枠がちょうど1枚
        Assert.Equal(1, deck.SelectedCards.Count(cs => cs.IsRental));
        // 必須5枚以外に自動選出された1枚が存在する
        Assert.Equal(1, cards.Count(c => !required.Contains(c.Id)));
    }

    [Fact]
    public void 必須カード6枚でデッキ全枠が固定される()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var required = new List<string> { "VO1", "VO3", "VO4", "DA3", "VI1", "VI2" };
        var deck = Select(plan, SyntheticPool(), new() { ["vo"] = 2, ["da"] = 2 }, 2, new() { "vo", "da" },
            null, required);
        Assert.Equal(
            required.OrderBy(x => x).ToList(),
            Constraints.DeckCards(deck).Select(c => c.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public void レンタルありの必須6枚はデッキが必須6枚そのもの_借用枠は必須内に1枚割り当たる()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 9999 });
        var pool = SyntheticPool();
        var required = new List<string> { "VO1", "VO3", "VO4", "DA3", "VI1", "VI2" };
        var deck = Svc.SelectOptimalDeck(
            plan, pool, Alloc(), new() { ["vo"] = 2, ["da"] = 2 }, new() { "vo", "da" },
            null, null, null, null, pool, 2, required);
        Assert.Equal(
            required.OrderBy(x => x).ToList(),
            Constraints.DeckCards(deck).Select(c => c.Id).OrderBy(x => x).ToList());
        // 全枠必須でもレンタル(借用)枠は消えず、必須6枚のうち1枚に割り当たる
        Assert.Equal(1, deck.SelectedCards.Count(cs => cs.IsRental));
    }

    [Fact]
    public void 同一入力なら同一出力()
    {
        var plan = MakePlan(new PlanSpec { StatusLimit = 500, BaseVo = 50, BaseDa = 50, BaseVi = 50 });
        var a = Select(plan, SyntheticPool(), new() { ["vo"] = 3, ["da"] = 2 }, 1, new() { "vo", "da" });
        var b = Select(plan, SyntheticPool(), new() { ["vo"] = 3, ["da"] = 2 }, 1, new() { "vo", "da" });
        Assert.Equal(
            a.SelectedCards.Select(c => c.Card.Id).ToList(),
            b.SelectedCards.Select(c => c.Card.Id).ToList());
    }

    [Theory]
    [InlineData("vo", "da", "vi")]
    [InlineData("da", "vi", "vo")]
    [InlineData("vo", "vi", "da")]
    public void 実データ全パターンが6枚_重複なし(string m1, string m2, string sub)
    {
        var plan = RepoData.LoadPlan("hatsu_legend");
        var allCards = RepoData.LoadAllCards();
        var mainStats = new List<string> { m1, m2 };
        int lessonWeeks = plan.Schedule.Count(w => w.Lessons.Count > 0 && w.Type != "fixed_event");

        var patterns = Svc.SelectMultiplePatterns(
            plan, allCards, mainStats, sub, lessonWeeks, new Dictionary<string, int> { [m1] = 1 });
        Assert.NotEmpty(patterns);
        foreach (var p in patterns)
        {
            var cards = Constraints.DeckCards(p);
            Assert.Equal(6, cards.Count);
            Assert.True(Constraints.HasNoDuplicates(cards));
        }
    }
}
