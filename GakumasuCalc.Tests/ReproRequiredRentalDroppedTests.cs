using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト (ユーザ報告 2026-08): hatsu_legend/anomaly で未所持の必須カード
/// ｖギャルピーーースッｖ (SP_SSR_0073, vo型SP) がレンタル枠を他カードに吸われ編成から漏れる。
/// TS の cardScoring.requiredRentalDropped.test.ts と対。
/// 雨夜燕 / 所持カードのみ ON / SP vo3/da2 / 必須3枚 = 0019(vo SP, 所持) + 0014(da 非SP, 所持)
/// + 0073(vo SP, 未所持=レンタル必須)。
///
/// バグ: ステップ0で未所持必須カードは requiredRentalCard として保留されるが、そのSP率が
///       spCountsForFill から減算されない。ステップ1が vo SP を1枚過剰確保して所持枠が
///       2(必須)+4(SP補充)=6枚に達し、「レンタル1枠」ブロックの selected.Count &lt; 6 が偽になって
///       requiredRentalCard が黙って捨てられる (EnsureRentalSlot は枠を立て直すだけで必須は戻さない)。
/// 修正: ①ステップ0で必須レンタルカードのSP率も spCountsForFill から減算、
///       ②レンタル投入前に所持枠が埋まっていたら最弱の非必須カードを落として必ず枠を空ける。
///
/// 検証: 全パターンで 6枚・重複なし・必須3枚全含有・未所持の 0073 がレンタル枠・SP充足。
/// </summary>
public class ReproRequiredRentalDroppedTests
{
    private readonly ITestOutputHelper _out;
    public ReproRequiredRentalDroppedTests(ITestOutputHelper o) { _out = o; }

    private record InvEntry(string card_id, bool owned, int uncap);

    private static readonly string[] Required = { "SP_SSR_0019", "SP_SSR_0014", "SP_SSR_0073" };
    private const string UnownedRequired = "SP_SSR_0073";

    // 診断情報の日程 (W10/W18 は固定イベントのため選択なし)
    private static readonly Dictionary<int, ActionType> Choices = new()
    {
        [1] = ActionType.DaClass, [2] = ActionType.DaClass, [3] = ActionType.ActivitySupply,
        [4] = ActionType.DaLesson, [5] = ActionType.Consultation, [6] = ActionType.VoClass,
        [7] = ActionType.DaLesson, [8] = ActionType.Consultation, [9] = ActionType.SpecialTraining,
        [11] = ActionType.ActivitySupply, [12] = ActionType.DaLesson, [13] = ActionType.Consultation,
        [14] = ActionType.DaLesson, [15] = ActionType.VoClass, [16] = ActionType.VoLesson,
        [17] = ActionType.Consultation,
    };

    [Fact]
    public void UnownedRequiredCardKeepsRentalSlot()
    {
        var allCards = RepoData.LoadAllCards();
        var plan = RepoData.LoadPlan("hatsu_legend");
        var invPath = Path.Combine(RepoData.RepoRoot(), "TestFixtures", "hif_repro_inventory.json");
        var inventory = JsonSerializer.Deserialize<List<InvEntry>>(File.ReadAllText(invPath))!;

        var ownedIds = inventory.Where(e => e.owned).Select(e => e.card_id).ToHashSet();
        var uncapLevels = new Dictionary<string, int>();
        foreach (var e in inventory) uncapLevels[e.card_id] = e.uncap;
        // 診断時点の凸数に合わせる
        uncapLevels["SP_SSR_0098"] = 2;
        uncapLevels["SP_SR_0069"] = 4;

        var candidateCards = allCards.Where(c => ownedIds.Contains(c.Id)).ToList();
        var rentalPool = allCards.ToList();

        var turnChoices = Choices
            .Select(kvp => new TurnChoice { Week = kvp.Key, ChosenAction = kvp.Value })
            .ToList();

        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 1, ["da"] = 4, ["vi"] = 0 };
        var mainStats = new List<string> { "da", "vo" };
        var spCounts = new Dictionary<string, int> { ["vo"] = 3, ["da"] = 2 };

        // 雨夜燕 (3凸OFF) の実効キャラ補正 (診断値)
        var effectiveChar = new Character
        {
            Id = "char_tsubame", Name = "雨夜燕", Color = "#7B68EE", Initial = "燕",
            BaseStatusBonus = new StatusValues(115, 140, 110),
            ParaBonus = new StatBonusPercent { Vo = 17, Da = 20, Vi = 13 },
        };

        var additionalCounts = new AdditionalCounts
        {
            PDrinkAcquire = 7, PItemAcquire = 6, SkillAcquire = 15, SkillSsrAcquire = 4,
            SkillEnhance = 4, SkillDelete = 5, SkillCustom = 3, SkillChange = 3,
            ActiveEnhance = 3, ActiveDelete = 3, MentalAcquire = 8, MentalEnhance = 3,
            MentalDelete = 3, ActiveAcquire = 8, GenkiAcquire = 8, GoodConditionAcquire = 8,
            GoodImpressionAcquire = 8, ConserveAcquire = 8, ConcentrateAcquire = 8,
            MotivationAcquire = 8, FullpowerAcquire = 8, AggressiveAcquire = 8,
        };

        var memory = new MemoryBonus
        {
            Vo = new MemoryAttributeBonus(2.8, MemoryBonusType.ParaBonus),
            Da = new MemoryAttributeBonus(2.8, MemoryBonusType.ParaBonus),
            Vi = new MemoryAttributeBonus(20, MemoryBonusType.Flat),
        };
        var memoryBonuses = new List<MemoryBonus> { memory, memory, memory, memory };

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, candidateCards, mainStats, lessonAllocation, spCounts, "anomaly",
            additionalCounts, uncapLevels, rentalPool, Required.ToList(), effectiveChar,
            memoryBonuses, turnChoices, null);

        Assert.NotEmpty(patterns);
        foreach (var p in patterns)
        {
            var cards = Constraints.DeckCards(p);
            _out.WriteLine($"{p.Label}: [{string.Join(",", p.SelectedCards.Select(cs => $"{cs.Card.Id}{(cs.IsRental ? "[R]" : "")}{(cs.IsRequired ? "[必]" : "")}"))}]");

            Assert.Equal(6, cards.Count);
            Assert.True(Constraints.HasNoDuplicates(cards), $"{p.Label}: 重複なし");
            var ids = cards.Select(c => c.Id).ToHashSet();
            foreach (var r in Required)
                Assert.True(ids.Contains(r), $"{p.Label}: 必須カード {r} が編成に含まれること");

            // 未所持の必須カードは必ずレンタル枠に乗る
            var rentals = p.SelectedCards.Where(cs => cs.IsRental).ToList();
            Assert.True(rentals.Count == 1, $"{p.Label}: レンタル枠は1枚であるべき (実際 {rentals.Count})");
            Assert.Equal(UnownedRequired, rentals[0].Card.Id);

            // SP枚数も維持される (必須と両立可能な構成)
            Assert.True(Constraints.CountSp(cards, "vo") >= 3, $"{p.Label}: vo SP>=3");
            Assert.True(Constraints.CountSp(cards, "da") >= 2, $"{p.Label}: da SP>=2");
        }
    }
}
