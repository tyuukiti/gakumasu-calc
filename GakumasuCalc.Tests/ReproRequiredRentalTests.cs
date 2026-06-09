using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: ユーザ報告(2026-06)「必須カードを増やすとレンタル枠が消える」の C# 版
/// (TS の cardScoringHif.requiredRental.test.ts と対)。
/// 紫雲清夏 / sense / 所持カードのみ ON / コンテストモード ON / DaSP2 / 必須4枚 (いずれも Da-SP 非カバー)。
///
/// バグ: 必須4枚 (step0) + DaSP補充2枚 (step1) で所持枠が6枚埋まり、「レンタル1枠」ブロック
///       (selected.Count &lt; 6) が発火せず IsRental が1枚も立たない (レンタル枠が消える)。
///       上限張り付き(Da capped)で借用が score-tie だと、付け替えも 4凸所持カードに乗ったままになる。
/// 修正: EnsureRentalSlot で必ずレンタル枠を確保し、OptimizeRentalAssignment の同点タイブレークを
///       「最低凸カード優先」にした。
///
/// 検証 (答え非依存の不変条件):
///   ① 所持カードのみ ON では各パターンに必ずレンタル枠が1枚存在する (= 消えない)。
///   ② 全カード所持のデッキでは、レンタルはデッキ内最低凸の所持カードに乗る。
/// </summary>
public class ReproRequiredRentalTests
{
    private readonly ITestOutputHelper _out;
    public ReproRequiredRentalTests(ITestOutputHelper o) { _out = o; }

    private record InvEntry(string card_id, bool owned, int uncap);

