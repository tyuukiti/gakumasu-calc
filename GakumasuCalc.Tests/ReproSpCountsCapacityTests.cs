using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト (ユーザ報告 2026-09): hatsu_legend/sense で必須3枚 (全て非SP) + SP指定 vo3/vi1 を
/// 与えるとデッキが7枚に膨張する (必須3 + voSP3 + viSP1 = 7)。
/// TS の cardScoring.spCountsCapacity.test.ts と対。
///
/// バグ: ステップ1のSP率先取りがデッキ容量 (所持枠数) を確認せずに spCounts 分を無条件に追加する。
///       必須カードで枠が埋まっている場合に所持枠が溢れ、「レンタル1枠」ブロックも発火しなくなる。
/// 修正: 所持枠上限で先取りを打ち切り、容量が逼迫する場合は all/as 型 (複数属性同時カバー) を優先。
///       EnforceSpCounts は「外すと充足数が要求を割る場合のみ外せない」判定でレンタル枠を補充する。
///
/// 検証: 各パターンでデッキは常に6枚・重複なし・SP充足 (vo>=3, vi>=1)・必須3枚全含有・レンタル1枚。
/// </summary>
public class ReproSpCountsCapacityTests
{
    private readonly ITestOutputHelper _out;
    public ReproSpCountsCapacityTests(ITestOutputHelper o) { _out = o; }

    // あら、奇遇ね / 二人ならあっという間だね / あなたにも作ってあげる！ (いずれも非SP)
    private static readonly string[] Required =
        { "SP_SSR_0005", "SP_SSR_0032", "SP_SSR_0017" };

    [Theory]
    [InlineData("vo", 2, 3, "Vo2/フリー3")] // ユーザ報告のパターン
    [InlineData("", 0, 5, "オールフリー")]
    public void NonSpRequiredPlusSpCountsStays6Cards(string slotStat, int slotCount, int freeSlots, string name)
    {
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");
        var character = RepoData.LoadCharacters().First(c => c.Id == "char_kotone");

        // 所持: sense+free の eligible カードを全所持
        bool Eligible(SupportCard c) =>
            string.IsNullOrEmpty(c.Plan) || c.Plan == "sense" || c.Plan == "free";
        var eligible = allCards.Where(Eligible).ToList();
        var uncapLevels = new Dictionary<string, int>();
        foreach (var c in eligible) uncapLevels[c.Id] = 4;
        uncapLevels["SP_SSR_0005"] = 3; // あら、奇遇ね
        uncapLevels["SP_SSR_0017"] = 2; // あなたにも作ってあげる！
        uncapLevels["SP_SSR_0079"] = 3; // レディ・セット、ゴー！

        var cardTypeSlots = new Dictionary<string, int>();
        if (!string.IsNullOrEmpty(slotStat)) cardTypeSlots[slotStat] = slotCount;
        var spCounts = new Dictionary<string, int> { ["vo"] = 3, ["vi"] = 1 };
        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 3, ["da"] = 0, ["vi"] = 2 };
        var mainStats = new List<string> { "vo", "vi" };

        var svc = new CardScoringService();
        var deck = svc.SelectOptimalDeck(
            plan, eligible, lessonAllocation, cardTypeSlots, mainStats,
            spCounts, "sense", null, uncapLevels, allCards,
            freeSlots, Required.ToList(), character, null);

        var cards = Constraints.DeckCards(deck);
        _out.WriteLine($"{name}: {cards.Count}枚 [{string.Join(",", deck.SelectedCards.Select(cs => $"{cs.Card.Id}{(cs.IsRental ? "[R]" : "")}{(cs.IsRequired ? "[必]" : "")}"))}]");

        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards), $"{name}: 重複なし");
        Assert.True(Constraints.CountSp(cards, "vo") >= 3, $"{name}: vo SP>=3");
        Assert.True(Constraints.CountSp(cards, "vi") >= 1, $"{name}: vi SP>=1");
        var ids = cards.Select(c => c.Id).ToHashSet();
        foreach (var r in Required) Assert.True(ids.Contains(r), $"{name}: 必須 {r} 含有");
        Assert.Equal(1, deck.SelectedCards.Count(cs => cs.IsRental));
    }
}
