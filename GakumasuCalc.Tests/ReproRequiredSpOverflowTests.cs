using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト (ユーザ報告 2026-07): hatsu_legend/anomaly で必須4枚 (内 all型SP=食欲, vi型SP=のんびり)
/// + SP指定 da2/vi3 を与えるとデッキが7枚に膨張し、非SPカードがレンタル枠を浪費する。
/// TS の cardScoring.requiredSpOverflow.test.ts と対。
///
/// バグ: ステップ0で all型SP必須カードを spCountsForFill から減算する際 break で1属性しか減算されず、
///       ステップ1が vi SPを過剰確保 → 所持枠が6枚に膨張しレンタルが乗って7枚。
/// 修正: all型SPはカバーする全属性を減算する (break 除去)。
///
/// 検証: 各パターンでデッキは常に6枚・重複なし・SP充足 (da>=2, vi>=3)・必須4枚全含有・レンタル1枚。
/// </summary>
public class ReproRequiredSpOverflowTests
{
    private readonly ITestOutputHelper _out;
    public ReproRequiredSpOverflowTests(ITestOutputHelper o) { _out = o; }

    private static readonly string[] Required =
        { "SP_SSR_0007", "SP_SR_0073", "SP_SSR_0014", "SP_SSR_0036" };

    [Theory]
    [InlineData("da", 2, 3, "Da2/フリー3")] // ユーザ報告のパターン
    [InlineData("vi", 2, 3, "Vi2/フリー3")]
    [InlineData("", 0, 5, "オールフリー")]
    public void RequiredPlusSpCountsStays6Cards(string slotStat, int slotCount, int freeSlots, string name)
    {
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");
        var character = RepoData.LoadCharacters().First(c => c.Id == "char_saki");

        // 所持: anomaly+free の eligible カードを全所持 (ひとりごと SP_SSR_0058 は非所持=レンタル専用)。
        var ownedExclude = new HashSet<string> { "SP_SSR_0058" };
        bool Eligible(SupportCard c) =>
            (string.IsNullOrEmpty(c.Plan) || c.Plan == "anomaly" || c.Plan == "free") && !ownedExclude.Contains(c.Id);
        var eligible = allCards.Where(Eligible).ToList();
        var uncapLevels = new Dictionary<string, int>();
        foreach (var c in eligible) uncapLevels[c.Id] = 4;
        uncapLevels["SP_SSR_0007"] = 2; // 私の目
        uncapLevels["SP_SR_0073"] = 1;  // のんびり

        var cardTypeSlots = new Dictionary<string, int>();
        if (!string.IsNullOrEmpty(slotStat)) cardTypeSlots[slotStat] = slotCount;
        var spCounts = new Dictionary<string, int> { ["da"] = 2, ["vi"] = 3 };
        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 5, ["vi"] = 5 };
        var mainStats = new List<string> { "da", "vi" };

        var svc = new CardScoringService();
        var deck = svc.SelectOptimalDeck(
            plan, eligible, lessonAllocation, cardTypeSlots, mainStats,
            spCounts, "anomaly", null, uncapLevels, allCards,
            freeSlots, Required.ToList(), character, null);

        var cards = Constraints.DeckCards(deck);
        _out.WriteLine($"{name}: {cards.Count}枚 [{string.Join(",", deck.SelectedCards.Select(cs => $"{cs.Card.Id}{(cs.IsRental ? "[R]" : "")}{(cs.IsRequired ? "[必]" : "")}"))}]");

        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards), $"{name}: 重複なし");
        Assert.True(Constraints.CountSp(cards, "da") >= 2, $"{name}: da SP>=2");
        Assert.True(Constraints.CountSp(cards, "vi") >= 3, $"{name}: vi SP>=3");
        var ids = cards.Select(c => c.Id).ToHashSet();
        foreach (var r in Required) Assert.True(ids.Contains(r), $"{name}: 必須 {r} 含有");
        Assert.Equal(1, deck.SelectedCards.Count(cs => cs.IsRental));
    }
}
