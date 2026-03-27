using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using GakumasuCalc.Models;
using SupportCardEditor.Services;

namespace SupportCardEditor.ViewModels;

public class CardEditorViewModel : ViewModelBase
{
    private readonly YamlExportService _exportService = new();
    private string _yamlPreview = string.Empty;
    private string _statusMessage = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _outputFileName = "ssr_cards.yaml";

    // --- 基本情報 ---
    private string _cardId = string.Empty;
    private string _cardName = string.Empty;
    private string _selectedRarity = "SSR";
    private string _selectedType = "vo";
    private PlanOption _selectedPlan;

    // --- イベントステータス (イベ2のステータス上昇) ---
    private int _eventStatus;

    // --- Pアイテム ---
    private string _pItemName = string.Empty;
    private TriggerOption? _pItemTrigger;
    private int _pItemValue;
    private int _pItemMaxCount;

    public List<string> Rarities { get; } = new() { "R", "SR", "SSR" };
    public List<string> CardTypes { get; } = new() { "vo", "da", "vi", "as" };
    public List<PlanOption> PlanOptions { get; } = new()
    {
        new("sense", "センス"),
        new("logic", "ロジック"),
        new("anomaly", "アノマリー"),
        new("free", "フリー"),
    };
    public static List<TriggerOption> AllTriggers { get; } = new()
    {
        new("equip_flat", "初期値"),
        new("equip_sp_rate", "SP率 (%)"),
        new("equip_para_bonus", "パラボ (%)"),
        new("sp_end", "SP終了時"),
        new("vo_sp_end", "VoSP終了時"),
        new("da_sp_end", "DaSP終了時"),
        new("vi_sp_end", "ViSP終了時"),
        new("lesson_end", "レッスン終了時"),
        new("vo_lesson_end", "Voレッスン終了時"),
        new("da_lesson_end", "Daレッスン終了時"),
        new("vi_lesson_end", "Viレッスン終了時"),
        new("class_end", "授業終了時"),
        new("outing_end", "お出かけ終了時"),
        new("consultation", "相談選択時"),
        new("activity_supply", "活動支給選択時"),
        new("exam_end", "試験終了時"),
        new("special_training", "特別指導開始時"),
        new("rest", "休憩選択時"),
        new("skill_ssr_acquire", "スキル(SSR)獲得時"),
        new("skill_enhance", "スキル強化時"),
        new("skill_delete", "スキル削除時"),
        new("skill_custom", "スキルカスタム時"),
        new("skill_change", "スキルチェンジ時"),
        new("active_enhance", "アクティブ強化時"),
        new("active_delete", "アクティブ削除時"),
        new("active_acquire", "アクティブ獲得時"),
        new("mental_acquire", "メンタル獲得時"),
        new("mental_enhance", "メンタル強化時"),
        new("genki_acquire", "元気カード獲得時"),
        new("good_condition_acquire", "好調カード獲得時"),
        new("good_impression_acquire", "好印象カード獲得時"),
        new("conserve_acquire", "温存カード獲得時"),
        new("p_item_acquire", "Pアイテム獲得時"),
        new("p_drink_acquire", "Pドリンク獲得時"),
        new("consultation_drink", "相談ドリンク交換後"),
        new("none", "ステータスに影響なし"),
    };

    // 固定6アビリティスロット
    public AbilitySlot Ability1 { get; }
    public AbilitySlot Ability2 { get; }
    public AbilitySlot Ability3 { get; }
    public AbilitySlot Ability4 { get; }
    public AbilitySlot Ability5 { get; }
    public AbilitySlot Ability6 { get; }

    // 基本情報プロパティ
    public string CardId { get => _cardId; set { SetProperty(ref _cardId, value); UpdatePreview(); } }
    public string CardName { get => _cardName; set { SetProperty(ref _cardName, value); UpdatePreview(); } }
    public string SelectedRarity
    {
        get => _selectedRarity;
        set
        {
            if (SetProperty(ref _selectedRarity, value))
            {
                CardId = GenerateNextId(_outputDirectory, value);
                UpdatePreview();
            }
        }
    }
    public string SelectedType { get => _selectedType; set { SetProperty(ref _selectedType, value); UpdatePreview(); } }
    public PlanOption SelectedPlan { get => _selectedPlan; set { SetProperty(ref _selectedPlan, value); UpdatePreview(); } }
    public int EventStatus { get => _eventStatus; set { SetProperty(ref _eventStatus, value); UpdatePreview(); } }

