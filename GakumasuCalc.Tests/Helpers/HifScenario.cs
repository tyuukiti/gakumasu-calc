using GakumasuCalc.Models;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>
/// HIF シナリオ再構築ヘルパ (Web版 hifStore の buildPlanAndChoices / executeCalculate の複製。
/// TS tests/helpers/hifScenario.ts と対)。共有ペイロード / フィクスチャ (week は文字列キー) から、
/// 計算エンジンに渡す TrainingPlan・TurnChoice・effective Character を Web版と同じ規則で組み立てる。
/// TS 側のテストで「ストア経由の復元と一致する」ことを検証済みの規則を、そのまま C# に写している。
/// </summary>
public static class HifScenario
{
    public record Choice(string action, string? sub_stat);

    /// <summary>hifStore.buildPlanAndChoices の複製。examAllocations は週→配分 (floor 済み)。</summary>
    public static (TrainingPlan plan, List<TurnChoice> turnChoices) BuildPlanAndChoices(
        TrainingPlan hifPlan,
        IReadOnlyDictionary<string, Choice> choices,
        IReadOnlyDictionary<string, StatusValues> examAllocations)
    {
        var newSchedule = hifPlan.Schedule.Select(w =>
        {
            if (w.Type == "public_lesson")
            {
                if (!choices.TryGetValue(w.Week.ToString(), out var ch) || ch.sub_stat == null)
                    return CloneWeek(w, lessons: w.Lessons.ToList());
                var mainStat = ch.action.Split('_')[0];
                var mainLesson = w.Lessons.FirstOrDefault(l => l.Type == mainStat);
                int mainValue = mainLesson != null ? GetStat(mainLesson.SpBonus, mainStat) : 0;
                int subValue = w.HifSubValue ?? 0;
                var newLessons = w.Lessons.Select(l =>
                {
                    if (l.Type != mainStat) return l;
                    var sp = new StatusValues(0, 0, 0);
                    SetStat(sp, mainStat, mainValue);
                    SetStat(sp, ch.sub_stat, GetStat(sp, ch.sub_stat) + subValue);
                    return new LessonConfig { Type = l.Type, SpBonus = sp };
                }).ToList();
                return CloneWeek(w, lessons: newLessons);
            }
            // 試験日: 基礎値(全属性同値) + ユーザ配分値 を status_gain に反映
            if (w.Type == "audition" && (w.HifExamBase != null || w.HifExamDistributed != null))
            {
                int b = w.HifExamBase ?? 0;
                var a = examAllocations.TryGetValue(w.Week.ToString(), out var alloc) ? alloc : new StatusValues(0, 0, 0);
                return CloneWeek(w, statusGain: new StatusValues(
                    b + Math.Max(0, a.Vo), b + Math.Max(0, a.Da), b + Math.Max(0, a.Vi)));
            }
            return w;
        }).ToList();

        var newPlan = new TrainingPlan
        {
            Id = hifPlan.Id, Name = hifPlan.Name, Description = hifPlan.Description,
            TotalWeeks = hifPlan.TotalWeeks, StatusLimit = hifPlan.StatusLimit,
            BaseStatus = hifPlan.BaseStatus, Schedule = newSchedule, ActivitySupply = hifPlan.ActivitySupply,
        };

        var turnChoices = new List<TurnChoice>();
        foreach (var w in newSchedule)
        {
            if (w.Type is "audition" or "fixed_event" or "exam") continue;
            if (w.AvailableActions.Count == 0) continue;
            if (!choices.TryGetValue(w.Week.ToString(), out var ch)) continue;
            turnChoices.Add(new TurnChoice { Week = w.Week, ChosenAction = ParseAction(ch.action) });
        }
        return (newPlan, turnChoices);
    }

    /// <summary>ボーナスLv (snake→Pascal で HifBonusLevels のプロパティに写す。欠落は既定=MAX)。</summary>
    public static HifBonusLevels LevelsFrom(IReadOnlyDictionary<string, int> raw)
    {
        var levels = new HifBonusLevels();
        foreach (var (key, value) in raw)
        {
            var prop = typeof(HifBonusLevels).GetProperty(char.ToUpperInvariant(key[0]) + key[1..]);
            if (prop != null && prop.PropertyType == typeof(int)) prop.SetValue(levels, value);
        }
        return levels;
    }

