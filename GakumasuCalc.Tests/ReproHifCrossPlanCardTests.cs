using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: ユーザ報告(2026-09) HIF「別プラン (sense) のサポカが1枚だけ選出される」の C# 版
/// (TS の cardScoringHif.crossPlanCard.test.ts と対)。
/// 十王星南 / logic / 所持カードのみ ON / VoSP3 /
/// 必須3枚: 私の目に狂いはない(SP_SSR_0007, 3凸) / やっと見つけたぞ！(SP_SSR_0081, 2凸) /
/// 進化したお弁当、気になる(SP_SSR_0094, 1凸)。
/// 診断情報ではレンタル枠に もう一度、最初から！(SP_SSR_0044, plan: sense) が入っていた。
///
/// 原因: 必須3枚で所持枠が3つ埋まり、ステップ1のSP先取りは所持枠上限 (5) で打ち切られるため
///       VoSP は所持2枚までしか確保できない。残り1枚は EnforceSpCounts が「レンタル枠を
///       レンタルプールの VoSP カードに差し替える」経路で補充するが、この経路だけ
///       rentalPool を planType でフィルタしておらず、別プラン (sense/anomaly) の SP カードが
///       素の寄与順で先頭に来るとそのまま採用される (入口はこの1経路なので混入は最大1枚)。
///       同じ差し替えは必須カードが載ったレンタル枠 (EnsureRentalSlot が低凸の必須カードを
///       借用先に指定したケース) も対象にしてしまい、必須カードがデッキから消える。
///       さらに別プランカードが所持カードだった場合、後続の OptimizeRentalAssignment が
///       レンタルフラグを別カードへ付け替えるため「所持サポカが別プラン」にも見える。
///
/// 検証 (答え非依存の不変条件): 全パターンで 6枚・重複なし・必須3枚全含有・VoSP>=3・
/// レンタル1枚、かつ必須指定でない全カードが planType (logic) または free/無指定 であること。
/// teeth: 修正前は
///   - ケース「報告編成の再現」: 診断情報と同一の編成 (0044 sense がレンタル) になり赤
///   - ケース「プラン内全4凸所持」: 5枚編成・必須 0094 脱落・0044 sense レンタルで赤
///   - ケース「全カード所持」: 0044 sense が所持枠に残り (レンタルは必須 0081 へ付替) 赤
/// </summary>
public class ReproHifCrossPlanCardTests
{
    private readonly ITestOutputHelper _out;
    public ReproHifCrossPlanCardTests(ITestOutputHelper o) { _out = o; }

    private const string PlanType = "logic";
    private static readonly string[] Required = { "SP_SSR_0007", "SP_SSR_0081", "SP_SSR_0094" };
    // 診断情報で所持枠に入っていた VoSP 2枚 (可愛いと可愛いで可愛い！ / 今はあえて、背を向けて)
    private static readonly HashSet<string> ReportedOwnedVoSp = new() { "SP_SSR_0008", "SP_SR_0014" };

    private static bool PlanOk(SupportCard c) =>
        string.IsNullOrEmpty(c.Plan) || c.Plan == PlanType || c.Plan == "free";

