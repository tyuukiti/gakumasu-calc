namespace GakumasuCalc.Models;

/// <summary>
/// HIFスケジュール調整のプリセット (個別調整した結果を名前付きで保存・読み込み)。
/// </summary>
public class HifSchedulePreset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>各日のユーザ選択 (Day → アクション + サブ属性等)</summary>
    public List<HifScheduleChoiceEntry> Choices { get; set; } = new();

    /// <summary>試験日の配分 (Day → Vo/Da/Vi 振り分け)</summary>
    public List<HifExamAllocationEntry> ExamAllocations { get; set; } = new();

    public override string ToString() => Name;
}

public class HifScheduleChoiceEntry
{
    public int Week { get; set; }
    /// <summary>選択したアクション (vo_lesson, da_class, outing 等)。</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>公開レッスン日のサブ属性 (vo/da/vi)。それ以外は null。</summary>
    public string? SubStat { get; set; }
}

public class HifExamAllocationEntry
{
    public int Week { get; set; }
    public int Vo { get; set; }
    public int Da { get; set; }
    public int Vi { get; set; }
}

public class HifSchedulePresetFile
{
    public List<HifSchedulePreset> Presets { get; set; } = new();
}
