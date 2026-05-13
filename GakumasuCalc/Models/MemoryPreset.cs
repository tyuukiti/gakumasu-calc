namespace GakumasuCalc.Models;

/// <summary>
/// 持ち込みメモリー4枠分のプリセット。ユーザーが名前を付けて保存し、後で呼び出せる。
/// </summary>
public class MemoryPreset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>常に4要素を想定（空スロットも含む）。</summary>
    public List<MemoryBonus> Bonuses { get; set; } = new();

    public override string ToString() => Name;
}

public class MemoryPresetFile
{
    public List<MemoryPreset> Presets { get; set; } = new();
}