    // Pアイテム
    public string PItemName { get => _pItemName; set { SetProperty(ref _pItemName, value); UpdatePreview(); } }
    public TriggerOption? PItemTrigger { get => _pItemTrigger; set { SetProperty(ref _pItemTrigger, value); UpdatePreview(); } }
    public int PItemValue { get => _pItemValue; set { SetProperty(ref _pItemValue, value); UpdatePreview(); } }
    public int PItemMaxCount { get => _pItemMaxCount; set { SetProperty(ref _pItemMaxCount, value); UpdatePreview(); } }

    // 出力
    public string OutputDirectory { get => _outputDirectory; set => SetProperty(ref _outputDirectory, value); }
    public string OutputFileName { get => _outputFileName; set => SetProperty(ref _outputFileName, value); }
    public string YamlPreview { get => _yamlPreview; private set => SetProperty(ref _yamlPreview, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public ICommand ExportCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CopyYamlCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    public CardEditorViewModel()
    {
        _selectedPlan = PlanOptions[0];

        // 6アビリティスロット
        Ability1 = new AbilitySlot { Label = "アビリティ1" };
        Ability2 = new AbilitySlot { Label = "アビリティ2" };
        Ability3 = new AbilitySlot { Label = "アビリティ3" };
        Ability4 = new AbilitySlot { Label = "アビリティ4" };
        Ability5 = new AbilitySlot { Label = "アビリティ5" };
        Ability6 = new AbilitySlot { Label = "アビリティ6" };

        foreach (var abi in new[] { Ability1, Ability2, Ability3, Ability4, Ability5, Ability6 })
            abi.PropertyChanged += (_, _) => UpdatePreview();

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "Data", "SupportCards");
        if (!Directory.Exists(dataDir))
        {
            var projectRoot = FindProjectRoot(baseDir);
            if (projectRoot != null)
                dataDir = Path.Combine(projectRoot, "Data", "SupportCards");
        }
        _outputDirectory = dataDir;

        // IDを自動採番
        _cardId = GenerateNextId(dataDir, "SSR");

        ExportCommand = new RelayCommand(Export);
        ClearCommand = new RelayCommand(Clear);
        CopyYamlCommand = new RelayCommand(CopyYaml);
        BrowseOutputCommand = new RelayCommand(BrowseOutput);

        UpdatePreview();
    }

    private SupportCard BuildCard()
    {
        var stat = SelectedType == "as" ? "all" : SelectedType;

        var card = new SupportCard
        {
            Id = CardId,
            Name = CardName,
            Rarity = SelectedRarity,
            Type = SelectedType,
            Plan = SelectedPlan.Value,
            Effects = new List<CardEffect>()
        };

        // アビリティ → effects
        foreach (var abi in new[] { Ability1, Ability2, Ability3, Ability4, Ability5, Ability6 })
        {
            var effect = abi.ToCardEffect(stat);
            if (effect != null)
                card.Effects.Add(effect);
        }

        // イベントステータス
        if (EventStatus > 0)
            card.Effects.Add(new CardEffect
            {
                Trigger = "equip", Stat = stat, Values = Enumerable.Repeat((double)EventStatus, 5).ToList(),
                ValueType = "flat", Description = "イベントステータス"
            });

        // Pアイテム
        if (PItemTrigger != null && PItemTrigger.Value != "none" && PItemValue > 0)
            card.Effects.Add(new CardEffect
            {
                Trigger = PItemTrigger.Value,
                Stat = stat,
                Values = Enumerable.Repeat((double)PItemValue, 5).ToList(),
                ValueType = "flat",
                MaxCount = PItemMaxCount > 0 ? PItemMaxCount : null,
                Description = $"Pアイテム: {PItemName}"
            });

        return card;
    }

    private void UpdatePreview()
    {
        try
        {
            var card = BuildCard();
            YamlPreview = _exportService.SerializeCard(card);
        }
        catch (Exception ex)
        {
            YamlPreview = $"# プレビューエラー: {ex.Message}";
        }
    }

    private void Export()
    {
        try
        {
            var card = BuildCard();
            var filePath = Path.Combine(OutputDirectory, OutputFileName);
            Directory.CreateDirectory(OutputDirectory);
            _exportService.AppendToFile(filePath, card);
            StatusMessage = $"エクスポート完了: {filePath}";
        }
        catch (Exception ex) { StatusMessage = $"エラー: {ex.Message}"; }
    }

    private void Clear()
    {
        _selectedRarity = "SSR"; OnPropertyChanged(nameof(SelectedRarity));
        CardId = GenerateNextId(_outputDirectory, "SSR");
        CardName = string.Empty; SelectedType = "vo"; SelectedPlan = PlanOptions[0];
        EventStatus = 0;
        PItemName = string.Empty; PItemTrigger = null; PItemValue = 0; PItemMaxCount = 0;
        foreach (var abi in new[] { Ability1, Ability2, Ability3, Ability4, Ability5, Ability6 })
            abi.Clear();
        StatusMessage = "クリアしました";
    }

    private void CopyYaml()
    {
        try { Clipboard.SetText(YamlPreview); StatusMessage = "YAMLをクリップボードにコピーしました"; }
        catch { StatusMessage = "コピーに失敗しました"; }
    }

    private void BrowseOutput()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "出力先フォルダを選択" };
        if (dialog.ShowDialog() == true) OutputDirectory = dialog.FolderName;
    }

    /// <summary>
    /// 既存YAMLから最大IDを読み取り、次のIDを返す
    /// </summary>
    private static string GenerateNextId(string cardsDir, string rarity)
    {
        var prefix = rarity.ToLower() switch
        {
            "ssr" => "ssr",
            "sr" => "sr",
            "r" => "r",
            _ => "ssr"
        };

        int maxNum = 0;
        try
        {
            if (Directory.Exists(cardsDir))
            {
                foreach (var file in Directory.GetFiles(cardsDir, "*.yaml"))
                {
                    var text = File.ReadAllText(file);
                    foreach (System.Text.RegularExpressions.Match m in
                        System.Text.RegularExpressions.Regex.Matches(text, $@"id:\s*{prefix}_(\d+)"))
                    {
                        if (int.TryParse(m.Groups[1].Value, out var num) && num > maxNum)
                            maxNum = num;
                    }
                }
            }
        }
        catch { /* 読み取りエラーは無視 */ }

        return $"{prefix}_{maxNum + 1:D3}";
    }

    private static string? FindProjectRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GakumasuCalc.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

