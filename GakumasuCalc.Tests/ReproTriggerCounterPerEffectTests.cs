using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: 同一カード内の別効果が発動回数カウンタを共有する (Web版 statusCalculation.triggerCounterPerEffect.test.ts と対)。
///
/// FireTrigger の発動回数カウンタのキーが "カードID_トリガー_属性" だったため、同じトリガー・同じ属性の
/// flat 効果を2つ持つカード (通常アビリティ + Pアイテム効果。例: もうすぐ本番ですね SP_SSR_0110) では、
/// 後続の効果が前の効果の消費分だけ早く MaxCount に達し、合計が期待値より低くなっていた。
/// 実例: 0110 を1凸で採用した編成が不採用の編成より合計が低くなる (最適化側は効果ごとに独立に見積もるため採用する)。
///
/// 期待仕様: 同一カード・同一トリガー・同一属性でも、別々の効果はそれぞれ独立した上限回数として扱う。
/// </summary>
public class ReproTriggerCounterPerEffectTests
{
    private static CardEffect TriggerEffect(string trigger, string stat, double[] values, int? max = null, bool item = false)
    {
        var e = new CardEffect
        {
            Trigger = trigger, Stat = stat, ValueType = "flat",
            Values = values.ToList(), MaxCount = max,
        };
        if (item)
        {
            e.Source = "item";
            e.Condition = "vi>=400";
        }
        return e;
    }

    private static SupportCard Card(string type, params CardEffect[] effects)
    {
        var card = Factories.MakeCard(new Factories.CardSpec { Type = type });
        card.Effects.AddRange(effects);
        return card;
    }

    /// <summary>trigger 名 (snake_case) に対応する AdditionalCounts のプロパティへ回数を設定する。</summary>
    private static AdditionalCounts Counts(string trigger, int fires)
    {
        var counts = new AdditionalCounts();
        var propName = string.Concat(trigger.Split('_').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
        var prop = typeof(AdditionalCounts).GetProperty(propName);
        Assert.True(prop != null, $"AdditionalCounts.{propName} が見つからない (trigger={trigger})");
        prop!.SetValue(counts, fires);
        Assert.Equal(fires, counts.ToDictionary()[trigger]);
        return counts;
    }

    /// <summary>スケジュールなしのプランで trigger を fires 回発火し、最終ステータスを返す。</summary>
    private static StatusValues FireAndGet(List<SupportCard> cards, string trigger, int fires, int uncap = 4, int baseVi = 0)
    {
        var plan = Factories.MakePlan(new Factories.PlanSpec { BaseVi = baseVi });
        var uncapLevels = cards.ToDictionary(c => c.Id, _ => uncap);
        return new StatusCalculationService()
            .Calculate(plan, cards, new List<TurnChoice>(), uncapLevels, Counts(trigger, fires))
            .FinalStatus;
    }

    private static double[] Five(double v) => new[] { v, v, v, v, v };

    [Fact]
    public void MaxCountが異なる2効果はそれぞれの上限まで発動する()
    {
        var card = Card("vi",
            TriggerEffect("skill_change", "vi", Five(30), max: 3),
            TriggerEffect("skill_change", "vi", Five(20), max: 2, item: true));
        // 5回発火: 30×3 + 20×2 = 130 (共有カウンタだと 30+20+30 = 80 で止まる)
        Assert.Equal(130, FireAndGet(new List<SupportCard> { card }, "skill_change", 5).Vi);
    }

    [Fact]
    public void 上限なし効果と上限付き効果の組は上限付き側も発動する()
    {
        var card = Card("vi",
            TriggerEffect("good_impression_acquire", "vi", Five(5)),
            TriggerEffect("good_impression_acquire", "vi", Five(30), max: 1, item: true));
        // 4回発火: 5×4 + 30×1 = 50 (共有カウンタだと上限付き側が一度も発動せず 20)
        Assert.Equal(50, FireAndGet(new List<SupportCard> { card }, "good_impression_acquire", 4).Vi);
    }

    [Theory]
    [InlineData(4, 192)] // 39×3 + 25×3
    [InlineData(1, 156)] // 27×3 + 25×3
    [InlineData(0, 156)]
    public void 凸数によらず各効果が上限回数まで発動する(int uncap, int expected)
    {
        var card = Card("vi",
            TriggerEffect("skill_change", "vi", new double[] { 27, 27, 27, 39, 39 }, max: 3),
            TriggerEffect("skill_change", "vi", Five(25), max: 3, item: true));
        Assert.Equal(expected, FireAndGet(new List<SupportCard> { card }, "skill_change", 3, uncap).Vi);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public void 実データ_もうすぐ本番ですね_スキルチェンジ3回(int uncap)
    {
        var card = RepoData.GetCard("SP_SSR_0110");
        var changes = card.Effects
            .Where(e => e.Trigger == "skill_change" && e.ValueType == "flat" && e.Stat == "vi")
            .ToList();
        // 前提: 通常アビリティ + Pアイテム効果の2効果 (データが変わったらこのテストの意味も変わる)
        Assert.Equal(2, changes.Count);

        // 初期値ボーナス: EventParam 付き flat には同カードの event_param_boost% が乗る
        var boostMul = 1.0 + card.GetEventParamBoostPercent(uncap) / 100.0;
        int equip = card.Effects
            .Where(e => e.Trigger == "equip" && e.ValueType == "flat" && e.Stat == "vi")
            .Sum(e =>
            {
                var raw = e.GetValue(uncap);
                return (int)(e.EventParam ? raw * boostMul : raw);
            });
        int expected = changes.Sum(e => (int)e.GetValue(uncap) * Math.Min(3, e.MaxCount ?? 3));

        // Pアイテム条件 (vi>=400) を満たすよう初期 Vi=400
        var fs = FireAndGet(new List<SupportCard> { card }, "skill_change", 3, uncap, baseVi: 400);
        Assert.Equal(400 + equip + expected, fs.Vi);
    }

    [Fact]
    public void 別カードの同一トリガー同一属性は互いに独立()
    {
        var a = Card("vi", TriggerEffect("skill_change", "vi", Five(10), max: 1));
        var b = Card("vi", TriggerEffect("skill_change", "vi", Five(10), max: 1));
        Assert.Equal(20, FireAndGet(new List<SupportCard> { a, b }, "skill_change", 2).Vi);
    }

    [Fact]
    public void 同一カードの同一トリガー別属性は互いに独立()
    {
        var card = Card("all",
            TriggerEffect("skill_change", "vo", Five(10), max: 1),
            TriggerEffect("skill_change", "vi", Five(10), max: 1));
        var fs = FireAndGet(new List<SupportCard> { card }, "skill_change", 2);
        Assert.Equal(10, fs.Vo);
        Assert.Equal(10, fs.Vi);
    }
}