    [Fact]
    public void RequiredCardsKeepRentalSlotOnLowestUncap()
    {
        var allCards = RepoData.LoadAllCards();
        var hifPlan = RepoData.LoadPlan("hif");
        var sumika = RepoData.LoadCharacters().First(c => c.Id == "char_sumika");
        var invPath = Path.Combine(RepoData.RepoRoot(), "TestFixtures", "hif_repro_inventory.json");
        var inventory = JsonSerializer.Deserialize<List<InvEntry>>(File.ReadAllText(invPath))!;

        var ownedIds = inventory.Where(e => e.owned).Select(e => e.card_id).ToHashSet();
        var uncapLevels = new Dictionary<string, int>();
        foreach (var e in inventory) uncapLevels[e.card_id] = e.uncap;

        // contestMode ON: skill / exam_item を除外
        bool ContestFilter(SupportCard c) => c.Tag != "skill" && c.Tag != "exam_item";
        var candidateCards = allCards.Where(c => ownedIds.Contains(c.Id) && ContestFilter(c)).ToList();
        var rentalPool = allCards.Where(ContestFilter).ToList();

        // 必須4枚: いずれも Da-SP を持たない → step1 が DaSP を2枚補充して所持枠が6枚に達する (overfill)。
        // 4枚とも exam_item/skill タグなので contestMode で候補から外れる。hifStore と同様に
        // 所持済み必須カードを candidateCards / rentalPool へ再投入する (これがないと必須が無視され overfill が起きない)。
        var requiredCardIds = new List<string> { "SP_SSR_0014", "SP_SSR_0005", "SP_SSR_0069", "SP_SSR_0002" };
        var candSet = candidateCards.Select(c => c.Id).ToHashSet();
        var rentalSet = rentalPool.Select(c => c.Id).ToHashSet();
        foreach (var card in allCards)
        {
            if (!requiredCardIds.Contains(card.Id)) continue;
            if (ownedIds.Contains(card.Id) && !candSet.Contains(card.Id)) candidateCards.Add(card);
            if (!rentalSet.Contains(card.Id)) rentalPool.Add(card);
        }

        var (plan, turnChoices) = BuildPlanAndChoices(hifPlan);
        plan.StatusLimit += 200; // finalStatLimitLevel=6

        // sense キャラ + HIFボーナス Lv5 (flat+100/para+10%)。invariant は補正値に依存しないが診断を再現。
        var effectiveChar = new Character
        {
            Id = "char_sumika", Name = sumika.Name, Color = sumika.Color, Initial = sumika.Initial,
            BaseStatusBonus = new StatusValues(sumika.BaseStatusBonus.Vo + 100, sumika.BaseStatusBonus.Da + 100, sumika.BaseStatusBonus.Vi + 100),
            ParaBonus = new StatBonusPercent { Vo = sumika.ParaBonus.Vo + 10, Da = sumika.ParaBonus.Da + 10, Vi = sumika.ParaBonus.Vi + 10 },
            Uncap3Bonus = sumika.Uncap3Bonus,
            Step4Bonus = sumika.Step4Bonus,
        };

        var additionalCounts = new AdditionalCounts
        {
            PDrinkAcquire = 15, PItemAcquire = 6, SkillAcquire = 20, SkillSsrAcquire = 8,
            SkillEnhance = 4, SkillDelete = 2, SkillCustom = 3, SkillChange = 3,
            ActiveEnhance = 3, ActiveDelete = 2, MentalAcquire = 8, MentalEnhance = 1,
            MentalDelete = 2, ActiveAcquire = 8, GoodConditionAcquire = 8, ConcentrateAcquire = 8,
            ConsultationDrink = 6,
        };

        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            if (tc.ChosenAction == ActionType.VoLesson) lessonAllocation["vo"]++;
            else if (tc.ChosenAction == ActionType.DaLesson) lessonAllocation["da"]++;
            else if (tc.ChosenAction == ActionType.ViLesson) lessonAllocation["vi"]++;
        }
        var mainStats = new List<string> { "da", "vo" };
        var spCounts = new Dictionary<string, int> { ["da"] = 2 };

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, candidateCards, mainStats, lessonAllocation, spCounts, "sense",
            additionalCounts, uncapLevels, rentalPool, requiredCardIds, effectiveChar, null, turnChoices, null);

        Assert.NotEmpty(patterns);
        foreach (var p in patterns)
        {
            var ids = p.SelectedCards.Select(cs => cs.Card.Id).ToHashSet();
            _out.WriteLine($"{p.Label}: rental={p.SelectedCards.Count(cs => cs.IsRental)} [{string.Join(",", ids)}]");

            // 前提: 必須4枚が実際に編成へ入っている (= overfill が起きる条件が成立している)
            foreach (var req in requiredCardIds)
                Assert.True(ids.Contains(req), $"{p.Label}: 必須カード {req} が編成に含まれること");

            var rentals = p.SelectedCards.Where(cs => cs.IsRental).ToList();
            // ① レンタルは消えない: 各パターンにちょうど1枚
            Assert.True(rentals.Count == 1, $"{p.Label}: レンタル枠は1枚であるべき (実際 {rentals.Count})");

            // ② 全カード所持なら、レンタルはデッキ内最低凸の所持カードに乗る
            bool allOwned = p.SelectedCards.All(cs => ownedIds.Contains(cs.Card.Id));
            if (allOwned)
            {
                int minUncap = p.SelectedCards.Min(cs => uncapLevels.GetValueOrDefault(cs.Card.Id, 4));
                int rentalUncap = uncapLevels.GetValueOrDefault(rentals[0].Card.Id, 4);
                Assert.True(rentalUncap == minUncap,
                    $"{p.Label}: レンタルは最低凸({minUncap})カードに乗るべき (実際 {rentalUncap})");
            }
        }
    }

    // hifStore.buildPlanAndChoices の複製 (診断の sense スケジュール選択を再構築)
    private static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlanAndChoices(TrainingPlan hifPlan)
    {
        var choices = new Dictionary<int, (string action, string? sub)>
        {
            [1] = ("activity_supply", null), [2] = ("da_lesson", "vo"), [3] = ("vi_class", null),
            [4] = ("da_lesson", "vo"), [5] = ("outing", null), [6] = ("vi_class", null),
            [8] = ("activity_supply", null), [9] = ("da_lesson", "vo"), [10] = ("vi_class", null),
            [11] = ("da_lesson", "vo"), [12] = ("consultation", null), [14] = ("activity_supply", null),
            [15] = ("da_lesson", "vo"), [16] = ("activity_supply", null), [17] = ("vo_class", null),
            [18] = ("da_lesson", "vo"), [19] = ("consultation", null), [21] = ("vo_class", null),
            [22] = ("da_lesson", "vo"), [23] = ("activity_supply", null), [24] = ("vo_class", null),
            [25] = ("da_lesson", "vo"), [26] = ("consultation", null),
        };
        var examAlloc = new Dictionary<int, StatusValues>
        {
            [7] = new(28, 26, 26), [13] = new(68, 66, 66), [20] = new(74, 73, 73),
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
