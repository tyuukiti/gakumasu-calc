namespace GakumasuCalc.Models;

/// <summary>
/// HIF カスタムPアイテム (ポーチ) 定義 (Data/Hif/pouch.yaml)。
/// 選抜①突破で入手 (段階0=未カスタマイズ) → ②突破で1段階目 (ぬいぐるみ) → ③突破で2段階目 (デコレーション)。
/// YamlDataService は UnderscoredNamingConvention なので snake_case キーは属性なしで対応付く。
/// </summary>
public class HifPouchFile
{
    public HifPouchDefinition? HifPouch { get; set; }
}

public class HifPouchDefinition
{
    /// <summary>段階ごとの有効週 (0=未カスタマイズ, 1=ぬいぐるみ, 2=デコレーション)</summary>
    public List<HifPouchStageWindow> StageWindows { get; set; } = new();
    public List<HifPouchColor> Colors { get; set; } = new();
}

public class HifPouchStageWindow
{
    public int Stage { get; set; }
    public int FromWeek { get; set; }
    public int ToWeek { get; set; }
}

/// <summary>ポーチ1段階分の効果</summary>
public class HifPouchStageEffect
{
    /// <summary>発動契機: lesson_end / consultation / activity_supply / outing_end</summary>
    public string Trigger { get; set; } = string.Empty;
    /// <summary>プロデュース中の発動回数上限 (段階ごと)</summary>
    public int MaxCount { get; set; }
    /// <summary>1回の発動で追加する回数 (キーは AdditionalCounts と同じ trigger 名)</summary>
    public Dictionary<string, int> Effects { get; set; } = new();
    /// <summary>表示用の効果文 (Pポイント・割引などの計算対象外効果も含む)</summary>
    public string? Description { get; set; }
}

/// <summary>2段階目: デコレーション</summary>
public class HifPouchDecoration : HifPouchStageEffect
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>1段階目: ぬいぐるみ</summary>
public class HifPouchPlush : HifPouchStageEffect
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<HifPouchDecoration> Decorations { get; set; } = new();
}

/// <summary>色 (段階0=未カスタマイズの効果を Base に持つ)</summary>
public class HifPouchColor
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public HifPouchStageEffect Base { get; set; } = new();
    public List<HifPouchPlush> Plushes { get; set; } = new();
}

/// <summary>ポーチ選択のプリセット (名前付きで保存・読込。Data/HifPouchPresets/hif_pouch_presets.yaml)</summary>
public class HifPouchPreset
{
    public string Name { get; set; } = string.Empty;
    /// <summary>null=未選択</summary>
    public string? Color { get; set; }
    public string? Plush { get; set; }
    public string? Decoration { get; set; }

    public override string ToString() => Name;
}

public class HifPouchPresetFile
{
    public List<HifPouchPreset> Presets { get; set; } = new();
}
