using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.Tests;

/// <summary>
/// アビリティまとめ (行動別) の集計ロジック。Web版 abilitySummary.test.ts と対になるパリティテスト。
/// </summary>
public class AbilitySummaryTests
{
    private static CardEffect Eff(string trigger, string stat, string valueType, double[] values, int? maxCount = null)
        => new() { Trigger = trigger, Stat = stat, ValueType = valueType, Values = values.ToList(), MaxCount = maxCount };

    private static SupportCard Card(string id, params CardEffect[] effects)
        => new() { Id = id, Name = id, Rarity = "ssr", Type = "vo", Plan = "", Effects = effects.ToList() };

    private static CardScoringService.CardScore Score(SupportCard card, bool isRental = false)
        => new() { Card = card, IsRental = isRental, UncapLevel = 4 };

    [Fact]
    public void トリガー属性で合算しTotal降順で並ぶ()
    {
        var selected = new List<CardScoringService.CardScore>
        {
            Score(Card("A", Eff("class_end", "vo", "flat", new double[] { 45 }))),
            Score(Card("B", Eff("class_end", "vo", "flat", new double[] { 30 }))),
            Score(Card("C", Eff("mental_acquire", "vi", "flat", new double[] { 40 }))),
            // 装備(初期値) と パラボ は行動選択で変動しないため除外される
            Score(Card("D", Eff("equip", "vo", "flat", new double[] { 800 }))),
            Score(Card("E", Eff("equip", "vo", "para_bonus", new double[] { 30 }))),
            // max_count=2 → class_end が6回でも2回ぶんしか発火しない
            Score(Card("F", Eff("class_end", "da", "flat", new double[] { 20 }, maxCount: 2))),
        };
        var triggerCounts = new Dictionary<string, int> { ["class_end"] = 6, ["mental_acquire"] = 3 };

        var svc = new CardScoringService();
        var entries = svc.BuildAbilitySummary(selected, triggerCounts, null);

        // class_end グループ合計 = 450(vo)+40(da) = 490 > mental_acquire 120 → 先に来る。
        // class_end 内は Vo→Da の順。
        Assert.Equal(
            new[] { "class_end/vo", "class_end/da", "mental_acquire/vi" },
            entries.Select(e => $"{e.Trigger}/{e.Stat}").ToArray());

        var vo = entries.First(e => e.Trigger == "class_end" && e.Stat == "vo");
        Assert.Equal(75.0, vo.PerFire);
        Assert.Equal(new List<double> { 45, 30 }, vo.Parts);
        Assert.Equal(6, vo.Fires);
        Assert.Equal(450.0, vo.Total);

        var mental = entries.First(e => e.Trigger == "mental_acquire");
        Assert.Equal("メンタル獲得", mental.TriggerName);
        Assert.Equal(120.0, mental.Total);

        // max_count: total は実効回数で正確、表示の fires は行動の発動回数、MaxCount を併記
        var da = entries.First(e => e.Trigger == "class_end" && e.Stat == "da");
        Assert.Equal(20.0, da.PerFire);
        Assert.Equal(6, da.Fires);
        Assert.Equal(2, da.MaxCount); // 上限2回として併記
        Assert.Equal(40.0, da.Total); // 20 × min(6, max_count=2)

        // 上限が行動回数を下回らない通常項目は MaxCount = null
        Assert.Null(vo.MaxCount);

        Assert.DoesNotContain(entries, e => e.Trigger == "equip");
    }

    [Fact]
    public void 通常レッスン終了時トリガーは除外せず日本語表示名でゼロ回表示する()
    {
        var selected = new List<CardScoringService.CardScore>
        {
            Score(Card("N", Eff("vi_normal_end", "vi", "flat", new double[] { 13 }))),
        };
        var svc = new CardScoringService();
        // 理論値計算では発火しない (常にSPレッスン前提で構造上0) が、トリガーとして ×0回 で表示する
        var entries = svc.BuildAbilitySummary(selected, new Dictionary<string, int>(), null);
        Assert.Single(entries);
        Assert.Equal("vi_normal_end", entries[0].Trigger);
        Assert.Equal("Vi通常終了", entries[0].TriggerName); // 生キーではなく日本語表示名
        Assert.Equal(0, entries[0].Fires);
        Assert.Equal(0.0, entries[0].Total);
    }

    [Fact]
    public void 発動回数0でも編成カードの行動アビリティはゼロ回で出る()
    {
        var selected = new List<CardScoringService.CardScore>
        {
            Score(Card("A", Eff("class_end", "vo", "flat", new double[] { 45 }))),
        };
        var svc = new CardScoringService();
        var entries = svc.BuildAbilitySummary(selected, new Dictionary<string, int> { ["class_end"] = 0 }, null);
        Assert.Single(entries);
        Assert.Equal("class_end", entries[0].Trigger);
        Assert.Equal(45.0, entries[0].PerFire);
        Assert.Equal(0, entries[0].Fires);
        Assert.Equal(0.0, entries[0].Total);
        Assert.Null(entries[0].MaxCount); // 0回時は上限を併記しない
    }
}
