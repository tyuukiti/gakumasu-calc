using System.Collections.ObjectModel;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

/// <summary>
/// HIFモード (Hatsuboshi IDOL FESTIVAL) 用 ViewModel。
/// </summary>
public class HifViewModel : ViewModelBase
{
    private TrainingPlan? _hifPlan;
    private string? _errorMessage;
    private readonly HifSchedulePresetService? _presetService;
    private readonly HifBonusLevelsService? _bonusLevelsService;

    public HifViewModel(HifSchedulePresetService? presetService = null, HifBonusLevelsService? bonusLevelsService = null)
    {
        _presetService = presetService;
        _bonusLevelsService = bonusLevelsService;
        ApplyBulkLessonChoiceCommand = new RelayCommand(_ => ExecuteApplyBulkLessonChoice());
        ApplyBulkClassChoiceCommand = new RelayCommand(_ => ExecuteApplyBulkClassChoice());
        ApplyExamPresetCommand = new RelayCommand(p => ExecuteApplyExamPreset(p as string));
        SaveSchedulePresetCommand = new RelayCommand(_ => ExecuteSaveSchedulePreset(),
            _ => !string.IsNullOrWhiteSpace(NewPresetName));
        DeleteSchedulePresetCommand = new RelayCommand(_ => ExecuteDeleteSchedulePreset(),
            _ => _selectedSchedulePreset != null);
        ResetBonusLevelsCommand = new RelayCommand(_ => ExecuteResetBonusLevels());
        LoadSchedulePresets();
        LoadBonusLevels();
    }

    /// <summary>HIFボーナスレベル設定 (パネル毎)</summary>
    public HifBonusLevels BonusLevels { get; private set; } = new();

    private void LoadBonusLevels()
    {
        if (_bonusLevelsService == null) return;
        try
        {
            BonusLevels = _bonusLevelsService.Load();
            OnPropertyChanged(nameof(VoUpLevel));
            OnPropertyChanged(nameof(DaUpLevel));
            OnPropertyChanged(nameof(ViUpLevel));
            OnPropertyChanged(nameof(SpRateLevel));
            OnPropertyChanged(nameof(HpRecoveryLevel));
            OnPropertyChanged(nameof(FinalStatLimitLevel));
            OnPropertyChanged(nameof(PreExamPpLevel));
            OnPropertyChanged(nameof(FinalPpLevel));
            OnPropertyChanged(nameof(ConsultationDiscountLevel));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HIFボーナス読込エラー: {ex.Message}");
        }
    }

