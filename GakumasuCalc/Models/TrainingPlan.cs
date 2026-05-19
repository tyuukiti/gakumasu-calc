using YamlDotNet.Serialization;

namespace GakumasuCalc.Models;

public class TrainingPlanFile
{
    [YamlMember(Alias = "plan")]
    public TrainingPlan Plan { get; set; } = new();
}

public class TrainingPlan
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "total_weeks")]
    public int TotalWeeks { get; set; }

    [YamlMember(Alias = "status_limit")]
    public int StatusLimit { get; set; } = 2800;

    [YamlMember(Alias = "base_status")]
    public StatusValues BaseStatus { get; set; } = StatusValues.Zero;

    [YamlMember(Alias = "schedule")]
    public List<WeekSchedule> Schedule { get; set; } = new();

    [YamlMember(Alias = "activity_supply")]
    public ActivitySupplyConfig? ActivitySupply { get; set; }

    public override string ToString() => Name;
}

public class WeekSchedule
{
    [YamlMember(Alias = "week")]
    public int Week { get; set; }

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "free";

    [YamlMember(Alias = "available_actions")]
    public List<string> AvailableActions { get; set; } = new();

    [YamlMember(Alias = "lessons")]
    public List<LessonConfig> Lessons { get; set; } = new();

    [YamlMember(Alias = "event_name")]
    public string? EventName { get; set; }

    [YamlMember(Alias = "status_gain")]
    public StatusValues? StatusGain { get; set; }

    [YamlMember(Alias = "outing_effect")]
    public StatusValues? OutingEffect { get; set; }

    [YamlMember(Alias = "classes")]
    public List<LessonConfig> Classes { get; set; } = new();

    [YamlMember(Alias = "class_effect")]
    public StatusValues? ClassEffect { get; set; }

    [YamlMember(Alias = "consultation_effect")]
    public StatusValues? ConsultationEffect { get; set; }

    [YamlMember(Alias = "special_training_effect")]
    public StatusValues? SpecialTrainingEffect { get; set; }

    /// <summary>
    /// HIFモードの公開レッスン日でサブ属性に加算されるサブ値。
    /// 実行時にユーザ選択でメイン属性以外の1属性へ加算される。
    /// </summary>
    [YamlMember(Alias = "hif_sub_value")]
    public int? HifSubValue { get; set; }

    /// <summary>
    /// HIFモードの試験日で全属性に同値加算される基礎値。
    /// </summary>
    [YamlMember(Alias = "hif_exam_base")]
    public int? HifExamBase { get; set; }

    /// <summary>
    /// HIFモードの試験日でユーザが Vo/Da/Vi に振り分ける配分値の合計。
    /// </summary>
    [YamlMember(Alias = "hif_exam_distributed")]
    public int? HifExamDistributed { get; set; }

    public bool IsFree => Type == "free";
    public bool IsFixedEvent => Type == "fixed_event" || Type == "exam" || Type == "audition";

    /// <summary>
    /// アクション文字列に対応するレッスン設定を取得
    /// </summary>
    public LessonConfig? GetLesson(string lessonType)
    {
        return Lessons.FirstOrDefault(l => l.Type == lessonType);
    }

    /// <summary>
    /// アクション文字列に対応する授業設定を取得
    /// </summary>
    public LessonConfig? GetClass(string classType)
    {
        return Classes.FirstOrDefault(l => l.Type == classType);
    }
}

public class LessonConfig
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "sp_bonus")]
    public StatusValues SpBonus { get; set; } = StatusValues.Zero;
}

public class ActivitySupplyConfig
{
    [YamlMember(Alias = "available_weeks")]
    public List<int> AvailableWeeks { get; set; } = new();

    [YamlMember(Alias = "options")]
    public List<SupplyOption> Options { get; set; } = new();
}

public class SupplyOption
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "effect")]
    public StatusValues Effect { get; set; } = StatusValues.Zero;

    public override string ToString() => Name;
}
