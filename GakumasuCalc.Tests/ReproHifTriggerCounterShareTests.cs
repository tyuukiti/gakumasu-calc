using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: ユーザ報告 (2026-09) HIF「もうすぐ本番ですね (SP_SSR_0110) を採用した方が数字が下がる」の C# 版
/// (TS の statusCalculationHif.triggerCounterShare.test.ts と対)。
///
/// 報告に添えられた共有 URL 2本 (アノマリー / 花海咲季 / 3凸OFF / STEP4 ON / ボーナスLv全MAX、
/// 日程・試験配分・イベント回数・メモリーは同一) を復号したフィクスチャ TestFixtures/hif_trigger_counter_share.json を使う。
/// 差は編成 1 枚だけ: 0110 (1凸) が入るか、代わりに 佑芽ソリレース、疾走！ (SP_SR_0063) が入るか。
///
/// バグ: FireTrigger の発動回数カウンタが "カードID_トリガー_属性" キーで、0110 の通常アビリティと
///       Pアイテム効果 (どちらもスキルチェンジ / Vi / 3回) が上限回数を共有し、3回で 27+25+27 しか出なかった。
///       最適化側は効果ごとに独立に見積もる (156) ため 0110 を採用し、実計算では 0063 の編成を下回った。
///
/// 検証: 0110 (1凸) を採用した編成の cap 後合計が、0063 の編成を上回る (自動ピックの判断が正しい)。
/// 再構築規則 (Helpers/HifScenario) は TS 側でストア復元と一致することを検証済み。
/// teeth: 修正前は 6705 &lt; 6720 で赤。修正後は 6782 &gt; 6720 で緑。
/// </summary>
public class ReproHifTriggerCounterShareTests
{
    private readonly ITestOutputHelper _out;
    public ReproHifTriggerCounterShareTests(ITestOutputHelper o) { _out = o; }

    private record DeckCard(string id, int uncap, bool rental, bool required);
    private record MemAttr(double value, string type);
    private record Mem(MemAttr vo, MemAttr da, MemAttr vi);
    private record Triple(double vo, double da, double vi);
    private record CalcSection(
        string planType, string? characterId, bool uncap3, bool step4,
        Dictionary<string, int> additionalCounts, List<Mem> memoryBonuses);
    private record HifSection(
        Dictionary<string, HifScenario.Choice> scheduleChoices,
        Dictionary<string, Triple> examAllocations,
        Dictionary<string, int> bonusLevels);
    private record Fixture(List<DeckCard> deck, List<DeckCard> deck_without_0110, CalcSection calc, HifSection hif);

    private static MemoryAttributeBonus Attr(MemAttr a) => new()
    {
        Value = a.value,
        Type = a.type == "para" ? MemoryBonusType.ParaBonus : MemoryBonusType.Flat,
    };

    [Fact]
    public void DeckWith0110ScoresHigherThanDeckWithout()
    {
        var path = Path.Combine(RepoData.RepoRoot(), "TestFixtures", "hif_trigger_counter_share.json");
        var fx = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path))!;

        // フィクスチャの前提: 差は 0110 (1凸) ↔ SR_0063 の1枚だけ
        var with0110 = fx.deck.Single(d => d.id == "SP_SSR_0110");
        Assert.Equal((1, false), (with0110.uncap, with0110.rental));
        Assert.DoesNotContain(fx.deck_without_0110, d => d.id == "SP_SSR_0110");
        Assert.Equal(
            fx.deck.Where(d => d.id != "SP_SSR_0110").Select(d => d.id).OrderBy(x => x),
            fx.deck_without_0110.Where(d => d.id != "SP_SR_0063").Select(d => d.id).OrderBy(x => x));

        var allCards = RepoData.LoadAllCards();
        var examAlloc = fx.hif.examAllocations.ToDictionary(
            kv => kv.Key,
            kv => new StatusValues((int)Math.Floor(kv.Value.vo), (int)Math.Floor(kv.Value.da), (int)Math.Floor(kv.Value.vi)));
        var (plan, turnChoices) = HifScenario.BuildPlanAndChoices(RepoData.LoadPlan("hif"), fx.hif.scheduleChoices, examAlloc);
        var levels = HifScenario.LevelsFrom(fx.hif.bonusLevels);
        HifScenario.ApplyFinalCap(plan, levels);
        var character = RepoData.LoadCharacters().FirstOrDefault(c => c.Id == fx.calc.characterId);
        var effectiveChar = HifScenario.BuildEffectiveCharacter(character, fx.calc.uncap3, fx.calc.step4, levels);
        var additionalCounts = HifScenario.AdditionalCountsFrom(fx.calc.additionalCounts);
        var memoryBonuses = fx.calc.memoryBonuses
            .Select(m => new MemoryBonus { Vo = Attr(m.vo), Da = Attr(m.da), Vi = Attr(m.vi) })
            .ToList();

        var calc = new StatusCalculationService();
        int cap = plan.StatusLimit;
        (int total, StatusValues fs) Score(List<DeckCard> deck)
        {
            var cards = deck.Select(d => allCards.First(c => c.Id == d.id)).ToList();
            var uc = deck.ToDictionary(d => d.id, d => d.rental ? 4 : d.uncap);
            var fs = calc.Calculate(plan, cards, turnChoices, uc, additionalCounts, effectiveChar, memoryBonuses).FinalStatus;
            return (Math.Min(fs.Vo, cap) + Math.Min(fs.Da, cap) + Math.Min(fs.Vi, cap), fs);
        }

        var without = Score(fx.deck_without_0110);
        var with = Score(fx.deck);
        _out.WriteLine($"cap={cap} without0110={without.total} ({without.fs.Vo}/{without.fs.Da}/{without.fs.Vi}) with0110={with.total} ({with.fs.Vo}/{with.fs.Da}/{with.fs.Vi})");

        // 比較が Vi の差を反映するよう、両編成とも Vi が上限に張り付いていないこと
        Assert.True(without.fs.Vi < cap);
        Assert.True(with.fs.Vi < cap);
        Assert.True(with.total > without.total,
            $"0110 採用 ({with.total}) が不採用 ({without.total}) を下回った: 同一カード内の通常アビリティと Pアイテム効果が発動回数の上限を共有している");
    }
}