    /// <summary>
    /// 所持セットの定義。診断情報にインベントリは含まれないため、報告編成を再現する所持条件を
    /// 再構築する。共通: 必須3枚は報告の凸数 (0094:1 / 0007:3 / 0081:2)、それ以外の所持は4凸。
    /// </summary>
    public static IEnumerable<object[]> Inventories()
    {
        // 報告編成の再現: logic/free のうち非必須の vi 型カードは未所持 (レンタル候補が非SPのみになる)、
        // VoSP は報告どおり 0008 / SR_0014 の2枚のみ所持。
        // → Pattern A/B/C は非SPの vi カードをレンタルに選び、EnforceSpCounts がそれを
        //   レンタルプール先頭の VoSP (= 0044 sense) に差し替える。
        yield return new object[] { "報告編成の再現 (VoSP は 0008/SR_0014 のみ所持・vi 型非必須は未所持)", "reported" };
        // プラン内カードを全て4凸所持: 4凸所持カードはレンタル候補から除外されるため
        // 候補が (デッキ内の) 必須カードだけになり、レンタル枠が立たず EnsureRentalSlot が
        // 低凸の必須 0094 を借用先に指定 → EnforceSpCounts がその必須カードを 0044 に差し替える。
        yield return new object[] { "プラン内 (logic/free) 全4凸所持", "eligible" };
        // 全カード所持 (別プラン含む): 0044 が所持カードなので、レンタル差し替え後に
        // OptimizeRentalAssignment がレンタルフラグを低凸の必須カードへ付け替え、
        // 0044 (sense) が所持枠に残る = 「所持サポカが別プラン」の症状。
        yield return new object[] { "全カード所持 (別プラン含む)", "all" };
    }

    private static bool Owned(SupportCard c, string inventory) => inventory switch
    {
        "reported" => PlanOk(c)
            && !(c.Type == "vi" && !Required.Contains(c.Id))
            && (!Constraints.CoversSpStat(c, "vo") || ReportedOwnedVoSp.Contains(c.Id)),
        "eligible" => PlanOk(c),
        _ => true,
    };

