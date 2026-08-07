using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: issue #145「SP枚数設定が多いときに『有効な編成パターンが見つかりませんでした』」
/// の C# 版 (TS の cardScoringHif.spTotal6.test.ts と対)。
/// 初Legend / 所持カードのみON相当 / SP Vo=4+Da=2 (合計6) /
/// 必須2枚: 食欲の秋なんです(SP_SSR_0036, as型SP all) + 相手にとって不足なしよ！(SP_SSR_0053, vi型SP・未所持→レンタル)。
///
/// バグ: SelectMultiplePatternsHif のスキップ判定 `spShortage > free` がレンタル枠(6枚目)を
///       吸収容量に数えておらず、SP合計6では4パターン全てがスキップされ結果0件 → エラー表示。
///       下流 (SelectOptimalDeck の Step1 SP先取り + EnforceSpCounts) は同条件で制約を満たす
///       デッキを組めるため、事前スキップ判定だけが唯一のブロッカーだった。
/// 修正: 吸収容量に `rentalPool != null ? 1 : 0` を加算。
///
/// 検証 (答え非依存の不変条件): SP合計6でもパターンが1件以上返り、各パターンは
/// 6枚編成・必須カード全含・SP枚数充足・レンタルちょうど1枚を満たす。
/// teeth: 修正前はパターン0件で赤。
/// </summary>
public class ReproSpTotal6Tests
{
    private static readonly Dictionary<string, int> SpCounts = new() { ["vo"] = 4, ["da"] = 2 };
    private static readonly List<string> Required = new() { "SP_SSR_0036", "SP_SSR_0053" };
    // ユーザ日程相当: Voレッスン2回 / Daレッスン3回
    private static readonly Dictionary<string, int> LessonAllocation = new() { ["vo"] = 2, ["da"] = 3, ["vi"] = 0 };

    private static bool CoversSpStat(SupportCard card, string stat) =>
        card.Effects.Any(e =>
            e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == stat || e.Stat == "all"));

    private static void AssertDeckInvariants(List<CardScoringService.DeckResult> patterns, List<string> requiredIds)
    {
        Assert.NotEmpty(patterns);
        foreach (var p in patterns)
        {
            var cards = p.SelectedCards.Select(cs => cs.Card).ToList();
            Assert.True(cards.Count == 6, $"{p.Label}: 6枚編成であること (実際 {cards.Count})");
            var ids = cards.Select(c => c.Id).ToHashSet();
            foreach (var req in requiredIds)
                Assert.True(ids.Contains(req), $"{p.Label}: 必須カード {req} が編成に含まれること");
            int voSp = cards.Count(c => CoversSpStat(c, "vo"));
            int daSp = cards.Count(c => CoversSpStat(c, "da"));
            Assert.True(voSp >= SpCounts["vo"], $"{p.Label}: VoSP枚数 {voSp} < {SpCounts["vo"]}");
            Assert.True(daSp >= SpCounts["da"], $"{p.Label}: DaSP枚数 {daSp} < {SpCounts["da"]}");
            int rentals = p.SelectedCards.Count(cs => cs.IsRental);
            Assert.True(rentals == 1, $"{p.Label}: レンタル枠が1枚であること (実際 {rentals})");
        }
    }

    [Fact]
    public void RequiredScenario_ReturnsPatternsSatisfyingConstraints()
    {
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");
        // 所持カードのみON相当: 診断情報でレンタル扱いだった SP_SSR_0053 のみ未所持
        var owned = allCards.Where(c => c.Id != "SP_SSR_0053").ToList();
        var rentalPool = allCards.ToList();

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, owned, new List<string> { "vo", "da" }, LessonAllocation, SpCounts, "anomaly",
            additionalCounts: null, uncapLevels: null, rentalPool: rentalPool,
            requiredCardIds: Required);
        AssertDeckInvariants(patterns, Required);
    }

    [Fact]
    public void NoRequiredCards_SpTotal6StillReturnsPatterns()
    {
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, allCards.ToList(), new List<string> { "vo", "da" }, LessonAllocation, SpCounts, "anomaly",
            additionalCounts: null, uncapLevels: null, rentalPool: allCards.ToList());
        AssertDeckInvariants(patterns, new List<string>());
    }

    [Fact]
    public void NoRentalPool_SpTotal6IsStillSkipped()
    {
        // 吸収容量+1はレンタル枠がある場合のみ。レンタルなしで SP合計6 は
        // フリー5パターンでも吸収不能 (6 > 5) のため従来どおり0件になる。
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, allCards.ToList(), new List<string> { "vo", "da" }, LessonAllocation, SpCounts, "anomaly");
        Assert.Empty(patterns);
    }
}
