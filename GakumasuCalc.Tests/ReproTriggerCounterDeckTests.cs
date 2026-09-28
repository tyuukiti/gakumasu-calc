using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: 同一カード内の別効果が発動回数カウンタを共有するバグの 6枚編成版
/// (TS の statusCalculation.triggerCounterDeck.test.ts と対。単体版は ReproTriggerCounterPerEffectTests)。
///
/// 実データにある「通常アビリティ + Pアイテム効果 (同トリガー・同属性)」の型を網羅した合成カード 6枚を
/// 1つの編成に入れ、週次処理 (レッスン / 試験 / 休む) と追加イベント回数の両経路で発火させる。
/// 期待値は「効果ごとに独立した上限回数」で手計算した値。
///
///   A 0110型 (1凸): 装備 Vi+20 / スキルチェンジ Vi 27×3 + アイテム 25×3 / 試験終了 Vi 26×2      = 228
///   B 試験型:        試験終了 Vi 26 (5回まで) + アイテム 10 (2回まで)、試験 2回                   =  72
///   C SR_0067型:     VoSP終了 Vo 7 (無制限) + アイテム 20 (2回まで)、Voレッスン 2回              =  54
///   D 0108型:        集中獲得 Da 16 (4回まで) + アイテム 10 (2回まで)、5回                       =  84
///   E SR_0056型:     好印象獲得 Vi 3 (無制限) + アイテム 30 (1回まで)、4回                       =  42
///   F 休む型:        休む Da 8 (2回まで) + アイテム 12 (1回まで)、休む 2回 / スキルチェンジ Da 10 (1回) =  38
///
/// 共有カウンタだと A=151 / B=62 / C=34 / D=58 / E=12 / F=26 になり、全カードが期待値を下回る。
/// 併せて「6枚の寄与 = 各カード単体の寄与の和」(カード間で干渉しない) も確認する。
/// </summary>
public class ReproTriggerCounterDeckTests
{
    private static CardEffect Eff(string trigger, string stat, double[] values, int? max = null, bool item = false)
    {
        var e = new CardEffect { Trigger = trigger, Stat = stat, ValueType = "flat", Values = values.ToList(), MaxCount = max };
        if (item)
        {
            e.Source = "item";
            e.Condition = $"{stat}>=400";
        }
        return e;
    }

    private static double[] Five(double v) => new[] { v, v, v, v, v };

    private static SupportCard Card(string id, string type, params CardEffect[] effects)
    {
        var card = Factories.MakeCard(new Factories.CardSpec { Id = id, Type = type });
        card.Effects.AddRange(effects);
        return card;
    }

    private static readonly SupportCard CardA = Card("A_0110型", "vi",
        new CardEffect { Trigger = "equip", Stat = "vi", ValueType = "flat", Values = Five(20).ToList(), EventParam = true },
        Eff("skill_change", "vi", new double[] { 27, 27, 27, 39, 39 }, max: 3),
        Eff("exam_end", "vi", new double[] { 26, 26, 26, 26, 37 }, max: 5),
        Eff("skill_change", "vi", Five(25), max: 3, item: true));
    private static readonly SupportCard CardB = Card("B_試験型", "vi",
        Eff("exam_end", "vi", Five(26), max: 5), Eff("exam_end", "vi", Five(10), max: 2, item: true));
    private static readonly SupportCard CardC = Card("C_SR0067型", "vo",
        Eff("vo_sp_end", "vo", Five(7)), Eff("vo_sp_end", "vo", Five(20), max: 2, item: true));
    private static readonly SupportCard CardD = Card("D_0108型", "da",
        Eff("concentrate_acquire", "da", Five(16), max: 4), Eff("concentrate_acquire", "da", Five(10), max: 2, item: true));
    private static readonly SupportCard CardE = Card("E_SR0056型", "vi",
        Eff("good_impression_acquire", "vi", Five(3)), Eff("good_impression_acquire", "vi", Five(30), max: 1, item: true));
    private static readonly SupportCard CardF = Card("F_休む型", "da",
        Eff("rest", "da", Five(8), max: 2), Eff("rest", "da", Five(12), max: 1, item: true),
        Eff("skill_change", "da", Five(10), max: 1));

