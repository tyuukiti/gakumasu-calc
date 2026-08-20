using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly StatusCalculationService _calculationService;
    private readonly CardScoringService _scoringService;
    private readonly PlanLoaderService _planLoader;
    private readonly SupportCardLoaderService _cardLoader;
    private readonly InventoryService _inventoryService;
    private readonly CharacterLoaderService _characterLoader;
    private readonly MemoryPresetService _memoryPresetService;
    private readonly EventCountPresetService _eventCountPresetService;
    private readonly HifConditionPresetService _hifConditionPresetService;
    private readonly VersionCheckService _versionCheckService;
    private readonly UiStateService _uiStateService;
    private List<SupportCard> _allCards = new();
    private List<CardInventoryEntry> _inventory = new();
    private Character? _selectedCharacter;

    private TrainingPlan? _selectedPlan;
    private CalculationResult? _result;
    private CalculationResult? _resultWithoutCharacter;

    // 所持カードフィルタ
    private bool _ownedOnly;
    private bool _contestMode;

    // 必須カード
    private SupportCard? _selectedRequiredCard;

    // 除外カード
    private SupportCard? _selectedExcludedCard;

    // 育成タイプ
    private string _selectedPlanType = "sense";

    // 属性設定
    private string _voRole = "サブ";
    private string _daRole = "サブ";
    private string _viRole = "サブ";
    private int _voSpCount;
    private int _daSpCount;
    private int _viSpCount;

    // 追加カウント
    private int _pDrinkAcquire;
    private int _pItemAcquire;
    private int _skillAcquire;
    private int _skillSsrAcquire;
    private int _skillEnhance;
    private int _skillDelete;
    private int _skillCustom;
    private int _skillChange;
    private int _activeEnhance;
    private int _activeDelete;
    private int _mentalAcquire;
    private int _mentalEnhance;
    private int _mentalDelete;
    private int _activeAcquire;
    private int _genkiAcquire;
    private int _goodConditionAcquire;
    private int _goodImpressionAcquire;
    private int _conserveAcquire;
    private int _concentrateAcquire;
    private int _motivationAcquire;
    private int _fullpowerAcquire;
    private int _aggressiveAcquire;
    private int _consultationDrink;

    // パターン計算の元データ保持
    private List<CardScoringService.DeckResult> _deckResults = new();
    private List<string> _lastMainStats = new();
    private int _lastLessonWeekCount;

    // HIFモード計算用の状態
    private bool _isHifMode;
    private TrainingPlan? _hifDynamicPlan;
    private List<TurnChoice> _hifTurnChoices = new();

    // 日程方式 (初レジェンド / NIA) 計算用の状態
    private bool _isScheduleMode;
    private List<TurnChoice> _scheduleTurnChoices = new();
    /// <summary>タブ切替で日程編集を失わないための planId→(week→action) キャッシュ。</summary>
    private readonly Dictionary<string, Dictionary<int, ActionType>> _scheduleSelectionCache = new();
    /// <summary>日程方式プランごとのプリセット永続化サービス。</summary>
    private readonly Dictionary<string, SchedulePresetService> _schedulePresetServices = new();
    /// <summary>現在の選択プランが日程方式 (初レジェンド / NIA) かどうか。</summary>
    private bool IsExplicitSchedulePlan => _selectedPlan?.Id is "hatsu_legend" or "nia";

    public ObservableCollection<TrainingPlan> AvailablePlans { get; } = new();
    public ObservableCollection<TurnChoiceViewModel> TurnChoices { get; } = new();
    public ObservableCollection<DeckCardViewModel> DeckCards { get; } = new();
    /// <summary>選択デッキ6枚を行動別に合算したアビリティまとめ (total 降順)。</summary>
    public ObservableCollection<AbilitySummaryEntryViewModel> DeckAbilitySummary { get; } = new();
    public System.Windows.Visibility DeckAbilitySummaryVisibility =>
        DeckAbilitySummary.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public ObservableCollection<PatternResultViewModel> PatternResults { get; } = new();
    public ObservableCollection<CharacterTileViewModel> CharacterTiles { get; } = new();
    public ObservableCollection<MemoryBonusViewModel> MemoryBonuses { get; } = new();
    public ObservableCollection<MemoryPreset> MemoryPresets { get; } = new();

    /// <summary>
    /// HIFモード用 ViewModel。MainWindow の TabControl の HIF タブで使用。
    /// HIFプランは LoadData() 内で設定される。
    /// </summary>
    public HifViewModel HifVm { get; private set; } = null!;

    public List<string> RoleOptions { get; } = new() { "メイン1", "メイン2", "サブ" };
    public List<PlanTypeOption> PlanTypeOptions { get; } = new()
    {
        new("sense", "センス"),
        new("logic", "ロジック"),
        new("anomaly", "アノマリー"),
    };

    public bool OwnedOnly
    {
        get => _ownedOnly;
        set => SetProperty(ref _ownedOnly, value);
    }

    public bool ContestMode
    {
        get => _contestMode;
        set => SetProperty(ref _contestMode, value);
    }

    public string SelectedPlanType
    {
        get => _selectedPlanType;
        set
        {
            if (SetProperty(ref _selectedPlanType, value))
                FilterEventCountTemplates();
        }
    }

    public TrainingPlan? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            // プラン切替前に、現在の日程編集をキャッシュ（タブ往復で編集を保持）
            CacheScheduleSelections();
            if (SetProperty(ref _selectedPlan, value))
                OnPlanChanged();
        }
    }

    // 属性ロール
    public string VoRole { get => _voRole; set => SetProperty(ref _voRole, value); }
    public string DaRole { get => _daRole; set => SetProperty(ref _daRole, value); }
    public string ViRole { get => _viRole; set => SetProperty(ref _viRole, value); }

    // SP枚数
    public int VoSpCount { get => _voSpCount; set => SetProperty(ref _voSpCount, value); }
    public int DaSpCount { get => _daSpCount; set => SetProperty(ref _daSpCount, value); }
    public int ViSpCount { get => _viSpCount; set => SetProperty(ref _viSpCount, value); }

    public ICommand CalculateCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SelectPatternCommand { get; }
    public ICommand RecalcLessonCommand { get; }
    public ICommand CopyResultCommand { get; }
    public ICommand CopyDiagnosticCommand { get; }
    public ICommand CopyHifDiagnosticCommand { get; }

    public MainViewModel()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "Data");

        if (!Directory.Exists(dataDir))
        {
            var projectRoot = FindProjectRoot(baseDir);
            if (projectRoot != null)
                dataDir = Path.Combine(projectRoot, "Data");
        }

        var yamlService = new YamlDataService();
        _planLoader = new PlanLoaderService(yamlService, Path.Combine(dataDir, "Plans"));
        _cardLoader = new SupportCardLoaderService(yamlService, Path.Combine(dataDir, "SupportCards"));
        _inventoryService = new InventoryService(Path.Combine(dataDir, "Inventory", "inventory.yaml"));
        _characterLoader = new CharacterLoaderService(yamlService, Path.Combine(dataDir, "Characters"));
        _memoryPresetService = new MemoryPresetService(Path.Combine(dataDir, "MemoryPresets", "memory_presets.yaml"));
        _eventCountPresetService = new EventCountPresetService(Path.Combine(dataDir, "EventCountPresets", "event_count_presets.yaml"));
        _hifConditionPresetService = new HifConditionPresetService(Path.Combine(dataDir, "HifConditionPresets", "hif_condition_presets.yaml"));
        HifVm = new HifViewModel(
            new HifSchedulePresetService(Path.Combine(dataDir, "HifSchedulePresets", "hif_schedule_presets.yaml")),
            new HifBonusLevelsService(Path.Combine(dataDir, "HifBonusLevels", "hif_bonus_levels.yaml")));
        // 日程方式プラン (初レジェンド / NIA) のプリセットはプランごとに別ファイル
        _schedulePresetServices["hatsu_legend"] =
            new SchedulePresetService(Path.Combine(dataDir, "SchedulePresets", "hatsu_legend.yaml"));
        _schedulePresetServices["nia"] =
            new SchedulePresetService(Path.Combine(dataDir, "SchedulePresets", "nia.yaml"));
        _versionCheckService = new VersionCheckService();
        _calculationService = new StatusCalculationService();
        _scoringService = new CardScoringService();
        _uiStateService = new UiStateService(Path.Combine(dataDir, "UiState", "ui_state.yaml"));
        _calcSchedulePanelHeight = Math.Clamp(
            _uiStateService.GetPanelHeight(CalcSchedulePanelKey, SchedulePanelDefaultHeight),
            SchedulePanelMinHeight, SchedulePanelMaxHeight);
        _hifSchedulePanelHeight = Math.Clamp(
            _uiStateService.GetPanelHeight(HifSchedulePanelKey, SchedulePanelDefaultHeight),
            SchedulePanelMinHeight, SchedulePanelMaxHeight);

        LoadEventCountTemplates(yamlService, Path.Combine(dataDir, "Templates", "event_count_templates.yaml"));

        CalculateCommand = new RelayCommand(ExecuteCalculate);
        ResetCommand = new RelayCommand(ExecuteReset);
        AddRequiredCardCommand = new RelayCommand(ExecuteAddRequiredCard);
        RemoveRequiredCardCommand = new RelayCommand(ExecuteRemoveRequiredCard);
        AddExcludedCardCommand = new RelayCommand(ExecuteAddExcludedCard);
        RemoveExcludedCardCommand = new RelayCommand(ExecuteRemoveExcludedCard);
        ExcludeCardCommand = new RelayCommand(ExecuteExcludeCard);
        SelectPatternCommand = new RelayCommand(o =>
        {
            if (o is PatternResultViewModel pattern)
                SelectedPattern = pattern;
        });
        RecalcLessonCommand = new RelayCommand(ExecuteRecalcLesson);
        CopyResultCommand = new RelayCommand(ExecuteCopyResult);
        CopyDiagnosticCommand = new RelayCommand(ExecuteCopyDiagnostic);
        CopyHifDiagnosticCommand = new RelayCommand(_ => ExecuteCopyHifDiagnostic());
        SelectCharacterCommand = new RelayCommand(o =>
        {
            var target = o as Character;
            // 同じキャラを再度押した場合はトグルで解除
            SelectedCharacter = (target != null && target == _selectedCharacter) ? null : target;
        });

        // 持ち込みメモリースロット (4枠) を初期化。値変更時に再計算をトリガするコールバックを渡す。
        for (int i = 1; i <= 4; i++)
            MemoryBonuses.Add(new MemoryBonusViewModel(i, OnMemoryBonusChanged));

        ClearMemoryBonusesCommand = new RelayCommand(_ =>
        {
            foreach (var vm in MemoryBonuses)
                vm.Reset();
        });

        SaveMemoryPresetCommand = new RelayCommand(_ => ExecuteSaveMemoryPreset(),
            _ => CanSaveMemoryPreset());
        DeleteMemoryPresetCommand = new RelayCommand(_ => ExecuteDeleteMemoryPreset(),
            _ => _selectedMemoryPreset != null);

        SaveEventCountPresetCommand = new RelayCommand(_ => ExecuteSaveEventCountPreset(),
            _ => CanSaveEventCountPreset());
        DeleteEventCountPresetCommand = new RelayCommand(_ => ExecuteDeleteEventCountPreset(),
            _ => _selectedEventCountPreset != null);

        SaveHifConditionPresetCommand = new RelayCommand(_ => ExecuteSaveHifConditionPreset(),
            _ => CanSaveHifConditionPreset());
        DeleteHifConditionPresetCommand = new RelayCommand(_ => ExecuteDeleteHifConditionPreset(),
            _ => _selectedHifConditionPreset != null);

        CheckUpdateCommand = new RelayCommand(async _ => await CheckUpdateAsync(manual: true));
        OpenReleasePageCommand = new RelayCommand(_ => OpenReleasePage());
        DismissUpdateBannerCommand = new RelayCommand(_ =>
        {
            _isUpdateBannerDismissed = true;
            OnPropertyChanged(nameof(IsUpdateBannerVisible));
        });
        HifCalculateCommand = new RelayCommand(_ => ExecuteHifCalculate());
        ApplyScheduleBulkLessonCommand = new RelayCommand(_ => ExecuteApplyScheduleBulkLesson());
        ApplyScheduleBulkClassCommand = new RelayCommand(_ => ExecuteApplyScheduleBulkClass());
        SaveSchedulePresetCommand = new RelayCommand(_ => ExecuteSaveSchedulePreset());
        DeleteSchedulePresetCommand = new RelayCommand(_ => ExecuteDeleteSchedulePreset(),
            _ => _selectedSchedulePreset != null);

        CurrentVersion = VersionCheckService.GetCurrentVersion();

        LoadData();
        LoadMemoryPresets();
        LoadEventCountPresets();
        LoadHifConditionPresets();

        // 起動時に非同期で更新確認（失敗は静かに無視）
        _ = CheckUpdateAsync(manual: false);
    }

    private void LoadData()
    {
        try
        {
            var plans = _planLoader.LoadAllPlans();

            // HIFプランは専用タブで扱うため通常モードの選択肢から除外
            var hifPlan = plans.FirstOrDefault(p => p.Id == "hif");
            HifVm.HifPlan = hifPlan;

            AvailablePlans.Clear();
            foreach (var plan in plans.Where(p => p.Id != "hif"))
                AvailablePlans.Add(plan);

            _allCards = _cardLoader.LoadAllCards();
            _inventory = _inventoryService.Load();

            // キャラデータ読み込み（タイルビュー生成）
            CharacterTiles.Clear();
            foreach (var c in _characterLoader.LoadAll())
                CharacterTiles.Add(new CharacterTileViewModel(c));

            // 所持カードがあればデフォルトでチェックON
            OwnedOnly = _inventory.Any(e => e.Owned);

            if (AvailablePlans.Count > 0)
                SelectedPlan = AvailablePlans[0];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"データ読み込みエラー: {ex.Message}");
        }
    }

    // ===================== 日程方式 (初レジェンド / NIA) =====================

    private static string? FindProjectRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GakumasuCalc.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

public class PlanTypeOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public PlanTypeOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
