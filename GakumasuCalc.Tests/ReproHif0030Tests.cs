using System.IO;
using System.Text.Json;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace GakumasuCalc.Tests;

/// <summary>
/// 回帰テスト: ユーザ報告(2026-06) HIF 診断シナリオの C# 版 (TS の cardScoringHif.crossSeed.test.ts と対)。
/// バグ: 自動編成が Da偏重・Vi0枚の局所最適(6354)に落ち、ほっぺた等の高Viカードを使う
///       balanced 最適(6418)を逃していた。修正: SelectMultiplePatternsHif の cross-seed 大域最適化。
/// 検証: レンタル枠対応の総当たりオラクルを実データ(各パターンが surface したカードの和集合)に適用し、
///       自動最良 ≧ 独立に総当たりで求めた最適。答えを事前に知らなくてもこのクラスのバグを捕捉できる。
/// </summary>
public class ReproHif0030Tests
{
    private readonly ITestOutputHelper _out;
    public ReproHif0030Tests(ITestOutputHelper o) { _out = o; }

    private record InvEntry(string card_id, bool owned, int uncap);

    [Fact]
    public void CrossSeed_ReachesBalancedOptimum()
    {
        var allCards = RepoData.LoadAllCards();
        var hifPlan = RepoData.LoadPlan("hif");
        var lilja = RepoData.LoadCharacters().First(c => c.Id == "char_lilja");
        var invPath = Path.Combine(RepoData.RepoRoot(), "TestFixtures", "hif_repro_inventory.json");
        var inventory = JsonSerializer.Deserialize<List<InvEntry>>(File.ReadAllText(invPath))!;

        var ownedIds = inventory.Where(e => e.owned).Select(e => e.card_id).ToHashSet();
        var candidateCards = allCards.Where(c => ownedIds.Contains(c.Id)).ToList();
        var uncapLevels = new Dictionary<string, int>();
        foreach (var e in inventory) uncapLevels[e.card_id] = e.uncap;
        var rentalPool = allCards;

        var (plan, turnChoices) = BuildPlanAndChoices(hifPlan);
        plan.StatusLimit += 200; // finalStatLimitLevel=6

        var effectiveChar = new Character
        {
            Id = "char_lilja", Name = lilja.Name, Color = lilja.Color, Initial = lilja.Initial,
            BaseStatusBonus = new StatusValues(lilja.BaseStatusBonus.Vo + 100, lilja.BaseStatusBonus.Da + 100, lilja.BaseStatusBonus.Vi + 100),
            ParaBonus = new StatBonusPercent { Vo = lilja.ParaBonus.Vo + 10, Da = lilja.ParaBonus.Da + 10, Vi = lilja.ParaBonus.Vi + 10 },
            Uncap3Bonus = lilja.Uncap3Bonus,
            Step4Bonus = lilja.Step4Bonus,
        };

        var additionalCounts = new AdditionalCounts
        {
            PDrinkAcquire = 15, PItemAcquire = 6, SkillAcquire = 20, SkillSsrAcquire = 8,
            SkillEnhance = 4, SkillDelete = 2, SkillCustom = 3, SkillChange = 3,
            ActiveEnhance = 3, ActiveDelete = 2, MentalAcquire = 8, MentalEnhance = 1,
            MentalDelete = 2, ActiveAcquire = 8, ConserveAcquire = 8, FullpowerAcquire = 8,
            AggressiveAcquire = 4, ConsultationDrink = 6,
        };

        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            if (tc.ChosenAction == ActionType.VoLesson) lessonAllocation["vo"]++;
            else if (tc.ChosenAction == ActionType.DaLesson) lessonAllocation["da"]++;
            else if (tc.ChosenAction == ActionType.ViLesson) lessonAllocation["vi"]++;
        }
        var mainStats = new List<string> { "da", "vo" };
        var spCounts = new Dictionary<string, int> { ["da"] = 3 };

        var svc = new CardScoringService();
        var patterns = svc.SelectMultiplePatternsHif(
            plan, candidateCards, mainStats, lessonAllocation, spCounts, "anomaly",
            additionalCounts, uncapLevels, rentalPool, null, effectiveChar, null, turnChoices, null);

        int cap = plan.StatusLimit;
        var calc = new StatusCalculationService();