    /// <summary>本戦上限増加を StatusLimit に加算 (hifStore.executeCalculate と同じ)。</summary>
    public static void ApplyFinalCap(TrainingPlan plan, HifBonusLevels levels)
        => plan.StatusLimit += HifBonusTables.GetFinalCapBonus(levels.FinalStatLimitLevel);

    /// <summary>キャラ補正トグル (3凸/STEP4) を適用した一時 Character (Web版 applyCharacterToggles と同じ)。</summary>
    public static Character? ApplyCharacterToggles(Character? c, bool uncap3, bool step4)
    {
        if (c == null) return null;
        var baseBonus = c.BaseStatusBonus;
        var para = c.ParaBonus;
        if (!uncap3 && c.Uncap3Bonus != null) para = para.Subtract(c.Uncap3Bonus);
        if (step4 && c.Step4Bonus != null)
        {
            baseBonus = baseBonus.Add(c.Step4Bonus.BaseStatusBonus);
            para = para.Add(c.Step4Bonus.ParaBonus);
        }
        return new Character
        {
            Id = c.Id, Name = c.Name, Color = c.Color, Initial = c.Initial,
            BaseStatusBonus = baseBonus, ParaBonus = para,
            Uncap3Bonus = c.Uncap3Bonus, Step4Bonus = c.Step4Bonus,
            NiaCriteria = c.NiaCriteria, NiaTrend = c.NiaTrend,
        };
    }

    /// <summary>キャラ補正トグル + HIF 上昇パネルを合算した effective Character (hifStore.executeCalculate と同じ)。</summary>
    public static Character? BuildEffectiveCharacter(Character? character, bool uncap3, bool step4, HifBonusLevels levels)
    {
        var b = ApplyCharacterToggles(character, uncap3, step4);
        int voFlat = HifBonusTables.GetStatUpFlat(levels.VoUpLevel);
        int daFlat = HifBonusTables.GetStatUpFlat(levels.DaUpLevel);
        int viFlat = HifBonusTables.GetStatUpFlat(levels.ViUpLevel);
        int voPara = HifBonusTables.GetStatUpPara(levels.VoUpLevel);
        int daPara = HifBonusTables.GetStatUpPara(levels.DaUpLevel);
        int viPara = HifBonusTables.GetStatUpPara(levels.ViUpLevel);
        if (b == null && voFlat == 0 && daFlat == 0 && viFlat == 0 && voPara == 0 && daPara == 0 && viPara == 0)
            return null;
        return new Character
        {
            Id = b?.Id ?? "__hif_bonus__", Name = b?.Name ?? "HIF Bonus",
            Color = b?.Color ?? "#000000", Initial = b?.Initial ?? "",
            BaseStatusBonus = new StatusValues(
                (b?.BaseStatusBonus.Vo ?? 0) + voFlat,
                (b?.BaseStatusBonus.Da ?? 0) + daFlat,
                (b?.BaseStatusBonus.Vi ?? 0) + viFlat),
            ParaBonus = new StatBonusPercent
            {
                Vo = (b?.ParaBonus.Vo ?? 0) + voPara,
                Da = (b?.ParaBonus.Da ?? 0) + daPara,
                Vi = (b?.ParaBonus.Vi ?? 0) + viPara,
            },
            Uncap3Bonus = b?.Uncap3Bonus, Step4Bonus = b?.Step4Bonus,
        };
    }

    /// <summary>snake_case のトリガー名 → AdditionalCounts のプロパティへ回数を写す。</summary>
    public static AdditionalCounts AdditionalCountsFrom(IReadOnlyDictionary<string, int> raw)
    {
        var counts = new AdditionalCounts();
        foreach (var (key, value) in raw)
        {
            var propName = string.Concat(key.Split('_').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
            var prop = typeof(AdditionalCounts).GetProperty(propName)
                ?? throw new KeyNotFoundException($"AdditionalCounts.{propName} が見つからない (trigger={key})");
            prop.SetValue(counts, value);
        }
        return counts;
    }

    public static ActionType ParseAction(string a) => a switch
    {
        "vo_lesson" => ActionType.VoLesson, "da_lesson" => ActionType.DaLesson, "vi_lesson" => ActionType.ViLesson,
        "vo_class" => ActionType.VoClass, "da_class" => ActionType.DaClass, "vi_class" => ActionType.ViClass,
        "outing" => ActionType.Outing, "consultation" => ActionType.Consultation,
        "activity_supply" => ActionType.ActivitySupply, "special_training" => ActionType.SpecialTraining,
        _ => ActionType.Rest,
    };

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
}