    [Theory]
    [MemberData(nameof(Inventories))]
    public void NonRequiredCardsStayWithinPlan(string name, string inventory)
    {
        var allCards = RepoData.LoadAllCards();
        var hifPlan = RepoData.LoadPlan("hif");

        var ownedIds = allCards.Where(c => Owned(c, inventory)).Select(c => c.Id).ToHashSet();
        // buildUncapLevels(ownedOnly=true) 相当: 未所持カードも uncap=4 でエントリされる
        var uncapLevels = new Dictionary<string, int>();
        foreach (var c in allCards) uncapLevels[c.Id] = 4;
        uncapLevels["SP_SSR_0094"] = 1; // 進化したお弁当、気になる
        uncapLevels["SP_SSR_0007"] = 3; // 私の目に狂いはない
        uncapLevels["SP_SSR_0081"] = 2; // やっと見つけたぞ！

        var candidateCards = allCards.Where(c => ownedIds.Contains(c.Id)).ToList();
        var rentalPool = allCards.ToList();

        var (plan, turnChoices) = BuildPlanAndChoices(hifPlan);
        plan.StatusLimit += 200; // 本戦上限増加 Lv6 → 3200

        // 十王星南 + HIF Lv5: 診断情報の「キャラ補正(実効)」をそのまま使う
        var effectiveChar = new Character
        {
            Id = "char_sena", Name = "十王星南", Color = "#F6AE54", Initial = "星",
            BaseStatusBonus = new StatusValues(175, 125, 140),
            ParaBonus = new StatBonusPercent { Vo = 15, Da = 8, Vi = 20.5 },
        };

        var additionalCounts = new AdditionalCounts
        {
            PDrinkAcquire = 16, PItemAcquire = 6, SkillAcquire = 20, SkillSsrAcquire = 4,
            SkillEnhance = 4, SkillDelete = 8, SkillCustom = 3, SkillChange = 3,
            ActiveEnhance = 3, ActiveDelete = 3, MentalAcquire = 8, MentalEnhance = 1,
            MentalDelete = 3, ActiveAcquire = 8, GenkiAcquire = 8, GoodConditionAcquire = 8,
            GoodImpressionAcquire = 8, ConserveAcquire = 8, ConcentrateAcquire = 8,
            MotivationAcquire = 8, FullpowerAcquire = 8, AggressiveAcquire = 8,
        };

        MemoryBonus Memory() => new()
        {
            Vo = new MemoryAttributeBonus(2.8, MemoryBonusType.ParaBonus),
            Da = new MemoryAttributeBonus(20, MemoryBonusType.Flat),
            Vi = new MemoryAttributeBonus(2.8, MemoryBonusType.ParaBonus),
        };
        var memoryBonuses = new List<MemoryBonus> { Memory(), Memory(), Memory(), Memory() };

        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            if (tc.ChosenAction == ActionType.VoLesson) lessonAllocation["vo"]++;
            else if (tc.ChosenAction == ActionType.DaLesson) lessonAllocation["da"]++;
            else if (tc.ChosenAction == ActionType.ViLesson) lessonAllocation["vi"]++;
        }
        var mainStats = new List<string> { "vo", "da" }; // inferMainStats: Voレッスンのみ → vo, タイブレークで da
        var spCounts = new Dictionary<string, int> { ["vo"] = 3 };
        var overflowPenalty = new CardScoringService.OverflowPenaltyConfig { Threshold = 100 };

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, candidateCards, mainStats, lessonAllocation, spCounts, PlanType,
            additionalCounts, uncapLevels, rentalPool, Required.ToList(), effectiveChar,
            memoryBonuses, turnChoices, overflowPenalty);
        Assert.NotEmpty(patterns);

        foreach (var p in patterns)
        {
            var cards = Constraints.DeckCards(p);
            var desc = string.Join(", ", p.SelectedCards.Select(cs =>
                $"{cs.Card.Id}({cs.Card.Plan}){(cs.IsRental ? "[R]" : "")}{(cs.IsRequired ? "[必]" : "")}"));
            _out.WriteLine($"{name} / {p.Label}: [{desc}]");

            // 本題: 必須指定でないカードに別プランが混ざらない (レンタル・所持を問わず)
            var crossPlan = p.SelectedCards.Where(cs => !cs.IsRequired && !PlanOk(cs.Card)).ToList();
            Assert.True(crossPlan.Count == 0,
                $"{p.Label}: 別プランのカードが選出された {string.Join(",", crossPlan.Select(cs => $"{cs.Card.Id}({cs.Card.Plan})"))} [{desc}]");

            // 基本不変条件 (必須 > SP枚数 > 編成パターン / 6枚 / レンタル1枚)
            Assert.True(cards.Count == 6, $"{p.Label}: 6枚編成 [{desc}]");
            Assert.True(Constraints.HasNoDuplicates(cards), $"{p.Label}: 重複なし");
            var ids = cards.Select(c => c.Id).ToHashSet();
            foreach (var r in Required) Assert.True(ids.Contains(r), $"{p.Label}: 必須 {r} 含有 [{desc}]");
            Assert.True(Constraints.CountSp(cards, "vo") >= 3, $"{p.Label}: VoSP>=3 [{desc}]");
            Assert.Equal(1, p.SelectedCards.Count(cs => cs.IsRental));
        }
    }

    // hifStore.buildPlanAndChoices の複製 (診断情報の 十王星南 logic スケジュール選択を再構築)
    private static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlanAndChoices(TrainingPlan hifPlan)
    {
        var choices = new Dictionary<int, (string action, string? sub)>
        {
            [1] = ("activity_supply", null), [2] = ("vo_lesson", "vi"), [3] = ("vi_class", null),
            [4] = ("vo_lesson", "vi"), [5] = ("outing", null), [6] = ("vi_class", null),
            [8] = ("outing", null), [9] = ("vo_lesson", "vi"), [10] = ("vi_class", null),
            [11] = ("vo_lesson", "vi"), [12] = ("consultation", null), [14] = ("outing", null),
            [15] = ("vo_lesson", "vi"), [16] = ("consultation", null), [17] = ("vi_class", null),
            [18] = ("vo_lesson", "vi"), [19] = ("consultation", null), [21] = ("vi_class", null),
            [22] = ("vo_lesson", "vi"), [23] = ("activity_supply", null), [24] = ("vi_class", null),
            [25] = ("vo_lesson", "vi"), [26] = ("consultation", null),
        };
        var examAlloc = new Dictionary<int, StatusValues>
        {
            [7] = new(0, 0, 80), [13] = new(0, 0, 200), [20] = new(0, 0, 220),
        };

        var newSchedule = hifPlan.Schedule.Select(w =>
        {
            if (w.Type == "public_lesson" && choices.TryGetValue(w.Week, out var ch) && ch.sub != null)
            {
                var mainStat = ch.action.Split('_')[0];
                var mainLesson = w.Lessons.FirstOrDefault(l => l.Type == mainStat);
                int mainValue = mainLesson != null ? GetStat(mainLesson.SpBonus, mainStat) : 0;
                int subValue = w.HifSubValue ?? 0;
                var newLessons = w.Lessons.Select(l =>
                {
                    if (l.Type != mainStat) return l;
                    var sp = new StatusValues(0, 0, 0);
                    SetStat(sp, mainStat, mainValue);
                    SetStat(sp, ch.sub!, GetStat(sp, ch.sub!) + subValue);
                    return new LessonConfig { Type = l.Type, SpBonus = sp };
                }).ToList();
                return CloneWeek(w, lessons: newLessons);
            }
            if (w.Type == "audition" && (w.HifExamBase != null || w.HifExamDistributed != null))
            {
                int b = w.HifExamBase ?? 0;
                var a = examAlloc.GetValueOrDefault(w.Week) ?? new StatusValues(0, 0, 0);
                return CloneWeek(w, statusGain: new StatusValues(b + Math.Max(0, a.Vo), b + Math.Max(0, a.Da), b + Math.Max(0, a.Vi)));
            }
            return w;
        }).ToList();

        var newPlan = new TrainingPlan
        {
            Id = hifPlan.Id, Name = hifPlan.Name, StatusLimit = hifPlan.StatusLimit,
            BaseStatus = hifPlan.BaseStatus, Schedule = newSchedule,
        };

        var turnChoices = new List<TurnChoice>();
        foreach (var w in newSchedule)
        {
            if (w.Type is "audition" or "fixed_event" or "exam") continue;
            if (w.AvailableActions.Count == 0) continue;
            if (!choices.TryGetValue(w.Week, out var ch)) continue;
            turnChoices.Add(new TurnChoice { Week = w.Week, ChosenAction = ParseAction(ch.action) });
        }
        return (newPlan, turnChoices);
    }

    private static WeekSchedule CloneWeek(WeekSchedule w, List<LessonConfig>? lessons = null, StatusValues? statusGain = null)
        => new()
        {
            Week = w.Week, Type = w.Type, AvailableActions = w.AvailableActions,
            Lessons = lessons ?? w.Lessons, EventName = w.EventName,
            StatusGain = statusGain ?? w.StatusGain, OutingEffect = w.OutingEffect,
            Classes = w.Classes, ClassEffect = w.ClassEffect, ConsultationEffect = w.ConsultationEffect,
            SpecialTrainingEffect = w.SpecialTrainingEffect, HifSubValue = w.HifSubValue,
            HifExamBase = w.HifExamBase, HifExamDistributed = w.HifExamDistributed,
        };

    private static int GetStat(StatusValues sv, string stat) => stat switch
    {
        "vo" => sv.Vo, "da" => sv.Da, "vi" => sv.Vi, _ => 0,
    };
    private static void SetStat(StatusValues sv, string stat, int v)
    {
        if (stat == "vo") sv.Vo = v;
        else if (stat == "da") sv.Da = v;
        else if (stat == "vi") sv.Vi = v;
    }

    private static ActionType ParseAction(string a) => a switch
    {
        "vo_lesson" => ActionType.VoLesson, "da_lesson" => ActionType.DaLesson, "vi_lesson" => ActionType.ViLesson,
        "vo_class" => ActionType.VoClass, "da_class" => ActionType.DaClass, "vi_class" => ActionType.ViClass,
        "outing" => ActionType.Outing, "consultation" => ActionType.Consultation,
        "activity_supply" => ActionType.ActivitySupply, "special_training" => ActionType.SpecialTraining,
        _ => ActionType.Rest,
    };
}