// ========== データモデル ==========

public class PlanOption
{
    public string Value { get; }
    public string DisplayName { get; }
    public PlanOption(string value, string display) { Value = value; DisplayName = display; }
    public override string ToString() => DisplayName;
}

public class TriggerOption
{
    public string Value { get; }
    public string DisplayName { get; }
    public TriggerOption(string value, string display) { Value = value; DisplayName = display; }
    public override string ToString() => DisplayName;
}

/// <summary>アビリティ1スロット分</summary>
public class AbilitySlot : ViewModelBase
{
    private string _description = string.Empty;
    private TriggerOption? _selectedTrigger;
    private double _value;
    private int _maxCount;

    public string Label { get; set; } = string.Empty;

    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public TriggerOption? SelectedTrigger { get => _selectedTrigger; set => SetProperty(ref _selectedTrigger, value); }
    public double Value { get => _value; set => SetProperty(ref _value, value); }
    public int MaxCount { get => _maxCount; set => SetProperty(ref _maxCount, value); }

    public CardEffect? ToCardEffect(string defaultStat)
    {
        if (SelectedTrigger == null || SelectedTrigger.Value == "none" || Value == 0)
            return null;

        var triggerKey = SelectedTrigger.Value;
        // エディタでは4凸値のみ入力。全凸同値の配列を生成
        var values = Enumerable.Repeat(Value, 5).ToList();

        // equip系の特殊処理
        if (triggerKey == "equip_flat")
            return new CardEffect { Trigger = "equip", Stat = defaultStat, Values = values, ValueType = "flat", Description = Description };
        if (triggerKey == "equip_sp_rate")
            return new CardEffect { Trigger = "equip", Stat = defaultStat, Values = values, ValueType = "sp_rate", Description = Description };
        if (triggerKey == "equip_para_bonus")
            return new CardEffect { Trigger = "equip", Stat = defaultStat, Values = values, ValueType = "para_bonus", Description = Description };

        return new CardEffect
        {
            Trigger = triggerKey,
            Stat = defaultStat,
            Values = values,
            ValueType = "flat",
            MaxCount = MaxCount > 0 ? MaxCount : null,
            Description = Description
        };
    }

    public void Clear()
    {
        Description = string.Empty;
        SelectedTrigger = null;
        Value = 0;
        MaxCount = 0;
    }
}

// EventEditorItem は旧互換用に残す
public class EventEditorItem : ViewModelBase
{
    private string _trigger = "week_1";
    private int _effectVo, _effectDa, _effectVi;
    public string Trigger { get => _trigger; set => SetProperty(ref _trigger, value); }
    public int EffectVo { get => _effectVo; set => SetProperty(ref _effectVo, value); }
    public int EffectDa { get => _effectDa; set => SetProperty(ref _effectDa, value); }
    public int EffectVi { get => _effectVi; set => SetProperty(ref _effectVi, value); }
}
