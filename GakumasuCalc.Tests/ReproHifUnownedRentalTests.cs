using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: ユーザ報告(2026-07)「未所持カードがレンタル選出されず合計が理論値を下回る」の C# 版
/// (TS の cardScoringHif.unownedRental.test.ts と対)。
/// 倉本千奈 / sense / 所持カードのみ ON / DaSP3 / 必須 パシャっとキメポ(SP_SR_0017) /
/// いつまでも続けばいいのに(SP_SSR_0069) は未所持。
///
/// バグ (2つの複合):
/// ① インベントリは未所持カードを owned:false, uncap:4 で保存するため、uncap だけを見る
///    IsUserOwned4Star が未所持カード全てを「4凸所持 (借用恩恵ゼロ)」と誤判定し、
///    Pattern A/B/C のレンタル候補プールから除外していた。
/// ② レンタル枠が SP要員 (0071 0凸所持を4凸借用) で確定すると、OptimizeRentalCard の
///    単手入替は SP枚数不足で全滅し、OptimizeRentalBorrowUpgrade も借用候補が
///    「低凸所持カード」限定のため、「未所持カードを借用し、旧レンタルを所持0凸の
///    SP要員に戻し、弱い1枚を落とす」複合手 (診断②の正解編成) に到達できなかった。
///
/// 修正: IsUserOwned4Star を所持集合との積で判定 (①)、OptimizeRentalBorrowUpgrade の
///       借用候補に rentalPool 内の未所持カードを追加 (②)。
///
/// 検証 (答え非依存の不変条件): 自動選出の最良合計は、ユーザが必須指定で作れる
/// 手動編成 (0069 4凸レンタル + 0071 0凸 + 0064 + 0057 + 0008 + 0017) を下回らない。
/// teeth: 修正前は自動 6589 &lt; 手動 6631 で赤。修正後は 6631 で緑。
/// </summary>
public class ReproHifUnownedRentalTests
{
    private readonly ITestOutputHelper _out;
    public ReproHifUnownedRentalTests(ITestOutputHelper o) { _out = o; }

    private record InvEntry(string card_id, bool owned, int uncap);

    [Fact]
    public void AutoSelectionIsNotWorseThanManualUnownedRentalDeck()
    {
        var allCards = RepoData.LoadAllCards();
        var hifPlan = RepoData.LoadPlan("hif");
        var invPath = Path.Combine(RepoData.RepoRoot(), "TestFixtures", "hif_unowned_rental_inventory.json");
        var inventory = JsonSerializer.Deserialize<List<InvEntry>>(File.ReadAllText(invPath))!;

        var ownedIds = inventory.Where(e => e.owned).Select(e => e.card_id).ToHashSet();
        // buildUncapLevels(ownedOnly=true) 相当: 未所持カードも uncap=4 のままエントリされる
        var uncapLevels = new Dictionary<string, int>();
        foreach (var e in inventory) uncapLevels[e.card_id] = e.uncap;

        var candidateCards = allCards.Where(c => ownedIds.Contains(c.Id)).ToList();
        var rentalPool = allCards.ToList();

        var (plan, turnChoices) = BuildPlanAndChoices(hifPlan);
        plan.StatusLimit += 200; // finalStatLimitLevel=6 → 3200

        // 倉本千奈 (3凸OFF/STEP4 ON: 基礎95/125/135, パラボ13/24/21.5) + HIF Lv5 (flat+100/para+10)
        var effectiveChar = new Character
        {
            Id = "char_china", Name = "倉本千奈", Color = "#F68B1F", Initial = "千",
            BaseStatusBonus = new StatusValues(95 + 100, 125 + 100, 135 + 100),
            ParaBonus = new StatBonusPercent { Vo = 13 + 10, Da = 24 + 10, Vi = 21.5 + 10 },
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
            Vo = new MemoryAttributeBonus(20, MemoryBonusType.Flat),
            Da = new MemoryAttributeBonus(2.8, MemoryBonusType.ParaBonus),
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
        var mainStats = new List<string> { "da", "vo" };
        var spCounts = new Dictionary<string, int> { ["da"] = 3 };
        var requiredCardIds = new List<string> { "SP_SR_0017" };
        var overflowPenalty = new CardScoringService.OverflowPenaltyConfig { Threshold = 100 };

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, candidateCards, mainStats, lessonAllocation, spCounts, "sense",
            additionalCounts, uncapLevels, rentalPool, requiredCardIds, effectiveChar,
            memoryBonuses, turnChoices, overflowPenalty);
        Assert.NotEmpty(patterns);

        var calcService = new StatusCalculationService();
        int cap = plan.StatusLimit;
        int CappedTotal(List<string> cardIds, HashSet<string> rentalIds)
        {
            var uc = new Dictionary<string, int>(uncapLevels);
            foreach (var id in rentalIds) uc[id] = 4;
            var cards = cardIds.Select(id => allCards.First(c => c.Id == id)).ToList();
            var fs = calcService.Calculate(plan, cards, turnChoices, uc, additionalCounts, effectiveChar, memoryBonuses).FinalStatus;
            return Math.Min(fs.Vo, cap) + Math.Min(fs.Da, cap) + Math.Min(fs.Vi, cap);
        }

        // 自動選出の最良合計 (キャラ込みキャップ後合計で比較)
        int bestAuto = int.MinValue;
        foreach (var p in patterns)
        {
            var rentalIds = p.SelectedCards.Where(cs => cs.IsRental).Select(cs => cs.Card.Id).ToHashSet();
            int total = CappedTotal(p.SelectedCards.Select(cs => cs.Card.Id).ToList(), rentalIds);
            _out.WriteLine($"{p.Label}: total={total} [{string.Join(",", p.SelectedCards.Select(cs => cs.Card.Id + (cs.IsRental ? "(R)" : "")))}]");
            if (total > bestAuto) bestAuto = total;
        }

        // 手動編成: 診断②でユーザが必須指定により到達した編成 (自動が下回ったらバグ)
        var manualIds = new List<string> { "SP_SSR_0069", "SP_SSR_0071", "SP_SSR_0064", "SP_SR_0057", "SP_SR_0008", "SP_SR_0017" };
        int manualTotal = CappedTotal(manualIds, new HashSet<string> { "SP_SSR_0069" });

        Assert.True(bestAuto >= manualTotal,
            $"自動選出({bestAuto})が手動編成({manualTotal})を下回った: 未所持カードのレンタル候補除外 or 複合手の取り逃し");
    }

    // hifStore.buildPlanAndChoices の複製 (診断①の千奈 sense スケジュール選択を再構築)
    private static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlanAndChoices(TrainingPlan hifPlan)
    {
        var choices = new Dictionary<int, (string action, string? sub)>
        {
            [1] = ("activity_supply", null), [2] = ("da_lesson", "vi"), [3] = ("vo_class", null),
            [4] = ("da_lesson", "vi"), [5] = ("outing", null), [6] = ("vo_class", null),
            [8] = ("outing", null), [9] = ("da_lesson", "vi"), [10] = ("vo_class", null),
            [11] = ("da_lesson", "vi"), [12] = ("consultation", null), [14] = ("outing", null),
            [15] = ("da_lesson", "vi"), [16] = ("consultation", null), [17] = ("vo_class", null),
            [18] = ("da_lesson", "vi"), [19] = ("consultation", null), [21] = ("vo_class", null),
            [22] = ("da_lesson", "vi"), [23] = ("activity_supply", null), [24] = ("vo_class", null),
            [25] = ("da_lesson", "vi"), [26] = ("consultation", null),
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