    private void SaveBonusLevels()
    {
        if (_bonusLevelsService == null) return;
        try { _bonusLevelsService.Save(BonusLevels); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HIFボーナス保存エラー: {ex.Message}"); }
    }

    public int VoUpLevel
    {
        get => BonusLevels.VoUpLevel;
        set { if (BonusLevels.VoUpLevel != value) { BonusLevels.VoUpLevel = Math.Clamp(value, 0, 5); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(VoUpEffectText)); } }
    }
    public int DaUpLevel
    {
        get => BonusLevels.DaUpLevel;
        set { if (BonusLevels.DaUpLevel != value) { BonusLevels.DaUpLevel = Math.Clamp(value, 0, 5); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(DaUpEffectText)); } }
    }
    public int ViUpLevel
    {
        get => BonusLevels.ViUpLevel;
        set { if (BonusLevels.ViUpLevel != value) { BonusLevels.ViUpLevel = Math.Clamp(value, 0, 5); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(ViUpEffectText)); } }
    }
    public int SpRateLevel
    {
        get => BonusLevels.SpRateLevel;
        set { if (BonusLevels.SpRateLevel != value) { BonusLevels.SpRateLevel = Math.Clamp(value, 0, 5); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(SpRateEffectText)); } }
    }
    public int HpRecoveryLevel
    {
        get => BonusLevels.HpRecoveryLevel;
        set { if (BonusLevels.HpRecoveryLevel != value) { BonusLevels.HpRecoveryLevel = Math.Clamp(value, 0, 6); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(HpRecoveryEffectText)); } }
    }
    public int FinalStatLimitLevel
    {
        get => BonusLevels.FinalStatLimitLevel;
        set { if (BonusLevels.FinalStatLimitLevel != value) { BonusLevels.FinalStatLimitLevel = Math.Clamp(value, 0, 6); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(FinalStatLimitEffectText)); } }
    }
    public int PreExamPpLevel
    {
        get => BonusLevels.PreExamPpLevel;
        set { if (BonusLevels.PreExamPpLevel != value) { BonusLevels.PreExamPpLevel = Math.Clamp(value, 0, 6); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(PreExamPpEffectText)); } }
    }
    public int FinalPpLevel
    {
        get => BonusLevels.FinalPpLevel;
        set { if (BonusLevels.FinalPpLevel != value) { BonusLevels.FinalPpLevel = Math.Clamp(value, 0, 6); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(FinalPpEffectText)); } }
    }
    public int ConsultationDiscountLevel
    {
        get => BonusLevels.ConsultationDiscountLevel;
        set { if (BonusLevels.ConsultationDiscountLevel != value) { BonusLevels.ConsultationDiscountLevel = Math.Clamp(value, 0, 6); SaveBonusLevels(); OnPropertyChanged(); OnPropertyChanged(nameof(ConsultationDiscountEffectText)); } }
    }

    public string VoUpEffectText => VoUpLevel > 0 ? $"+{HifBonusTables.StatUpFlat[VoUpLevel]} / +{HifBonusTables.StatUpPara[VoUpLevel]}%" : "未解放";
    public string DaUpEffectText => DaUpLevel > 0 ? $"+{HifBonusTables.StatUpFlat[DaUpLevel]} / +{HifBonusTables.StatUpPara[DaUpLevel]}%" : "未解放";
    public string ViUpEffectText => ViUpLevel > 0 ? $"+{HifBonusTables.StatUpFlat[ViUpLevel]} / +{HifBonusTables.StatUpPara[ViUpLevel]}%" : "未解放";
    public string SpRateEffectText => SpRateLevel > 0 ? $"+{HifBonusTables.SpRateIncrease[SpRateLevel]}%" : "未解放";
    public string HpRecoveryEffectText => HpRecoveryLevel > 0 ? $"{HifBonusTables.HpRecovery[HpRecoveryLevel]}%" : "未解放";
    public string FinalStatLimitEffectText => FinalStatLimitLevel > 0 ? $"+{HifBonusTables.FinalCapBonus[FinalStatLimitLevel]}" : "未解放";
    public string PreExamPpEffectText => PreExamPpLevel > 0 ? $"+{HifBonusTables.PpIncrease[PreExamPpLevel]}" : "未解放";
    public string FinalPpEffectText => FinalPpLevel > 0 ? $"+{HifBonusTables.PpIncrease[FinalPpLevel]}" : "未解放";
    public string ConsultationDiscountEffectText => ConsultationDiscountLevel > 0 ? $"{HifBonusTables.ConsultationDiscount[ConsultationDiscountLevel]}%" : "未解放";

    public ICommand ResetBonusLevelsCommand { get; }
    private void ExecuteResetBonusLevels()
    {
        BonusLevels = new HifBonusLevels(); // デフォルト MAX
        SaveBonusLevels();
        OnPropertyChanged(nameof(VoUpLevel)); OnPropertyChanged(nameof(DaUpLevel)); OnPropertyChanged(nameof(ViUpLevel));
        OnPropertyChanged(nameof(SpRateLevel)); OnPropertyChanged(nameof(HpRecoveryLevel));
        OnPropertyChanged(nameof(FinalStatLimitLevel));
        OnPropertyChanged(nameof(PreExamPpLevel)); OnPropertyChanged(nameof(FinalPpLevel));
        OnPropertyChanged(nameof(ConsultationDiscountLevel));
        OnPropertyChanged(nameof(VoUpEffectText)); OnPropertyChanged(nameof(DaUpEffectText)); OnPropertyChanged(nameof(ViUpEffectText));
        OnPropertyChanged(nameof(SpRateEffectText)); OnPropertyChanged(nameof(HpRecoveryEffectText));
        OnPropertyChanged(nameof(FinalStatLimitEffectText));
        OnPropertyChanged(nameof(PreExamPpEffectText)); OnPropertyChanged(nameof(FinalPpEffectText));
        OnPropertyChanged(nameof(ConsultationDiscountEffectText));
    }

    public int MaxSchedulePresets => HifSchedulePresetService.MaxPresets;
    public string SchedulePresetCountText => $"{SchedulePresets.Count}/{MaxSchedulePresets}";

    public ObservableCollection<HifSchedulePreset> SchedulePresets { get; } = new();

    private HifSchedulePreset? _selectedSchedulePreset;
    public HifSchedulePreset? SelectedSchedulePreset
    {
        get => _selectedSchedulePreset;
        set
        {
            if (SetProperty(ref _selectedSchedulePreset, value))
            {
                if (value != null) LoadSchedulePresetIntoItems(value);
            }
        }
    }

    private string _newPresetName = string.Empty;
    public string NewPresetName
    {
        get => _newPresetName;
        set => SetProperty(ref _newPresetName, value);
    }

    public ICommand SaveSchedulePresetCommand { get; }
    public ICommand DeleteSchedulePresetCommand { get; }

    private void LoadSchedulePresets()
    {
        if (_presetService == null) return;
        try
        {
            var loaded = _presetService.Load();
            SchedulePresets.Clear();
            foreach (var p in loaded) SchedulePresets.Add(p);
            OnPropertyChanged(nameof(SchedulePresetCountText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HIFプリセット読込エラー: {ex.Message}");
        }
    }

    private void LoadSchedulePresetIntoItems(HifSchedulePreset preset)
    {
        // ScheduleItems の各日に Choices / ExamAllocations を反映
        var choicesByWeek = preset.Choices.ToDictionary(c => c.Week);
        var allocsByWeek = preset.ExamAllocations.ToDictionary(a => a.Week);
        foreach (var item in ScheduleItems)
        {
            if (item.IsFixed)
            {
                if (item.IsExam && allocsByWeek.TryGetValue(item.Week, out var alloc))
                {
                    item.ExamVoAlloc = alloc.Vo;
                    item.ExamDaAlloc = alloc.Da;
                    item.ExamViAlloc = alloc.Vi;
                }
                continue;
            }
            if (!choicesByWeek.TryGetValue(item.Week, out var choice)) continue;
            if (item.IsPublicLesson)
            {
                var mainStat = choice.Action.Split('_')[0];
                item.MainStat = mainStat;
                item.SubStat = choice.SubStat;
                item.LessonCombo = $"{mainStat}-{choice.SubStat}";
            }
            else
            {
                item.SelectedAction = choice.Action;
            }
        }
    }

    private void ExecuteSaveSchedulePreset()
    {
        if (_presetService == null) return;
        var name = NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var choices = new List<HifScheduleChoiceEntry>();
        var allocs = new List<HifExamAllocationEntry>();
        foreach (var item in ScheduleItems)
        {
            if (item.IsFixed)
            {
                if (item.IsExam && item.ExamDistributed > 0)
                {
                    allocs.Add(new HifExamAllocationEntry
                    {
                        Week = item.Week,
                        Vo = item.ExamVoAlloc, Da = item.ExamDaAlloc, Vi = item.ExamViAlloc,
                    });
                }
                continue;
            }
            if (item.IsPublicLesson && item.MainStat != null && item.SubStat != null)
            {
                choices.Add(new HifScheduleChoiceEntry
                {
                    Week = item.Week,
                    Action = $"{item.MainStat}_lesson",
                    SubStat = item.SubStat,
                });
            }
            else if (item.SelectedAction != null)
            {
                choices.Add(new HifScheduleChoiceEntry
                {
                    Week = item.Week,
                    Action = item.SelectedAction,
                    SubStat = null,
                });
            }
        }

        var preset = new HifSchedulePreset
        {
            Name = name,
            Choices = choices,
            ExamAllocations = allocs,
        };

        // 同名は上書き
        var existing = SchedulePresets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            int idx = SchedulePresets.IndexOf(existing);
            SchedulePresets[idx] = preset;
        }
        else
        {
            if (SchedulePresets.Count >= MaxSchedulePresets)
            {
                System.Windows.MessageBox.Show(
                    $"プリセットは最大{MaxSchedulePresets}件まで保存できます。",
                    "上限到達", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            SchedulePresets.Add(preset);
        }

        try { _presetService.Save(SchedulePresets.ToList()); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HIFプリセット保存エラー: {ex.Message}");
        }
        OnPropertyChanged(nameof(SchedulePresetCountText));
        NewPresetName = string.Empty;
    }

    private void ExecuteDeleteSchedulePreset()
    {
        if (_presetService == null || _selectedSchedulePreset == null) return;
        var name = _selectedSchedulePreset.Name;
        var target = SchedulePresets.FirstOrDefault(p => p.Name == name);
        if (target == null) return;
        SchedulePresets.Remove(target);
        try { _presetService.Save(SchedulePresets.ToList()); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HIFプリセット削除エラー: {ex.Message}");
        }
        SelectedSchedulePreset = null;
        OnPropertyChanged(nameof(SchedulePresetCountText));
    }

    /// <summary>一括設定: 公開レッスンのデフォルトメイン属性。</summary>
    private string _bulkMainStat = "vo";
    public string BulkMainStat
    {
        get => _bulkMainStat;
        set
        {
            if (SetProperty(ref _bulkMainStat, value))
            {
                if (_bulkSubStat == value)
                {
                    BulkSubStat = new[] { "vo", "da", "vi" }.First(s => s != value);
                }
                OnPropertyChanged(nameof(AvailableBulkSubStats));
            }
        }
    }

    /// <summary>一括設定: 公開レッスンのデフォルトサブ属性。</summary>
    private string _bulkSubStat = "da";
    public string BulkSubStat
    {
        get => _bulkSubStat;
        set => SetProperty(ref _bulkSubStat, value);
    }

    /// <summary>BulkMainStat 以外のサブ属性候補。</summary>
    public List<HifStatOption> AvailableBulkSubStats =>
        new List<HifStatOption>
        {
            new() { Value = "vo", Label = "Vocal" },
            new() { Value = "da", Label = "Dance" },
            new() { Value = "vi", Label = "Visual" },
        }.Where(o => o.Value != _bulkMainStat).ToList();

    public List<HifStatOption> AllStatOptions { get; } = new()
    {
        new() { Value = "vo", Label = "Vocal" },
        new() { Value = "da", Label = "Dance" },
        new() { Value = "vi", Label = "Visual" },
    };

    /// <summary>一括設定: 授業のデフォルト属性。</summary>
    private string _bulkClassStat = "vo";
    public string BulkClassStat
    {
        get => _bulkClassStat;
        set => SetProperty(ref _bulkClassStat, value);
    }

    public ICommand ApplyBulkLessonChoiceCommand { get; }
    public ICommand ApplyBulkClassChoiceCommand { get; }
    public ICommand ApplyExamPresetCommand { get; }

    private void ExecuteApplyBulkLessonChoice()
    {
        if (_bulkMainStat == _bulkSubStat) return;
        foreach (var item in ScheduleItems)
        {
            if (!item.IsPublicLesson) continue;
            item.MainStat = _bulkMainStat;
            item.SubStat = _bulkSubStat;
            item.LessonCombo = $"{_bulkMainStat}-{_bulkSubStat}";
        }
    }

    private void ExecuteApplyBulkClassChoice()
    {
        var targetAction = $"{_bulkClassStat}_class";
        foreach (var item in ScheduleItems)
        {
            // 授業日: 全アクションが _class で終わる週、かつ対象アクションが選択肢にある
            if (item.IsFixed || item.IsPublicLesson) continue;
            if (item.ActionOptions.Count == 0) continue;
            bool allClass = item.ActionOptions.All(o => o.Value.EndsWith("_class"));
            if (!allClass) continue;
            if (item.ActionOptions.Any(o => o.Value == targetAction))
                item.SelectedAction = targetAction;
        }
    }

    private void ExecuteApplyExamPreset(string? preset)
    {
        if (string.IsNullOrEmpty(preset)) return;
        foreach (var item in ScheduleItems)
        {
            if (!item.IsExam || item.ExamDistributed <= 0) continue;
            int d = item.ExamDistributed;
            switch (preset)
            {
                case "vo_all":
                    item.ExamVoAlloc = d; item.ExamDaAlloc = 0; item.ExamViAlloc = 0;
                    break;
                case "da_all":
                    item.ExamVoAlloc = 0; item.ExamDaAlloc = d; item.ExamViAlloc = 0;
                    break;
                case "vi_all":
                    item.ExamVoAlloc = 0; item.ExamDaAlloc = 0; item.ExamViAlloc = d;
                    break;
                case "equal":
                    int q = d / 3;
                    int r = d - q * 3;
                    item.ExamVoAlloc = q + r;
                    item.ExamDaAlloc = q;
                    item.ExamViAlloc = q;
                    break;
            }
        }
    }

    /// <summary>
    /// HIFプラン (Data/Plans/hif.yaml から読み込まれたもの)。
    /// MainViewModel から初期化時に注入される。
    /// </summary>
    public TrainingPlan? HifPlan
    {
        get => _hifPlan;
        set
        {
            if (SetProperty(ref _hifPlan, value))
            {
                PopulateScheduleItems();
                OnPropertyChanged(nameof(IsPlanAvailable));
                OnPropertyChanged(nameof(PlanStatusText));
                OnPropertyChanged(nameof(ScheduleDayCount));
            }
        }
    }

    public bool IsPlanAvailable => _hifPlan != null;

    public string PlanStatusText => _hifPlan != null
        ? $"HIFプラン読み込み済み (全 {_hifPlan.Schedule.Count} 日)"
        : "HIFプランが読み込まれていません。Data/Plans/hif.yaml を確認してください。";

    public int ScheduleDayCount => _hifPlan?.Schedule.Count ?? 0;

    /// <summary>
    /// スケジュール調整UIに表示する各日項目。
    /// </summary>
    public ObservableCollection<HifScheduleItemViewModel> ScheduleItems { get; } = new();

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// HifPlan を元に ScheduleItems を初期化（デフォルト選択値も設定）。
    /// </summary>
    private void PopulateScheduleItems()
    {
        ScheduleItems.Clear();
        if (_hifPlan == null) return;

        foreach (var week in _hifPlan.Schedule)
        {
            var item = HifScheduleItemViewModel.Build(week);
            ScheduleItems.Add(item);
        }
    }
}

/// <summary>
/// HIF スケジュール調整の1日分項目。
/// </summary>
public class HifScheduleItemViewModel : ViewModelBase
{
    public int Week { get; init; }
    public string DayLabel { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public string WeekType { get; init; } = "free";
    public string? EventName { get; init; }
    public bool IsFixed { get; init; }
    public bool IsPublicLesson { get; init; }
    public bool IsSingleOption { get; init; }
    /// <summary>試験日（配分入力UIを表示するかどうか）。基礎値か配分値のいずれかが正なら true。</summary>
    public bool IsExam { get; init; }
    /// <summary>試験日の基礎値（3属性すべてに同値加算される）。</summary>
    public int ExamBase { get; init; }
    /// <summary>試験日の配分値合計（ユーザが Vo/Da/Vi に振り分ける）。</summary>
    public int ExamDistributed { get; init; }

    private int _examVoAlloc;
    public int ExamVoAlloc
    {
        get => _examVoAlloc;
        set
        {
            if (SetProperty(ref _examVoAlloc, Math.Max(0, value)))
                OnPropertyChanged(nameof(ExamAllocSummary));
        }
    }

    private int _examDaAlloc;
    public int ExamDaAlloc
    {
        get => _examDaAlloc;
        set
        {
            if (SetProperty(ref _examDaAlloc, Math.Max(0, value)))
                OnPropertyChanged(nameof(ExamAllocSummary));
        }
    }

    private int _examViAlloc;
    public int ExamViAlloc
    {
        get => _examViAlloc;
        set
        {
            if (SetProperty(ref _examViAlloc, Math.Max(0, value)))
                OnPropertyChanged(nameof(ExamAllocSummary));
        }
    }

    public string ExamBaseText => ExamBase > 0 ? $"基礎 +{ExamBase}/属性" : string.Empty;

    public string ExamAllocSummary
    {
        get
        {
            if (ExamDistributed <= 0) return string.Empty;
            int used = ExamVoAlloc + ExamDaAlloc + ExamViAlloc;
            return $"配分 {used}/{ExamDistributed}";
        }
    }

    /// <summary>選択肢一覧（公開レッスン以外）。Label + Value のペア。</summary>
    public List<HifActionOption> ActionOptions { get; set; } = new();

    /// <summary>メイン属性選択肢（公開レッスン用）。</summary>
    public List<HifStatOption> MainStatOptions { get; set; } = new();

    private string? _selectedAction;
    public string? SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    private string? _mainStat;
    /// <summary>公開レッスン日のメイン属性 (vo/da/vi)。</summary>
    public string? MainStat
    {
        get => _mainStat;
        set
        {
            if (SetProperty(ref _mainStat, value))
            {
                OnPropertyChanged(nameof(AvailableSubStats));
                OnPropertyChanged(nameof(MainStatBonusText));
                OnPropertyChanged(nameof(LessonComboText));
                // サブが新メインと同一なら強制変更
                if (_subStat == value)
                {
                    SubStat = AvailableSubStats.FirstOrDefault()?.Value;
                }
                // 連動して SelectedAction も更新
                if (value != null)
                    SelectedAction = $"{value}_lesson";
            }
        }
    }

    private string? _subStat;
    /// <summary>公開レッスン日のサブ属性 (vo/da/vi)。メイン属性以外の2属性から1つ選択。</summary>
    public string? SubStat
    {
        get => _subStat;
        set
        {
            if (SetProperty(ref _subStat, value))
                OnPropertyChanged(nameof(LessonComboText));
        }
    }

    /// <summary>「メイン-サブ」の組み合わせ値 ("vo-da" 等)。1ドロップダウン化のため。</summary>
    private string? _lessonCombo;
    public string? LessonCombo
    {
        get => _lessonCombo ?? (MainStat != null && SubStat != null ? $"{MainStat}-{SubStat}" : null);
        set
        {
            if (value == null) return;
            var parts = value.Split('-');
            if (parts.Length != 2) return;
            _lessonCombo = value;
            MainStat = parts[0];
            SubStat = parts[1];
            OnPropertyChanged();
        }
    }

    public string LessonComboText
    {
        get
        {
            if (MainStat == null || SubStat == null || _mainValueByStat == null) return string.Empty;
            var mainV = _mainValueByStat.TryGetValue(MainStat, out var v) ? v : 0;
            return $"{MainStat.ToUpper()}+{mainV} / {SubStat.ToUpper()}+{HifSubValue}";
        }
    }

    /// <summary>公開レッスンの6パターン (Vo→Da, Vo→Vi, ...)。</summary>
    public List<HifActionOption> LessonComboOptions { get; } = new()
    {
        new() { Value = "vo-da", Label = "Vo → Da" },
        new() { Value = "vo-vi", Label = "Vo → Vi" },
        new() { Value = "da-vo", Label = "Da → Vo" },
        new() { Value = "da-vi", Label = "Da → Vi" },
        new() { Value = "vi-vo", Label = "Vi → Vo" },
        new() { Value = "vi-da", Label = "Vi → Da" },
    };

    public int HifSubValue { get; init; }
    public string HifSubValueText => HifSubValue > 0 ? $"サブ +{HifSubValue}" : string.Empty;

    /// <summary>メイン属性に応じた上昇値表示テキスト。</summary>
    public string MainStatBonusText
    {
        get
        {
            if (_mainValueByStat == null || _mainStat == null) return string.Empty;
            return _mainValueByStat.TryGetValue(_mainStat, out var v) ? $"+{v}" : string.Empty;
        }
    }

    /// <summary>メイン属性以外のサブ属性候補。</summary>
    public List<HifStatOption> AvailableSubStats
    {
        get
        {
            if (_mainStat == null) return MainStatOptions;
            return MainStatOptions.Where(o => o.Value != _mainStat).ToList();
        }
    }

    private Dictionary<string, int>? _mainValueByStat;

    /// <summary>WeekSchedule から ViewModel を構築。</summary>
    public static HifScheduleItemViewModel Build(WeekSchedule week)
    {
        bool isFixed = week.IsFixedEvent;
        bool isPublic = week.Type == "public_lesson";
        int examBase = week.HifExamBase ?? 0;
        int examDistributed = week.HifExamDistributed ?? 0;
        bool isExam = isFixed && (examBase > 0 || examDistributed > 0);

        var item = new HifScheduleItemViewModel
        {
            Week = week.Week,
            DayLabel = BuildDayLabel(week),
            TypeLabel = BuildTypeLabel(week),
            WeekType = week.Type,
            EventName = week.EventName,
            IsFixed = isFixed,
            IsPublicLesson = isPublic,
            IsSingleOption = !isFixed && !isPublic && week.AvailableActions.Count == 1,
            HifSubValue = week.HifSubValue ?? 0,
            IsExam = isExam,
            ExamBase = examBase,
            ExamDistributed = examDistributed,
        };

        if (isFixed)
        {
            // 固定イベントは表示のみ（配分入力UIは IsExam で別途扱う）
            return item;
        }

        if (isPublic)
        {
            // 公開レッスン: メイン/サブの2選択
            item.MainStatOptions = new List<HifStatOption>
            {
                new() { Value = "vo", Label = "Vocal" },
                new() { Value = "da", Label = "Dance" },
                new() { Value = "vi", Label = "Visual" },
            };
            item._mainValueByStat = new Dictionary<string, int>();
            foreach (var l in week.Lessons)
            {
                // sp_bonus は メイン属性のみに値が入っている (例: vo lesson は {vo:60, da:0, vi:0})
                int val = l.Type switch
                {
                    "vo" => l.SpBonus.Vo,
                    "da" => l.SpBonus.Da,
                    "vi" => l.SpBonus.Vi,
                    _ => 0,
                };
                item._mainValueByStat[l.Type] = val;
            }
            // デフォルト: メイン=vo, サブ=da
            item._mainStat = "vo";
            item._subStat = "da";
            item._selectedAction = "vo_lesson";
            return item;
        }

        // その他 (free 系) → 1つのプルダウン
        item.ActionOptions = week.AvailableActions.Select(a => new HifActionOption
        {
            Value = a,
            Label = BuildActionLabel(a, week),
        }).ToList();

        if (item.ActionOptions.Count > 0)
        {
            // その他選択日のデフォルト優先度: 活動支給 > 相談 > お出かけ > 特別指導
            var priority = new[] { "activity_supply", "consultation", "outing", "special_training" };
            var picked = priority.FirstOrDefault(p => item.ActionOptions.Any(o => o.Value == p));
            item._selectedAction = picked ?? item.ActionOptions[0].Value;
        }

        return item;
    }

    private static string BuildDayLabel(WeekSchedule week)
    {
        if (week.Week <= 20) return $"Day {week.Week}";
        return week.Week switch
        {
            27 => "本戦R1",
            28 => "本戦インターバル",
            29 => "本戦R2",
            _ => $"本戦{week.Week - 20}日目",
        };
    }

    private static string BuildTypeLabel(WeekSchedule week)
    {
        if (week.Type == "audition") return "固定イベント";
        if (week.Type == "public_lesson") return "公開レッスン";
        if (week.AvailableActions.Any(a => a.EndsWith("_class"))) return "授業";
        if (week.AvailableActions.Count == 1) return ActionLabel(week.AvailableActions[0]);
        return string.Join(" / ", week.AvailableActions.Select(ActionLabel));
    }

    private static string BuildActionLabel(string action, WeekSchedule week)
    {
        if (action.EndsWith("_class"))
        {
            var stat = action.Substring(0, 2);
            int val = week.Classes.FirstOrDefault(c => c.Type == stat) is { } cls
                ? (stat switch { "vo" => cls.SpBonus.Vo, "da" => cls.SpBonus.Da, "vi" => cls.SpBonus.Vi, _ => 0 })
                : 0;
            return $"{stat.ToUpper()}授業 (+{val})";
        }
        return ActionLabel(action);
    }

    private static string ActionLabel(string action) => action switch
    {
        "outing" => "お出かけ",
        "consultation" => "相談",
        "activity_supply" => "活動支給",
        "special_training" => "特別指導",
        "vo_class" => "Vo授業",
        "da_class" => "Da授業",
        "vi_class" => "Vi授業",
        "vo_lesson" => "Voレッスン",
        "da_lesson" => "Daレッスン",
        "vi_lesson" => "Viレッスン",
        _ => action,
    };
}

public class HifActionOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public class HifStatOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}
