namespace GakumasuCalc.Models;

/// <summary>
/// 日程方式 (初レジェンド / NIA) のスケジュールプリセット。
/// HIF と違い公開レッスンのサブ属性・試験配分は無いため Action のみを保持する。
/// </summary>
public class SchedulePreset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>各週のユーザ選択 (Week → アクション)。固定イベント週は含めない。</summary>
    public List<ScheduleChoiceEntry> Choices { get; set; } = new();

    public override string ToString() => Name;
}

public class ScheduleChoiceEntry
{
    public int Week { get; set; }
    /// <summary>選択したアクション (vo_lesson, da_class, outing 等)。</summary>
    public string Action { get; set; } = string.Empty;
}

public class SchedulePresetFile
{
    public List<SchedulePreset> Presets { get; set; } = new();
}