        int Score(List<SupportCard> cards, string? rentalId)
        {
            var uc = new Dictionary<string, int>(uncapLevels);
            if (rentalId != null) uc[rentalId] = 4;
            var fs = calc.Calculate(plan, cards, turnChoices, uc, additionalCounts, effectiveChar, null).FinalStatus;
            return Math.Min(fs.Vo, cap) + Math.Min(fs.Da, cap) + Math.Min(fs.Vi, cap);
        }

        int autoBest = int.MinValue;
        foreach (var p in patterns)
        {
            var cards = p.SelectedCards.Select(cs => cs.Card).ToList();
            var rentalId = p.SelectedCards.FirstOrDefault(cs => cs.IsRental)?.Card.Id;
            int total = Score(cards, rentalId);
            _out.WriteLine($"{p.Label}: total={total} [{string.Join(",", cards.Select(c => c.Id))}]");
            if (total > autoBest) autoBest = total;
        }
        _out.WriteLine($"autoBest={autoBest}");

        // --- 独立した総当たりオラクル (実データ・レンタル枠考慮) ---
        // プール = 各パターンが選んだカードの和集合 (最適化器自身が surface した属性多様な部品;
        // 個別寄与が低くても最適に入る札 0008 等も含む) + レンタル候補の Da上位 (0059 等)。
        bool PlanOk(SupportCard c) => string.IsNullOrEmpty(c.Plan) || c.Plan == "anomaly" || c.Plan == "free";
        var patternCardIds = patterns.SelectMany(p => p.SelectedCards.Select(cs => cs.Card.Id)).ToHashSet();
        int SoloTotal(SupportCard c, bool asRental)
        {
            var uc = new Dictionary<string, int>(uncapLevels);
            if (asRental) uc[c.Id] = 4;
            var fs = calc.Calculate(plan, new List<SupportCard> { c }, turnChoices, uc, additionalCounts, effectiveChar, null).FinalStatus;
            return Math.Min(fs.Vo, cap) + Math.Min(fs.Da, cap) + Math.Min(fs.Vi, cap);
        }
        var ownedBF = candidateCards.Where(c => patternCardIds.Contains(c.Id) && PlanOk(c)).ToList();
        var topDaRental = rentalPool.Where(c => PlanOk(c) && c.Type == "da")
            .OrderByDescending(c => SoloTotal(c, true)).Take(5);
        var rentalBF = rentalPool.Where(c => patternCardIds.Contains(c.Id) && PlanOk(c))
            .Concat(topDaRental)
            .GroupBy(c => c.Id).Select(g => g.First()).ToList();

        bool CoversDaSp(SupportCard c) =>
            c.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate" && (e.Stat == "da" || e.Stat == "all"));
        bool ValidDeck(List<SupportCard> deck) =>
            deck.Select(c => c.Id).Distinct().Count() == deck.Count && deck.Count(CoversDaSp) >= 3;

        var bf = BruteForce.FindOptimalDeckWithRental(ownedBF, rentalBF, 6,
            (deck, rentalId) => Score(deck, rentalId), ValidDeck);
        _out.WriteLine($"bf.BestTotal={bf.BestTotal} [{string.Join(",", bf.BestDeck.Select(c => c.Id))}] rental={bf.BestRentalId}");

        // teeth: オラクルは旧出荷の局所最適(6354)を独立に上回るデッキを実際に見つけている
        Assert.True(bf.BestTotal > 6354, $"オラクルが独立に見つけた最適={bf.BestTotal} は旧出荷値6354を上回るべき(teeth)");
        // 本検証: 自動最良は独立に求めた総当たり最適を下回ってはならない
        Assert.True(autoBest >= bf.BestTotal, $"自動最良={autoBest} は総当たり最適={bf.BestTotal} 以上であるべき");
    }

    // hifStore.buildPlanAndChoices の複製 (診断のスケジュール選択を再構築)
    private static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlanAndChoices(TrainingPlan hifPlan)
    {
        var choices = new Dictionary<int, (string action, string? sub)>
        {
            [1] = ("activity_supply", null), [2] = ("da_lesson", "vi"), [3] = ("vo_class", null),
            [4] = ("da_lesson", "vi"), [5] = ("outing", null), [6] = ("vo_class", null),
            [8] = ("activity_supply", null), [9] = ("da_lesson", "vi"), [10] = ("vo_class", null),
            [11] = ("da_lesson", "vi"), [12] = ("consultation", null), [14] = ("activity_supply", null),
            [15] = ("da_lesson", "vi"), [16] = ("activity_supply", null), [17] = ("vo_class", null),
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