    private static readonly List<SupportCard> Deck = new() { CardA, CardB, CardC, CardD, CardE, CardF };

    private static Dictionary<string, int> UncapLevels()
    {
        var uc = Deck.ToDictionary(c => c.Id, _ => 4);
        uc[CardA.Id] = 1;
        return uc;
    }

    public static IEnumerable<object[]> ExpectedByCard() => new[]
    {
        new object[] { "A 0110型", CardA, new StatusValues(0, 0, 228) },
        new object[] { "B 試験型", CardB, new StatusValues(0, 0, 72) },
        new object[] { "C SR_0067型", CardC, new StatusValues(54, 0, 0) },
        new object[] { "D 0108型", CardD, new StatusValues(0, 84, 0) },
        new object[] { "E SR_0056型", CardE, new StatusValues(0, 0, 42) },
        new object[] { "F 休む型", CardF, new StatusValues(0, 38, 0) },
    };

    /// <summary>Voレッスン×2 → 試験×2 → 休む×2 の 6週</summary>
    private static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlan()
    {
        var plan = Factories.MakePlan(new Factories.PlanSpec { LessonVo = 2, LessonGain = 100 });
        plan.Schedule.Add(new WeekSchedule { Week = 3, Type = "fixed_event", EventName = "試験1" });
        plan.Schedule.Add(new WeekSchedule { Week = 4, Type = "fixed_event", EventName = "試験2" });
        plan.Schedule.Add(new WeekSchedule { Week = 5, Type = "normal", AvailableActions = new() { "rest" } });
        plan.Schedule.Add(new WeekSchedule { Week = 6, Type = "normal", AvailableActions = new() { "rest" } });
        plan.TotalWeeks = 6;
        var turnChoices = new List<TurnChoice>
        {
            new() { Week = 1, ChosenAction = ActionType.VoLesson },
            new() { Week = 2, ChosenAction = ActionType.VoLesson },
            new() { Week = 5, ChosenAction = ActionType.Rest },
            new() { Week = 6, ChosenAction = ActionType.Rest },
        };
        return (plan, turnChoices);
    }

    private static AdditionalCounts Counts() => new() { SkillChange = 3, ConcentrateAcquire = 5, GoodImpressionAcquire = 4 };

    /// <summary>カード群の寄与 = (カードあり) − (カードなし) の最終ステータス差</summary>
    private static StatusValues Contribution(List<SupportCard> cards)
    {
        var (plan, turnChoices) = BuildPlan();
        var svc = new StatusCalculationService();
        var with = svc.Calculate(plan, cards, turnChoices, UncapLevels(), Counts()).FinalStatus;
        var baseline = svc.Calculate(plan, new List<SupportCard>(), turnChoices, UncapLevels(), Counts()).FinalStatus;
        return new StatusValues(with.Vo - baseline.Vo, with.Da - baseline.Da, with.Vi - baseline.Vi);
    }

    private static (int, int, int) T(StatusValues s) => (s.Vo, s.Da, s.Vi);

    [Theory]
    [MemberData(nameof(ExpectedByCard))]
    public void 単体_各カードの寄与が効果ごとの上限回数で計算される(string name, SupportCard card, StatusValues expected)
    {
        var actual = Contribution(new List<SupportCard> { card });
        Assert.True(T(expected) == T(actual), $"{name}: 期待 {T(expected)} 実際 {T(actual)}");
    }

    [Fact]
    public void 六枚編成の寄与が全カードの期待値の合計に一致する()
    {
        Assert.Equal((54, 122, 342), T(Contribution(Deck)));
    }

    [Fact]
    public void 六枚編成の寄与は各カード単体の寄与の和に一致する()
    {
        int vo = 0, da = 0, vi = 0;
        foreach (var card in Deck)
        {
            var c = Contribution(new List<SupportCard> { card });
            vo += c.Vo; da += c.Da; vi += c.Vi;
        }
        Assert.Equal((vo, da, vi), T(Contribution(Deck)));
    }
}
