namespace GakumasuCalc.Models;

/// <summary>
/// イベント回数入力のプリセット。ユーザーが名前を付けて保存し、後で呼び出せる。
/// </summary>
public class EventCountPreset
{
    public string Name { get; set; } = string.Empty;

    public AdditionalCounts Counts { get; set; } = new();

    public override string ToString() => Name;
}

public class EventCountPresetFile
{
    public List<EventCountPreset> Presets { get; set; } = new();
}
