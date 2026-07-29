namespace GakumasuCalc.Models;

/// <summary>
/// HIF条件プリセット（HIFタブの入力条件一式を名前付きで保存・読み込み）。
/// 凸トグル・HIFボーナスLv・MAX超過再抽選は別途永続化されるアカウント状態のため含めない。
/// 旧バージョンのファイルは IgnoreUnmatchedProperties と既定値により部分読込される。
/// </summary>
public class HifConditionPreset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>HIFタブ固有の条件（スケジュール・試験配分・一括設定）</summary>
    public HifConditionHifFields Hif { get; set; } = new();

    /// <summary>タブ共有の条件（育成タイプ・SP枚数・イベント回数・カード指定・メモリー等）</summary>
    public HifConditionCalcFields Calc { get; set; } = new();

    public override string ToString() => Name;
}

public class HifConditionHifFields
{
    /// <summary>各日のユーザ選択（HifSchedulePreset と同形式）</summary>
    public List<HifScheduleChoiceEntry> Choices { get; set; } = new();

    /// <summary>試験日の配分（Day → Vo/Da/Vi 振り分け）</summary>
    public List<HifExamAllocationEntry> ExamAllocations { get; set; } = new();

    /// <summary>全試験共通の配分比率（合計100）。ExamAllocations 欠落時のフォールバック</summary>
    public int ExamRatioVo { get; set; } = 34;
    public int ExamRatioDa { get; set; } = 33;
    public int ExamRatioVi { get; set; } = 33;

    /// <summary>一括設定: 公開レッスンのメイン属性（vo/da/vi）</summary>
    public string BulkMainStat { get; set; } = "vo";
    /// <summary>一括設定: 公開レッスンのサブ属性（vo/da/vi）</summary>
    public string BulkSubStat { get; set; } = "da";
    /// <summary>一括設定: 授業の属性（vo/da/vi）</summary>
    public string BulkClassStat { get; set; } = "vo";
}

public class HifConditionCalcFields
{
    /// <summary>育成タイプ（sense/logic/anomaly）</summary>
    public string SelectedPlanType { get; set; } = "sense";

    public int VoSpCount { get; set; }
    public int DaSpCount { get; set; }
    public int ViSpCount { get; set; }

    /// <summary>イベント回数（23項目）</summary>
    public AdditionalCounts Counts { get; set; } = new();

    /// <summary>選択中のイベント回数テンプレート名（表示用）</summary>
    public string? SelectedTemplateName { get; set; }

    public bool OwnedOnly { get; set; }
    public bool ContestMode { get; set; }

    /// <summary>必須カードID（最大6枚）</summary>
    public List<string> RequiredCardIds { get; set; } = new();

    /// <summary>除外カードID</summary>
    public List<string> ExcludedCardIds { get; set; } = new();

    /// <summary>持ち込みメモリー（4枠）</summary>
    public List<MemoryBonus> MemoryBonuses { get; set; } = new();

    /// <summary>
    /// 選択キャラID。null=キャラなしとして復元する。
    /// 既定値の空文字はフィールド未保存（旧ファイル等）を表し、復元時は現在の選択を維持する。
    /// </summary>
    public string? SelectedCharacterId { get; set; } = "";
}

public class HifConditionPresetFile
{
    public List<HifConditionPreset> Presets { get; set; } = new();
}
