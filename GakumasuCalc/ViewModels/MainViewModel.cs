using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly StatusCalculationService _calculationService;
    private readonly CardScoringService _scoringService;
    private readonly PlanLoaderService _planLoader;
    private readonly SupportCardLoaderService _cardLoader;
    private readonly InventoryService _inventoryService;
    private readonly CharacterLoaderService _characterLoader;
    private readonly MemoryPresetService _memoryPresetService;
    private readonly VersionCheckService _versionCheckService;
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

    public ObservableCollection<TrainingPlan> AvailablePlans { get; } = new();
    public ObservableCollection<TurnChoiceViewModel> TurnChoices { get; } = new();
    public ObservableCollection<DeckCardViewModel> DeckCards { get; } = new();
    public ObservableCollection<PatternResultViewModel> PatternResults { get; } = new();
    public ObservableCollection<CharacterTileViewModel> CharacterTiles { get; } = new();
    public ObservableCollection<MemoryBonusViewModel> MemoryBonuses { get; } = new();
    public ObservableCollection<MemoryPreset> MemoryPresets { get; } = new();

    /// <summary>
    /// HIFモード用 ViewModel。MainWindow の TabControl の HIF タブで使用。
    /// HIFプランは LoadData() 内で設定される。
    /// </summary>
    public HifViewModel HifVm { get; private set; } = null!;

    private MemoryPreset? _selectedMemoryPreset;
    public MemoryPreset? SelectedMemoryPreset
    {
        get => _selectedMemoryPreset;
        set
        {
            if (SetProperty(ref _selectedMemoryPreset, value))
            {
                if (value != null)
                    LoadMemoryPreset(value);
            }
        }
    }

    private string _newPresetName = string.Empty;
    public string NewPresetName
    {
        get => _newPresetName;
        set => SetProperty(ref _newPresetName, value);
    }

    public int MaxMemoryPresets => MemoryPresetService.MaxPresets;
    public string MemoryPresetCountText => $"{MemoryPresets.Count}/{MaxMemoryPresets}";

    public Character? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (SetProperty(ref _selectedCharacter, value))
            {
                OnPropertyChanged(nameof(HasSelectedCharacter));
                OnPropertyChanged(nameof(SelectedCharacterDisplay));
                OnPropertyChanged(nameof(CharacterBonusSummary));
                OnPropertyChanged(nameof(HasUncap3Bonus));
                foreach (var tile in CharacterTiles)
                    tile.IsSelected = (tile.Character == value);
                // 計算済みなら選択中パターンで再計算
                if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
                    ApplySelectedPattern(_selectedPattern.Index);
            }
        }
    }

    public bool HasSelectedCharacter => _selectedCharacter != null;

    public string SelectedCharacterDisplay =>
        _selectedCharacter != null ? $": {_selectedCharacter.Name}" : "";

    public string CharacterBonusSummary
    {
        get
        {
            if (_selectedCharacter == null) return string.Empty;
            var b = _selectedCharacter.BaseStatusBonus;
            var p = EffectiveParaBonus(_selectedCharacter);
            return $"基礎+{b.Vo}/{b.Da}/{b.Vi}  パラボ Vo+{p.Vo:0.#}% Da+{p.Da:0.#}% Vi+{p.Vi:0.#}%";
        }
    }

    /// <summary>
    /// 実効パラボを返す。para_bonus は3凸ON時の最大値で、OFFなら uncap3_bonus 分を減算する。
    /// </summary>
    private StatBonusPercent EffectiveParaBonus(Character c)
    {
        if (!_uncap3BonusEnabled && c.Uncap3Bonus != null)
            return c.ParaBonus.Subtract(c.Uncap3Bonus);
        return c.ParaBonus;
    }

    /// <summary>
    /// 計算で実際に渡すキャラ。3凸OFF時はパラボから3凸分を減算した一時オブジェクトを返す。
    /// </summary>
    private Character? GetEffectiveCharacter()
    {
        if (_selectedCharacter == null) return null;
        if (_uncap3BonusEnabled || _selectedCharacter.Uncap3Bonus == null)
            return _selectedCharacter;
        return new Character
        {
            Id = _selectedCharacter.Id,
            Name = _selectedCharacter.Name,
            Color = _selectedCharacter.Color,
            Initial = _selectedCharacter.Initial,
            BaseStatusBonus = _selectedCharacter.BaseStatusBonus,
            ParaBonus = _selectedCharacter.ParaBonus.Subtract(_selectedCharacter.Uncap3Bonus),
            Uncap3Bonus = _selectedCharacter.Uncap3Bonus,
        };
    }

    /// <summary>
    /// HIFボーナス (Vo/Da/Vi 上昇パネル) をキャラ補正に合算した Character を返す。
    /// デッキ選出と最終表示で同じキャラを使うために共通化。
    /// HIFボーナスが全て0かつキャラ未選択なら null を返す。
    /// </summary>
    private Character? GetHifEffectiveCharacter(out bool hasAnyHifBonus)
    {
        var baseChar = GetEffectiveCharacter();
        var bl = HifVm.BonusLevels;
        int bonusVoFlat = HifBonusTables.GetStatUpFlat(bl.VoUpLevel);
        int bonusDaFlat = HifBonusTables.GetStatUpFlat(bl.DaUpLevel);
        int bonusViFlat = HifBonusTables.GetStatUpFlat(bl.ViUpLevel);
        int bonusVoPara = HifBonusTables.GetStatUpPara(bl.VoUpLevel);
        int bonusDaPara = HifBonusTables.GetStatUpPara(bl.DaUpLevel);
        int bonusViPara = HifBonusTables.GetStatUpPara(bl.ViUpLevel);
        hasAnyHifBonus = bonusVoFlat > 0 || bonusDaFlat > 0 || bonusViFlat > 0
                       || bonusVoPara > 0 || bonusDaPara > 0 || bonusViPara > 0;
        if (!hasAnyHifBonus) return baseChar;

        return new Character
        {
            Id = baseChar?.Id ?? "__hif_bonus__",
            Name = baseChar?.Name ?? "HIF Bonus",
            Color = baseChar?.Color ?? "#000000",
            Initial = baseChar?.Initial ?? "",
            BaseStatusBonus = new StatusValues(
                (baseChar?.BaseStatusBonus.Vo ?? 0) + bonusVoFlat,
                (baseChar?.BaseStatusBonus.Da ?? 0) + bonusDaFlat,
                (baseChar?.BaseStatusBonus.Vi ?? 0) + bonusViFlat
            ),
            ParaBonus = new StatBonusPercent
            {
                Vo = (baseChar?.ParaBonus.Vo ?? 0) + bonusVoPara,
                Da = (baseChar?.ParaBonus.Da ?? 0) + bonusDaPara,
                Vi = (baseChar?.ParaBonus.Vi ?? 0) + bonusViPara,
            },
            Uncap3Bonus = baseChar?.Uncap3Bonus,
        };
    }

    private bool _uncap3BonusEnabled = false;
    public bool Uncap3BonusEnabled
    {
        get => _uncap3BonusEnabled;
        set
        {
            if (SetProperty(ref _uncap3BonusEnabled, value))
            {
                OnPropertyChanged(nameof(CharacterBonusSummary));
                if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
                    ApplySelectedPattern(_selectedPattern.Index);
            }
        }
    }

    public bool HasUncap3Bonus => _selectedCharacter?.Uncap3Bonus != null;

    public ICommand SelectCharacterCommand { get; }
    public ICommand ClearMemoryBonusesCommand { get; private set; } = null!;
    public ICommand SaveMemoryPresetCommand { get; private set; } = null!;
    public ICommand DeleteMemoryPresetCommand { get; private set; } = null!;
    public ICommand CheckUpdateCommand { get; private set; } = null!;
    public ICommand OpenReleasePageCommand { get; private set; } = null!;
    public ICommand DismissUpdateBannerCommand { get; private set; } = null!;
    public ICommand HifCalculateCommand { get; private set; } = null!;

    // --- バージョンチェック関連 ---
    public string CurrentVersion { get; }

    private string _latestVersion = string.Empty;
    public string LatestVersion
    {
        get => _latestVersion;
        private set { if (SetProperty(ref _latestVersion, value)) OnPropertyChanged(nameof(UpdateMessage)); }
    }

    private string _latestReleaseUrl = string.Empty;
    public string LatestReleaseUrl
    {
        get => _latestReleaseUrl;
        private set => SetProperty(ref _latestReleaseUrl, value);
    }

    private bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(IsUpdateBannerVisible));
                OnPropertyChanged(nameof(UpdateMessage));
            }
        }
    }

    private bool _isUpdateBannerDismissed;
    public bool IsUpdateBannerVisible => _hasUpdate && !_isUpdateBannerDismissed;

    private string _versionCheckStatus = string.Empty;
    public string VersionCheckStatus
    {
        get => _versionCheckStatus;
        private set => SetProperty(ref _versionCheckStatus, value);
    }

    public string UpdateMessage =>
        string.IsNullOrEmpty(_latestVersion)
            ? string.Empty
            : $"新しいバージョン v{_latestVersion} が公開されています";

    /// <summary>
    /// メモリースロットを計算用モデルのリストに変換。
    /// </summary>
    private List<MemoryBonus> BuildMemoryBonuses() =>
        MemoryBonuses.Select(vm => vm.ToModel()).ToList();

    /// <summary>プリセットファイルから読み込んで MemoryPresets コレクションに反映。</summary>
    private void LoadMemoryPresets()
    {
        try
        {
            var presets = _memoryPresetService.Load();
            MemoryPresets.Clear();
            foreach (var p in presets)
                MemoryPresets.Add(p);
            OnPropertyChanged(nameof(MemoryPresetCountText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"プリセット読み込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在選択されているプリセットを強制的に再読み込みする。
    /// ComboBox の DropDownClosed から呼ばれ、同じ項目を再選択した時にも値が反映されるようにする。
    /// </summary>
    public void ReloadSelectedMemoryPreset()
    {
        if (_selectedMemoryPreset != null)
            LoadMemoryPreset(_selectedMemoryPreset);
    }

    /// <summary>選択されたプリセットの値を 4 つの MemoryBonusViewModel に反映する。</summary>
    private void LoadMemoryPreset(MemoryPreset preset)
    {
        for (int i = 0; i < MemoryBonuses.Count; i++)
        {
            var src = i < preset.Bonuses.Count ? preset.Bonuses[i] : new MemoryBonus();
            var vm = MemoryBonuses[i];
            vm.VoValue = src.Vo.Value;
            vm.VoType = src.Vo.Type;
            vm.DaValue = src.Da.Value;
            vm.DaType = src.Da.Type;
            vm.ViValue = src.Vi.Value;
            vm.ViType = src.Vi.Type;
        }
    }

    private bool CanSaveMemoryPreset()
    {
        if (string.IsNullOrWhiteSpace(_newPresetName)) return false;
        // 同名は上書き扱い。同名が無くて上限超過なら不可
        var existing = MemoryPresets.FirstOrDefault(p => p.Name == _newPresetName.Trim());
        return existing != null || MemoryPresets.Count < MemoryPresetService.MaxPresets;
    }

    private void ExecuteSaveMemoryPreset()
    {
        var name = _newPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = new MemoryPreset
        {
            Name = name,
            Bonuses = BuildMemoryBonuses(),
        };

        var existing = MemoryPresets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            // 同名は上書き
            var idx = MemoryPresets.IndexOf(existing);
            MemoryPresets[idx] = preset;
        }
        else
        {
            if (MemoryPresets.Count >= MemoryPresetService.MaxPresets) return;
            MemoryPresets.Add(preset);
        }

        try
        {
            _memoryPresetService.Save(MemoryPresets.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"メモリープリセット保存エラー: {ex}");
            System.Windows.MessageBox.Show(
                $"プリセットの保存に失敗しました。\n\n{ex.Message}",
                "保存失敗", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        OnPropertyChanged(nameof(MemoryPresetCountText));
        SelectedMemoryPreset = preset;
        NewPresetName = string.Empty;
    }

    private void ExecuteDeleteMemoryPreset()
    {
        if (_selectedMemoryPreset == null) return;
        MemoryPresets.Remove(_selectedMemoryPreset);
        SelectedMemoryPreset = null;
        try
        {
            _memoryPresetService.Save(MemoryPresets.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"メモリープリセット削除エラー: {ex}");
            System.Windows.MessageBox.Show(
                $"プリセットの削除に失敗しました。\n\n{ex.Message}",
                "削除失敗", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        OnPropertyChanged(nameof(MemoryPresetCountText));
    }

    /// <summary>
    /// GitHub から最新リリースを取得して、現在バージョンと比較する。
    /// manual=true なら結果をステータステキストに反映（手動チェック用）。
    /// </summary>
    private async Task CheckUpdateAsync(bool manual)
    {
        if (manual)
            VersionCheckStatus = "確認中...";

        var latest = await _versionCheckService.GetLatestAsync();
        if (latest == null)
        {
            if (manual)
                VersionCheckStatus = "確認失敗（ネットワークまたは GitHub 側エラー）";
            return;
        }

        var isNewer = VersionCheckService.IsNewer(latest.NormalizedVersion, CurrentVersion);
        LatestVersion = latest.NormalizedVersion;
        LatestReleaseUrl = latest.HtmlUrl;
        HasUpdate = isNewer;
        // 手動チェックなら毎回バナーを再表示
        if (manual)
        {
            _isUpdateBannerDismissed = false;
            OnPropertyChanged(nameof(IsUpdateBannerVisible));
            VersionCheckStatus = isNewer
                ? $"v{latest.NormalizedVersion} が公開されています"
                : "最新版を利用中です";
        }
    }

    private void OpenReleasePage()
    {
        var url = string.IsNullOrEmpty(_latestReleaseUrl)
            ? "https://github.com/tyuukiti/gakumasu-calc/releases/latest"
            : _latestReleaseUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"リリースページ起動エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// メモリースロットに 0 以外の値が1つでも入っているか。
    /// </summary>
    private bool HasAnyMemoryBonus => MemoryBonuses.Any(m => !m.IsEmpty);

    /// <summary>
    /// メモリー入力変更時のハンドラ。既存のキャラ変更時パターンと同様に再計算をトリガする。
    /// </summary>
    private void OnMemoryBonusChanged()
    {
        if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
            ApplySelectedPattern(_selectedPattern.Index);
    }

    private PatternResultViewModel? _selectedPattern;
    public PatternResultViewModel? SelectedPattern
    {
        get => _selectedPattern;
        set
        {
            if (SetProperty(ref _selectedPattern, value) && value != null)
            {
                // 選択状態の更新
                foreach (var p in PatternResults)
                    p.IsSelected = (p == value);
                ApplySelectedPattern(value.Index);
            }
        }
    }

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

    // 必須カード
    public ObservableCollection<SupportCard> RequiredCards { get; } = new();
    public List<SupportCard> AvailableCardsForRequired => _allCards;

    public SupportCard? SelectedRequiredCard
    {
        get => _selectedRequiredCard;
        set => SetProperty(ref _selectedRequiredCard, value);
    }

    public bool CanAddRequiredCard => RequiredCards.Count < 4;

    public ICommand AddRequiredCardCommand { get; private set; } = null!;
    public ICommand RemoveRequiredCardCommand { get; private set; } = null!;

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

    // 追加カウントプロパティ
    public int PDrinkAcquire { get => _pDrinkAcquire; set => SetProperty(ref _pDrinkAcquire, value); }
    public int PItemAcquire { get => _pItemAcquire; set => SetProperty(ref _pItemAcquire, value); }
    public int SkillAcquire { get => _skillAcquire; set => SetProperty(ref _skillAcquire, value); }
    public int SkillSsrAcquire { get => _skillSsrAcquire; set => SetProperty(ref _skillSsrAcquire, value); }
    public int SkillEnhance { get => _skillEnhance; set => SetProperty(ref _skillEnhance, value); }
    public int SkillDelete { get => _skillDelete; set => SetProperty(ref _skillDelete, value); }
    public int SkillCustom { get => _skillCustom; set => SetProperty(ref _skillCustom, value); }
    public int SkillChange { get => _skillChange; set => SetProperty(ref _skillChange, value); }
    public int ActiveEnhance { get => _activeEnhance; set => SetProperty(ref _activeEnhance, value); }
    public int ActiveDelete { get => _activeDelete; set => SetProperty(ref _activeDelete, value); }
    public int MentalAcquire { get => _mentalAcquire; set => SetProperty(ref _mentalAcquire, value); }
    public int MentalEnhance { get => _mentalEnhance; set => SetProperty(ref _mentalEnhance, value); }
    public int MentalDelete { get => _mentalDelete; set => SetProperty(ref _mentalDelete, value); }
    public int ActiveAcquire { get => _activeAcquire; set => SetProperty(ref _activeAcquire, value); }
    public int GenkiAcquire { get => _genkiAcquire; set => SetProperty(ref _genkiAcquire, value); }
    public int GoodConditionAcquire { get => _goodConditionAcquire; set => SetProperty(ref _goodConditionAcquire, value); }
    public int GoodImpressionAcquire { get => _goodImpressionAcquire; set => SetProperty(ref _goodImpressionAcquire, value); }
    public int ConserveAcquire { get => _conserveAcquire; set => SetProperty(ref _conserveAcquire, value); }
    public int ConcentrateAcquire { get => _concentrateAcquire; set => SetProperty(ref _concentrateAcquire, value); }
    public int MotivationAcquire { get => _motivationAcquire; set => SetProperty(ref _motivationAcquire, value); }
    public int FullpowerAcquire { get => _fullpowerAcquire; set => SetProperty(ref _fullpowerAcquire, value); }
    public int AggressiveAcquire { get => _aggressiveAcquire; set => SetProperty(ref _aggressiveAcquire, value); }
    public int ConsultationDrink { get => _consultationDrink; set => SetProperty(ref _consultationDrink, value); }

    // イベント回数テンプレート
    private List<EventCountTemplate> _allEventCountTemplates = new();
    public ObservableCollection<EventCountTemplate> EventCountTemplates { get; } = new();
    /// <summary>HIFタブ用にplan_id="hif"でフィルタしたテンプレート (planType は共有)</summary>
    public ObservableCollection<EventCountTemplate> HifEventCountTemplates { get; } = new();

    private EventCountTemplate? _selectedHifEventTemplate;
    public EventCountTemplate? SelectedHifEventTemplate
    {
        get => _selectedHifEventTemplate;
        set
        {
            if (SetProperty(ref _selectedHifEventTemplate, value) && value != null)
            {
                ApplyEventTemplate(value);
            }
        }
    }

    private EventCountTemplate? _selectedEventTemplate;
    public EventCountTemplate? SelectedEventTemplate
    {
        get => _selectedEventTemplate;
        set
        {
            if (SetProperty(ref _selectedEventTemplate, value) && value != null)
            {
                ApplyEventTemplate(value);
                // 既に計算済みならターン選択を道中テンプレートで再適用
                if (_deckResults.Count > 0 && SelectedPattern != null)
                    ApplySelectedPattern(SelectedPattern.Index);
            }
        }
    }

    // 計算結果
    public CalculationResult? Result
    {
        get => _result;
        private set
        {
            SetProperty(ref _result, value);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ResultVoRaw));
            OnPropertyChanged(nameof(ResultDaRaw));
            OnPropertyChanged(nameof(ResultViRaw));
            OnPropertyChanged(nameof(ResultVo));
            OnPropertyChanged(nameof(ResultDa));
            OnPropertyChanged(nameof(ResultVi));
            OnPropertyChanged(nameof(ResultVoOverflow));
            OnPropertyChanged(nameof(ResultDaOverflow));
            OnPropertyChanged(nameof(ResultViOverflow));
            OnPropertyChanged(nameof(ResultVoOverflowText));
            OnPropertyChanged(nameof(ResultDaOverflowText));
            OnPropertyChanged(nameof(ResultViOverflowText));
            OnPropertyChanged(nameof(ResultTotal));
            OnPropertyChanged(nameof(ResultTotalUncapped));
            OnPropertyChanged(nameof(ResultTotalOverflow));
            OnPropertyChanged(nameof(ResultTotalOverflowText));
            OnPropertyChanged(nameof(VoBarColumn));
            OnPropertyChanged(nameof(DaBarColumn));
            OnPropertyChanged(nameof(ViBarColumn));
            OnPropertyChanged(nameof(VoBarColumnRemain));
            OnPropertyChanged(nameof(DaBarColumnRemain));
            OnPropertyChanged(nameof(ViBarColumnRemain));
            OnPropertyChanged(nameof(IsVoAtCap));
            OnPropertyChanged(nameof(IsDaAtCap));
            OnPropertyChanged(nameof(IsViAtCap));
            RaiseBasePropertyChanged();
        }
    }

    private void RaiseBasePropertyChanged()
    {
        OnPropertyChanged(nameof(ResultVoBaseRaw));
        OnPropertyChanged(nameof(ResultDaBaseRaw));
        OnPropertyChanged(nameof(ResultViBaseRaw));
        OnPropertyChanged(nameof(ResultVoBase));
        OnPropertyChanged(nameof(ResultDaBase));
        OnPropertyChanged(nameof(ResultViBase));
        OnPropertyChanged(nameof(ResultTotalBase));
        OnPropertyChanged(nameof(VoBarColumnBase));
        OnPropertyChanged(nameof(DaBarColumnBase));
        OnPropertyChanged(nameof(ViBarColumnBase));
        OnPropertyChanged(nameof(VoBarColumnBaseRemain));
        OnPropertyChanged(nameof(DaBarColumnBaseRemain));
        OnPropertyChanged(nameof(ViBarColumnBaseRemain));
        OnPropertyChanged(nameof(HasCharacterBonus));
        OnPropertyChanged(nameof(ResultVoDelta));
        OnPropertyChanged(nameof(ResultDaDelta));
        OnPropertyChanged(nameof(ResultViDelta));
        OnPropertyChanged(nameof(ResultVoDeltaText));
        OnPropertyChanged(nameof(ResultDaDeltaText));
        OnPropertyChanged(nameof(ResultViDeltaText));
        OnPropertyChanged(nameof(ResultTotalBaseText));
    }

    public bool HasResult => Result != null;
    // 属性ごと: 表示値は cap 適用後 (実ゲームの見え方と一致)。生数値は ResultVoRaw 等で別途公開。
    public int ResultVoRaw => Result?.FinalStatus.Vo ?? 0;
    public int ResultDaRaw => Result?.FinalStatus.Da ?? 0;
    public int ResultViRaw => Result?.FinalStatus.Vi ?? 0;
    public int ResultVo => Math.Min(ResultVoRaw, StatCap);
    public int ResultDa => Math.Min(ResultDaRaw, StatCap);
    public int ResultVi => Math.Min(ResultViRaw, StatCap);
    public int ResultVoOverflow => ResultVoRaw - ResultVo;
    public int ResultDaOverflow => ResultDaRaw - ResultDa;
    public int ResultViOverflow => ResultViRaw - ResultVi;
    public string ResultVoOverflowText => ResultVoOverflow > 0 ? $"元 {ResultVoRaw}" : string.Empty;
    public string ResultDaOverflowText => ResultDaOverflow > 0 ? $"元 {ResultDaRaw}" : string.Empty;
    public string ResultViOverflowText => ResultViOverflow > 0 ? $"元 {ResultViRaw}" : string.Empty;

    // 合計も cap 適用後で表示する (algorithm の選出基準と一致させる)。
    public int ResultTotal => ResultVo + ResultDa + ResultVi;
    public int ResultTotalUncapped => ResultVoRaw + ResultDaRaw + ResultViRaw;
    public int ResultTotalOverflow => ResultTotalUncapped - ResultTotal;
    public string ResultTotalOverflowText =>
        ResultTotalOverflow > 0 ? $"cap超過 −{ResultTotalOverflow}" : string.Empty;

    // キャラ補正を抜いた値（キャラ未選択時は通常結果と同値）
    private CalculationResult? ResultBase => _resultWithoutCharacter ?? _result;
    public int ResultVoBaseRaw => ResultBase?.FinalStatus.Vo ?? 0;
    public int ResultDaBaseRaw => ResultBase?.FinalStatus.Da ?? 0;
    public int ResultViBaseRaw => ResultBase?.FinalStatus.Vi ?? 0;
    public int ResultVoBase => Math.Min(ResultVoBaseRaw, StatCap);
    public int ResultDaBase => Math.Min(ResultDaBaseRaw, StatCap);
    public int ResultViBase => Math.Min(ResultViBaseRaw, StatCap);
    public int ResultTotalBase => ResultVoBase + ResultDaBase + ResultViBase;

    // キャラ補正・メモリー補正いずれかが有効なら true（差分バー/差分テキストの表示判定）
    public bool HasCharacterBonus => _resultWithoutCharacter != null;
    public int ResultVoDelta => ResultVo - ResultVoBase;
    public int ResultDaDelta => ResultDa - ResultDaBase;
    public int ResultViDelta => ResultVi - ResultViBase;
    public string ResultVoDeltaText => HasCharacterBonus && ResultVoDelta != 0 ? FormatDelta(ResultVoDelta) : string.Empty;
    public string ResultDaDeltaText => HasCharacterBonus && ResultDaDelta != 0 ? FormatDelta(ResultDaDelta) : string.Empty;
    public string ResultViDeltaText => HasCharacterBonus && ResultViDelta != 0 ? FormatDelta(ResultViDelta) : string.Empty;
    public string ResultTotalBaseText => HasCharacterBonus ? $"補正なし: {ResultTotalBase:#,0}" : string.Empty;
    private static string FormatDelta(int v) => v >= 0 ? $"+{v}" : v.ToString();

    // HIFモード時は本戦上限増加 (FinalStatLimit) を加算済みの dynamicPlan の上限を使う
    private int StatCap => _isHifMode && _hifDynamicPlan != null
        ? _hifDynamicPlan.StatusLimit
        : (_selectedPlan?.StatusLimit ?? 2800);
    public bool IsVoAtCap => ResultVo >= StatCap;
    public bool IsDaAtCap => ResultDa >= StatCap;
    public bool IsViAtCap => ResultVi >= StatCap;

    // バー幅は Grid.ColumnDefinitions の Star 比率で表現し、親要素いっぱいに伸びるようにする。
    // [ratio*][1-ratio*] の2列構成で 1列目にバーを描画。比率はプランの StatusLimit を分母にして算出。
    private double VoRatio => StatCap > 0 ? Math.Min((double)ResultVo / StatCap, 1.0) : 0;
    private double DaRatio => StatCap > 0 ? Math.Min((double)ResultDa / StatCap, 1.0) : 0;
    private double ViRatio => StatCap > 0 ? Math.Min((double)ResultVi / StatCap, 1.0) : 0;
    private double VoRatioBase => StatCap > 0 ? Math.Min((double)ResultVoBase / StatCap, 1.0) : 0;
    private double DaRatioBase => StatCap > 0 ? Math.Min((double)ResultDaBase / StatCap, 1.0) : 0;
    private double ViRatioBase => StatCap > 0 ? Math.Min((double)ResultViBase / StatCap, 1.0) : 0;

    public System.Windows.GridLength VoBarColumn => new(VoRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumn => new(DaRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumn => new(ViRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength VoBarColumnRemain => new(Math.Max(1.0 - VoRatio, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnRemain => new(Math.Max(1.0 - DaRatio, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnRemain => new(Math.Max(1.0 - ViRatio, 0), System.Windows.GridUnitType.Star);

    public System.Windows.GridLength VoBarColumnBase => new(VoRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnBase => new(DaRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnBase => new(ViRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength VoBarColumnBaseRemain => new(Math.Max(1.0 - VoRatioBase, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnBaseRemain => new(Math.Max(1.0 - DaRatioBase, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnBaseRemain => new(Math.Max(1.0 - ViRatioBase, 0), System.Windows.GridUnitType.Star);

    public string DeckLabel
    {
        get
        {
            if (DeckCards.Count == 0) return string.Empty;
            return DeckCards.FirstOrDefault()?.DeckLabel ?? string.Empty;
        }
    }

    public int DeckTotal => DeckCards.Sum(c => c.StatValue);

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
        HifVm = new HifViewModel(
            new HifSchedulePresetService(Path.Combine(dataDir, "HifSchedulePresets", "hif_schedule_presets.yaml")),
            new HifBonusLevelsService(Path.Combine(dataDir, "HifBonusLevels", "hif_bonus_levels.yaml")));
        _versionCheckService = new VersionCheckService();
        _calculationService = new StatusCalculationService();
        _scoringService = new CardScoringService();

        LoadEventCountTemplates(yamlService, Path.Combine(dataDir, "Templates", "event_count_templates.yaml"));

        CalculateCommand = new RelayCommand(ExecuteCalculate);
        ResetCommand = new RelayCommand(ExecuteReset);
        AddRequiredCardCommand = new RelayCommand(ExecuteAddRequiredCard);
        RemoveRequiredCardCommand = new RelayCommand(ExecuteRemoveRequiredCard);
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

        CheckUpdateCommand = new RelayCommand(async _ => await CheckUpdateAsync(manual: true));
        OpenReleasePageCommand = new RelayCommand(_ => OpenReleasePage());
        DismissUpdateBannerCommand = new RelayCommand(_ =>
        {
            _isUpdateBannerDismissed = true;
            OnPropertyChanged(nameof(IsUpdateBannerVisible));
        });
        HifCalculateCommand = new RelayCommand(_ => ExecuteHifCalculate());

        CurrentVersion = VersionCheckService.GetCurrentVersion();

        LoadData();
        LoadMemoryPresets();

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

    private void OnPlanChanged()
    {
        TurnChoices.Clear();
        if (_selectedPlan == null) return;

        foreach (var week in _selectedPlan.Schedule)
        {
            TurnChoices.Add(new TurnChoiceViewModel(week, _selectedPlan.ActivitySupply));
        }

        FilterEventCountTemplates();

        Result = null;
        DeckCards.Clear();
        OnPropertyChanged(nameof(DeckLabel));
        OnPropertyChanged(nameof(DeckTotal));
    }

    private void ExecuteCalculate()
    {
        if (_selectedPlan == null) return;

        _isHifMode = false;

        var lessonWeekCount = _selectedPlan.Schedule.Count(w => w.Lessons.Count > 0);

        // メイン属性リスト (メイン1が先、メイン1のレッスン回数が多い)
        var mainStats = new List<string>();
        if (VoRole == "メイン1") mainStats.Add("vo");
        if (DaRole == "メイン1") mainStats.Add("da");
        if (ViRole == "メイン1") mainStats.Add("vi");
        if (VoRole == "メイン2") mainStats.Add("vo");
        if (DaRole == "メイン2") mainStats.Add("da");
        if (ViRole == "メイン2") mainStats.Add("vi");

        // サブ属性を特定
        var subStat = new[] { "vo", "da", "vi" }.FirstOrDefault(s => !mainStats.Contains(s));
        if (subStat == null)
        {
            System.Windows.MessageBox.Show(
                "メイン1とメイン2に異なる属性を1つずつ設定してください。\nサブ属性が特定できません。",
                "属性設定エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // 追加カウント構築
        var additional = BuildAdditionalCounts();

        // 所持フィルタ適用
        var candidateCards = GetCandidateCards();
        var uncapLevels = BuildUncapLevels();

        // 所持モード時: 全カードをレンタルプールとして渡す（コンテストモード時はフィルタ適用）
        List<SupportCard>? rentalPool = null;
        if (OwnedOnly)
        {
            rentalPool = ContestMode
                ? _allCards.Where(c => c.Tag is not ("skill" or "exam_item")).ToList()
                : _allCards;
        }

        // 必須カード
        var requiredCardIds = RequiredCards.Select(c => c.Id).ToList();

        // 必須カードはコンテストモード等のフィルタを回避して候補に含める
        if (requiredCardIds.Count > 0)
        {
            var requiredIdSet = requiredCardIds.ToHashSet();
            var candidateIdSet = candidateCards.Select(c => c.Id).ToHashSet();

            if (OwnedOnly)
            {
                // 所持済み必須カードを candidateCards に追加
                var ownedIdSet = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id) && ownedIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }

                // 全必須カードを rentalPool に追加（未所持必須カードの検索用）
                if (rentalPool != null)
                {
                    var rentalIdSet = rentalPool.Select(c => c.Id).ToHashSet();
                    foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                    {
                        if (!rentalIdSet.Contains(card.Id))
                            rentalPool.Add(card);
                    }
                }
            }
            else
            {
                // 全カード4凸モード: 必須カードを candidateCards に追加
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }
            }
        }

        // SP率カード枚数
        var spCounts = new Dictionary<string, int>();
        if (VoSpCount > 0) spCounts["vo"] = VoSpCount;
        if (DaSpCount > 0) spCounts["da"] = DaSpCount;
        if (ViSpCount > 0) spCounts["vi"] = ViSpCount;

        // バリデーション: OwnedOnly時、未所持必須カードが2枚以上ならエラー
        if (OwnedOnly && requiredCardIds.Count > 0)
        {
            var ownedIds = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
            int notOwnedCount = requiredCardIds.Count(id => !ownedIds.Contains(id));
            if (notOwnedCount > 1)
            {
                System.Windows.MessageBox.Show(
                    "未所持の必須カードは最大1枚です（レンタル枠使用）。",
                    "必須カード設定エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        // 複数パターン一括計算
        var patterns = _scoringService.SelectMultiplePatterns(
            _selectedPlan, candidateCards, mainStats, subStat, lessonWeekCount,
            spCounts: spCounts, planType: SelectedPlanType, additionalCounts: additional,
            uncapLevels: uncapLevels, rentalPool: rentalPool,
            requiredCardIds: requiredCardIds.Count > 0 ? requiredCardIds : null);

        _deckResults = patterns;
        _lastMainStats = mainStats;
        _lastLessonWeekCount = lessonWeekCount;

        PatternResults.Clear();
        int bestIndex = 0;
        int bestTotal = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            var vm = new PatternResultViewModel { Label = pattern.Label, Index = i };
            foreach (var cs in pattern.SelectedCards)
            {
                var suffix = cs.IsRental ? "（レンタル）" : cs.IsRequired ? "（必須）" : "";
                var displayName = cs.Card.Name + suffix;
                var breakdown = string.Join("\n", cs.Breakdowns
                    .Select(b => b.Value == 0 ? $"  {b.Reason}" : $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
                vm.Cards.Add(new DeckCardViewModel
                {
                    CardName = displayName,
                    CardType = cs.Card.Type,
                    CardRarity = cs.Card.Rarity,
                    CardPlan = cs.Card.Plan,
                    StatValue = cs.TotalValue,
                    TeamBonusTotal = cs.TeamBonusTotal,
                    TeamBonusContributors = cs.TeamBonusContributors.Select(c => (c.CardName, c.Value)).ToList(),
                    Breakdowns = new ObservableCollection<EffectBreakdownViewModel>(
                        cs.Breakdowns.Select(b => new EffectBreakdownViewModel { Reason = b.Reason, Stat = b.Stat, Value = b.Value })),
                    RawVo = cs.RawVo,
                    RawDa = cs.RawDa,
                    RawVi = cs.RawVi,
                    DeckLabel = pattern.Label,
                    BreakdownText = $"Vo:{cs.RawVo} Da:{cs.RawDa} Vi:{cs.RawVi}\n{breakdown}",
                    HasSpRate = cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"),
                });
            }
            PatternResults.Add(vm);

            if (pattern.TotalValue > bestTotal)
            {
                bestTotal = pattern.TotalValue;
                bestIndex = i;
            }
        }

        OnPropertyChanged(nameof(PatternResults));

        // 最高スコアのパターンをデフォルト選択
        if (PatternResults.Count > 0)
            SelectedPattern = PatternResults[bestIndex];
    }

    /// <summary>
    /// 選択されたパターンで詳細計算を実行する
    /// </summary>
    private void ApplySelectedPattern(int patternIndex)
    {
        if (patternIndex < 0 || patternIndex >= _deckResults.Count)
            return;

        // HIFモード時は別経路で計算
        var planForCalc = _isHifMode ? _hifDynamicPlan : _selectedPlan;
        if (planForCalc == null) return;

        var pattern = _deckResults[patternIndex];

        List<TurnChoice> choices;
        if (_isHifMode)
        {
            // HIFはユーザが指定したスケジュール選択をそのまま使用
            choices = _hifTurnChoices;
        }
        else
        {
            // このパターンのレッスン配分を復元（通常モード）
            var allocation = BuildLessonAllocationFromPattern(pattern, _lastMainStats, _lastLessonWeekCount);
            AutoAssignTurnChoices(allocation, _lastMainStats, _selectedEventTemplate);
            choices = TurnChoices.Select(tc => tc.ToTurnChoice()).ToList();
        }

        var selectedCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
        var uncapLevels = BuildUncapLevels();
        // レンタルカードは4凸として計算
        foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
            uncapLevels[cs.Card.Id] = 4;
        // HIFモードではデッキ選出時と同じ「HIFボーナス込みキャラ」で再計算しないと
        // 選出が想定する補正値と表示値がズレてしまう
        bool hasAnyHifBonus = false;
        var effectiveChar = _isHifMode
            ? GetHifEffectiveCharacter(out hasAnyHifBonus)
            : GetEffectiveCharacter();
        var memoryBonuses = BuildMemoryBonuses();
        // キャラ補正・メモリー補正・HIFボーナスのいずれかが有効なら「補正なし結果」を別途算出し差分表示に使う
        _resultWithoutCharacter = (_selectedCharacter != null || HasAnyMemoryBonus || hasAnyHifBonus)
            ? _calculationService.Calculate(planForCalc, selectedCards, choices, uncapLevels, BuildAdditionalCounts(), null, null)
            : null;
        Result = _calculationService.Calculate(planForCalc, selectedCards, choices, uncapLevels, BuildAdditionalCounts(), effectiveChar, memoryBonuses);

        DeckCards.Clear();
        foreach (var cs in pattern.SelectedCards)
        {
            var suffix = cs.IsRental ? " (レンタル)" : cs.IsRequired ? " (必須)" : "";
            var displayName = cs.Card.Name + suffix;
            var breakdown = string.Join("\n", cs.Breakdowns
                .Select(b => b.Value == 0 ? $"  {b.Reason}" : $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
            DeckCards.Add(new DeckCardViewModel
            {
                CardName = displayName,
                CardType = cs.Card.Type,
                CardRarity = cs.Card.Rarity,
                CardPlan = cs.Card.Plan,
                StatValue = cs.TotalValue,
                TeamBonusTotal = cs.TeamBonusTotal,
                TeamBonusContributors = cs.TeamBonusContributors.Select(c => (c.CardName, c.Value)).ToList(),
                Breakdowns = new ObservableCollection<EffectBreakdownViewModel>(
                    cs.Breakdowns.Select(b => new EffectBreakdownViewModel { Reason = b.Reason, Stat = b.Stat, Value = b.Value })),
                RawVo = cs.RawVo,
                RawDa = cs.RawDa,
                RawVi = cs.RawVi,
                DeckLabel = pattern.Label,
                BreakdownText = $"Vo:{cs.RawVo} Da:{cs.RawDa} Vi:{cs.RawVi}\n{breakdown}",
                IsRental = cs.IsRental,
                IsRequired = cs.IsRequired,
                HasSpRate = cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"),
            });
        }
        OnPropertyChanged(nameof(DeckLabel));
        OnPropertyChanged(nameof(DeckTotal));
    }

    /// <summary>
    /// HIFモードの計算実行。HifVm.ScheduleItems の選択を元に動的な TrainingPlan を構築し、
    /// 既存の SelectMultiplePatterns / Calculate に渡す。
    /// </summary>
    private void ExecuteHifCalculate()
    {
        var hifPlan = HifVm.HifPlan;
        if (hifPlan == null)
        {
            HifVm.ErrorMessage = "HIFプランが読み込まれていません";
            return;
        }

        // 動的TrainingPlan + TurnChoices を構築
        var (dynamicPlan, turnChoices) = BuildHifPlanAndChoices(hifPlan);
        if (turnChoices.Count == 0)
        {
            HifVm.ErrorMessage = "スケジュールが未設定です";
            return;
        }

        // mainStats 自動推論 (出現数 desc 上位2属性、PostOptimize の保護対象計算に使う)
        var mainStats = InferMainStatsFromTurnChoices(turnChoices);

        var lessonWeekCount = dynamicPlan.Schedule.Count(w => w.Lessons.Count > 0);

        // 追加カウント
        var additional = BuildAdditionalCounts();

        // 候補カード
        var candidateCards = GetCandidateCards();
        var uncapLevels = BuildUncapLevels();

        List<SupportCard>? rentalPool = null;
        if (OwnedOnly)
        {
            rentalPool = ContestMode
                ? _allCards.Where(c => c.Tag is not ("skill" or "exam_item")).ToList()
                : _allCards;
        }

        var requiredCardIds = RequiredCards.Select(c => c.Id).ToList();
        if (requiredCardIds.Count > 0)
        {
            var requiredIdSet = requiredCardIds.ToHashSet();
            var candidateIdSet = candidateCards.Select(c => c.Id).ToHashSet();

            if (OwnedOnly)
            {
                var ownedIdSet = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id) && ownedIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }

                if (rentalPool != null)
                {
                    var rentalIdSet = rentalPool.Select(c => c.Id).ToHashSet();
                    foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                    {
                        if (!rentalIdSet.Contains(card.Id))
                            rentalPool.Add(card);
                    }
                }
            }
            else
            {
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }
            }
        }

        // SP率カード枚数
        var spCounts = new Dictionary<string, int>();
        if (VoSpCount > 0) spCounts["vo"] = VoSpCount;
        if (DaSpCount > 0) spCounts["da"] = DaSpCount;
        if (ViSpCount > 0) spCounts["vi"] = ViSpCount;

        // バリデーション: OwnedOnly時、未所持必須カードが2枚以上ならエラー
        if (OwnedOnly && requiredCardIds.Count > 0)
        {
            var ownedIds = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
            int notOwnedCount = requiredCardIds.Count(id => !ownedIds.Contains(id));
            if (notOwnedCount > 1)
            {
                HifVm.ErrorMessage = "未所持の必須カードは最大1枚です（レンタル枠使用）";
                return;
            }
        }

        // HIFモード状態を設定
        _isHifMode = true;
        _hifTurnChoices = turnChoices;
        _lastMainStats = mainStats;
        _lastLessonWeekCount = lessonWeekCount;

        // ユーザのスケジュールから実際のレッスン配分を集計
        var hifLessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            switch (tc.ChosenAction)
            {
                case ActionType.VoLesson: hifLessonAllocation["vo"]++; break;
                case ActionType.DaLesson: hifLessonAllocation["da"]++; break;
                case ActionType.ViLesson: hifLessonAllocation["vi"]++; break;
            }
        }

        // HIF専用パターン計算 (Vo×3 / Da×3 / Vi×3 / オールフリー)
        // キャラ補正・メモリーは PostOptimize の評価値と最終表示値を一致させるため渡す
        var hifMemoryBonuses = BuildMemoryBonuses();
        var hifEffectiveChar = GetHifEffectiveCharacter(out _);
        int hifFinalCapBonus = HifBonusTables.GetFinalCapBonus(HifVm.BonusLevels.FinalStatLimitLevel);

        // status_limit に本戦上限増加を加算 (hifCap は後段で dynamicPlan.StatusLimit から取得される)
        if (hifFinalCapBonus > 0)
        {
            dynamicPlan = new TrainingPlan
            {
                Id = dynamicPlan.Id,
                Name = dynamicPlan.Name,
                Description = dynamicPlan.Description,
                TotalWeeks = dynamicPlan.TotalWeeks,
                StatusLimit = dynamicPlan.StatusLimit + hifFinalCapBonus,
                BaseStatus = dynamicPlan.BaseStatus,
                Schedule = dynamicPlan.Schedule,
                ActivitySupply = dynamicPlan.ActivitySupply,
            };
        }
        // MAX判定はキャップボーナス込みの動的プランで行うため、ボーナス加算後に保持する
        _hifDynamicPlan = dynamicPlan;

        // MAX大幅超過時の再抽選オプション (ON のときだけ × 2 overflow罰則を有効化)
        var overflowPenaltyConfig = HifVm.OverflowPenaltyEnabled
            ? new CardScoringService.OverflowPenaltyConfig { Threshold = HifVm.OverflowPenaltyThreshold }
            : null;

        var patterns = _scoringService.SelectMultiplePatternsHif(
            dynamicPlan, candidateCards, mainStats, hifLessonAllocation,
            spCounts: spCounts, planType: SelectedPlanType, additionalCounts: additional,
            uncapLevels: uncapLevels, rentalPool: rentalPool,
            requiredCardIds: requiredCardIds.Count > 0 ? requiredCardIds : null,
            character: hifEffectiveChar, memoryBonuses: hifMemoryBonuses,
            turnChoicesOverride: turnChoices,
            overflowPenalty: overflowPenaltyConfig);

        _deckResults = patterns;

        // パターン選出はキャラ補正込みのキャップ後合計で比較
        // (TotalValue はキャラなしのカード寄与合計で、キャラの偏りを反映しないため)
        var hifCap = dynamicPlan.StatusLimit;
        PatternResults.Clear();
        int bestIndex = 0;
        int bestEffectiveTotal = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];

            // パターンの実効キャップ後合計を算出
            var pCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
            var pUncap = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
                pUncap[cs.Card.Id] = 4;
            var pFs = _calculationService.Calculate(dynamicPlan, pCards, turnChoices, pUncap, additional, hifEffectiveChar, hifMemoryBonuses).FinalStatus;
            int cappedTotal = Math.Min(pFs.Vo, hifCap) + Math.Min(pFs.Da, hifCap) + Math.Min(pFs.Vi, hifCap);
            // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則をパターン選択にも適用
            int effectiveScore = cappedTotal;
            if (overflowPenaltyConfig != null)
            {
                int pOverflow = Math.Max(0, pFs.Vo - hifCap) + Math.Max(0, pFs.Da - hifCap) + Math.Max(0, pFs.Vi - hifCap);
                if (pOverflow > overflowPenaltyConfig.Threshold)
                {
                    effectiveScore -= pOverflow * 2;
                }
            }

            var vm = new PatternResultViewModel { Label = pattern.Label, Index = i };
            foreach (var cs in pattern.SelectedCards)
            {
                var suffix = cs.IsRental ? "（レンタル）" : cs.IsRequired ? "（必須）" : "";
                var displayName = cs.Card.Name + suffix;
                var breakdown = string.Join("\n", cs.Breakdowns
                    .Select(b => b.Value == 0 ? $"  {b.Reason}" : $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
                vm.Cards.Add(new DeckCardViewModel
                {
                    CardName = displayName,
                    CardType = cs.Card.Type,
                    CardRarity = cs.Card.Rarity,
                    CardPlan = cs.Card.Plan,
                    StatValue = cs.TotalValue,
                    TeamBonusTotal = cs.TeamBonusTotal,
                    TeamBonusContributors = cs.TeamBonusContributors.Select(c => (c.CardName, c.Value)).ToList(),
                    Breakdowns = new ObservableCollection<EffectBreakdownViewModel>(
                        cs.Breakdowns.Select(b => new EffectBreakdownViewModel { Reason = b.Reason, Stat = b.Stat, Value = b.Value })),
                    RawVo = cs.RawVo,
                    RawDa = cs.RawDa,
                    RawVi = cs.RawVi,
                    DeckLabel = pattern.Label,
                    BreakdownText = $"Vo:{cs.RawVo} Da:{cs.RawDa} Vi:{cs.RawVi}\n{breakdown}",
                    HasSpRate = cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"),
                });
            }
            PatternResults.Add(vm);

            if (effectiveScore > bestEffectiveTotal)
            {
                bestEffectiveTotal = effectiveScore;
                bestIndex = i;
            }
        }

        OnPropertyChanged(nameof(PatternResults));

        HifVm.ErrorMessage = null;

        if (PatternResults.Count > 0)
            SelectedPattern = PatternResults[bestIndex];
        else
            HifVm.ErrorMessage = "有効な編成パターンが見つかりませんでした";
    }

    /// <summary>
    /// HifVm.ScheduleItems の選択を元に動的TrainingPlanとTurnChoice配列を構築。
    /// 公開レッスン日はユーザのメイン/サブ選択を sp_bonus に反映する。
    /// </summary>
    private (TrainingPlan plan, List<TurnChoice> turnChoices) BuildHifPlanAndChoices(TrainingPlan hifPlan)
    {
        // schedule を deep copy しつつ、公開レッスン日は sp_bonus を上書き
        var newSchedule = new List<WeekSchedule>();
        var itemsByWeek = HifVm.ScheduleItems.ToDictionary(it => it.Week);

        foreach (var w in hifPlan.Schedule)
        {
            // 試験日: 基礎値(全属性同値) + ユーザ配分値 を status_gain に反映
            if (w.IsFixedEvent && ((w.HifExamBase ?? 0) > 0 || (w.HifExamDistributed ?? 0) > 0))
            {
                if (itemsByWeek.TryGetValue(w.Week, out var examItem) && examItem.IsExam)
                {
                    int baseVal = examItem.ExamBase;
                    int voGain = baseVal + Math.Max(0, examItem.ExamVoAlloc);
                    int daGain = baseVal + Math.Max(0, examItem.ExamDaAlloc);
                    int viGain = baseVal + Math.Max(0, examItem.ExamViAlloc);
                    newSchedule.Add(new WeekSchedule
                    {
                        Week = w.Week,
                        Type = w.Type,
                        AvailableActions = w.AvailableActions,
                        Lessons = w.Lessons,
                        Classes = w.Classes,
                        EventName = w.EventName,
                        StatusGain = new StatusValues(voGain, daGain, viGain),
                        OutingEffect = w.OutingEffect,
                        ClassEffect = w.ClassEffect,
                        ConsultationEffect = w.ConsultationEffect,
                        SpecialTrainingEffect = w.SpecialTrainingEffect,
                        HifSubValue = w.HifSubValue,
                        HifExamBase = w.HifExamBase,
                        HifExamDistributed = w.HifExamDistributed,
                    });
                    continue;
                }
            }

            if (w.Type == "public_lesson" && itemsByWeek.TryGetValue(w.Week, out var item) && item.MainStat != null && item.SubStat != null)
            {
                var mainStat = item.MainStat;
                var subStat = item.SubStat;
                var mainValue = w.Lessons.FirstOrDefault(l => l.Type == mainStat) is { } ml
                    ? (mainStat switch { "vo" => ml.SpBonus.Vo, "da" => ml.SpBonus.Da, _ => ml.SpBonus.Vi })
                    : 0;
                var subValue = w.HifSubValue ?? 0;

                var newLessons = w.Lessons.Select(l =>
                {
                    if (l.Type != mainStat) return l;
                    int spVo = 0, spDa = 0, spVi = 0;
                    if (mainStat == "vo") spVo = mainValue;
                    else if (mainStat == "da") spDa = mainValue;
                    else if (mainStat == "vi") spVi = mainValue;

                    if (subStat == "vo") spVo += subValue;
                    else if (subStat == "da") spDa += subValue;
                    else if (subStat == "vi") spVi += subValue;

                    return new LessonConfig { Type = l.Type, SpBonus = new StatusValues(spVo, spDa, spVi) };
                }).ToList();

                newSchedule.Add(new WeekSchedule
                {
                    Week = w.Week,
                    Type = w.Type,
                    AvailableActions = w.AvailableActions,
                    Lessons = newLessons,
                    Classes = w.Classes,
                    EventName = w.EventName,
                    StatusGain = w.StatusGain,
                    OutingEffect = w.OutingEffect,
                    ClassEffect = w.ClassEffect,
                    ConsultationEffect = w.ConsultationEffect,
                    SpecialTrainingEffect = w.SpecialTrainingEffect,
                    HifSubValue = w.HifSubValue,
                });
            }
            else
            {
                newSchedule.Add(w);
            }
        }

        var newPlan = new TrainingPlan
        {
            Id = hifPlan.Id,
            Name = hifPlan.Name,
            Description = hifPlan.Description,
            TotalWeeks = hifPlan.TotalWeeks,
            StatusLimit = hifPlan.StatusLimit,
            BaseStatus = hifPlan.BaseStatus,
            Schedule = newSchedule,
            ActivitySupply = hifPlan.ActivitySupply,
        };

        // TurnChoices 構築 (固定日 audition はスキップ)
        var turnChoices = new List<TurnChoice>();
        foreach (var w in newSchedule)
        {
            if (w.IsFixedEvent) continue;
            if (!itemsByWeek.TryGetValue(w.Week, out var item)) continue;
            if (string.IsNullOrEmpty(item.SelectedAction)) continue;
            var actionType = ParseActionType(item.SelectedAction);
            if (actionType != null)
                turnChoices.Add(new TurnChoice { Week = w.Week, ChosenAction = actionType.Value });
        }

        return (newPlan, turnChoices);
    }

    /// <summary>
    /// TurnChoice 配列から mainStats を自動推論 (vo_lesson/da_lesson/vi_lesson の出現数 desc 上位2属性)。
    /// タイブレーク: vo > da > vi。
    /// </summary>
    private static List<string> InferMainStatsFromTurnChoices(List<TurnChoice> choices)
    {
        var counts = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in choices)
        {
            switch (tc.ChosenAction)
            {
                case ActionType.VoLesson: counts["vo"]++; break;
                case ActionType.DaLesson: counts["da"]++; break;
                case ActionType.ViLesson: counts["vi"]++; break;
            }
        }
        var order = new[] { "vo", "da", "vi" };
        return order
            .OrderByDescending(s => counts[s])
            .ThenBy(s => Array.IndexOf(order, s))
            .Take(2)
            .ToList();
    }

    private static ActionType? ParseActionType(string s) => s switch
    {
        "vo_lesson" => ActionType.VoLesson,
        "da_lesson" => ActionType.DaLesson,
        "vi_lesson" => ActionType.ViLesson,
        "vo_class" => ActionType.VoClass,
        "da_class" => ActionType.DaClass,
        "vi_class" => ActionType.ViClass,
        "outing" => ActionType.Outing,
        "rest" => ActionType.Rest,
        "consultation" => ActionType.Consultation,
        "activity_supply" => ActionType.ActivitySupply,
        "special_training" => ActionType.SpecialTraining,
        _ => null,
    };

    /// <summary>
    /// パターンのラベルからレッスン配分を復元する
    /// </summary>
    private Dictionary<string, int> BuildLessonAllocationFromPattern(
        CardScoringService.DeckResult pattern, List<string> mainStats, int totalLessonWeeks)
    {
        // パターンラベルからカード枚数を取得し、それをレッスン配分として使う
        // ラベル例: "Dance 3 / Visual 2 / Vocal 1 編成"
        var allocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };

        foreach (var part in pattern.Label.Replace(" 編成", "").Split(" / "))
        {
            var tokens = part.Trim().Split(' ');
            if (tokens.Length == 2 && int.TryParse(tokens[1], out int count))
            {
                var stat = tokens[0] switch
                {
                    "Vocal" => "vo",
                    "Dance" => "da",
                    "Visual" => "vi",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(stat))
                    allocation[stat] = count;
            }
        }

        // 残りをメインに配分
        int assigned = allocation.Values.Sum();
        int remaining = totalLessonWeeks - assigned;
        if (remaining > 0 && mainStats.Count > 0)
        {
            allocation[mainStats[0]] += remaining / 2;
            allocation[mainStats.Count > 1 ? mainStats[1] : mainStats[0]] += remaining - remaining / 2;
        }

        return allocation;
    }

    /// <summary>
    /// メイン/サブとSP枚数からレッスン配分を構築。
    /// SP枚数 = その属性のレッスンに割り当てる週数。
    /// 残りのレッスン週はメイン属性に均等配分。
    /// </summary>
    private Dictionary<string, int> BuildLessonAllocation(int totalLessonWeeks)
    {
        var allocation = new Dictionary<string, int>
        {
            ["vo"] = VoSpCount,
            ["da"] = DaSpCount,
            ["vi"] = ViSpCount
        };

        int assigned = allocation.Values.Sum();
        int remaining = totalLessonWeeks - assigned;

        // 残りをメイン属性に配分
        var mains = new List<string>();
        if (VoRole == "メイン") mains.Add("vo");
        if (DaRole == "メイン") mains.Add("da");
        if (ViRole == "メイン") mains.Add("vi");

        if (mains.Count > 0 && remaining > 0)
        {
            int perMain = remaining / mains.Count;
            int extra = remaining % mains.Count;
            foreach (var stat in mains)
            {
                allocation[stat] += perMain;
                if (extra > 0)
                {
                    allocation[stat]++;
                    extra--;
                }
            }
        }

        return allocation;
    }

    /// <summary>
    /// ターン選択を自動設定する。
    /// レッスン週: メイン属性のレッスンのみ選択（サブ属性のレッスンは選ばない）
    ///   中間前: メイン1:メイン2 = 1:1
    ///   中間後: メイン1:メイン2 = 1:2 (パラメータを早く伸ばすため)
    /// 授業週: サブ属性の授業を選択
    /// </summary>
    private void AutoAssignTurnChoices(Dictionary<string, int> allocation, List<string> mainStats, EventCountTemplate? template = null)
    {
        var subStat = new[] { "vo", "da", "vi" }.First(s => !mainStats.Contains(s));

        // サブの授業ActionType
        var subClassAction = subStat switch
        {
            "vo" => ActionType.VoClass,
            "da" => ActionType.DaClass,
            _ => ActionType.ViClass
        };

        if (mainStats.Count < 2)
        {
            // メインが1つだけの場合は全レッスンをそれに割り当て
            var onlyAction = mainStats[0] switch
            {
                "vo" => ActionType.VoLesson,
                "da" => ActionType.DaLesson,
                _ => ActionType.ViLesson
            };
            foreach (var tc in TurnChoices)
            {
                if (!tc.IsFixedEvent && tc.AvailableActions.Contains(onlyAction))
                    tc.SelectedAction = onlyAction;
            }
        }
        else
        {
            // メイン2属性のレッスンアクション
            var main1Action = mainStats[0] switch
            {
                "vo" => ActionType.VoLesson,
                "da" => ActionType.DaLesson,
                _ => ActionType.ViLesson
            };
            var main2Action = mainStats[1] switch
            {
                "vo" => ActionType.VoLesson,
                "da" => ActionType.DaLesson,
                _ => ActionType.ViLesson
            };

            // 中間試験の週を探す
            var midExamWeek = _selectedPlan?.Schedule
                .Where(w => w.IsFixedEvent && w.EventName == "中間試験")
                .Select(w => w.Week)
                .FirstOrDefault() ?? 10;

            // レッスン週を中間前後に分ける
            var lessonTurns = TurnChoices
                .Where(tc => !tc.IsFixedEvent && tc.AvailableActions.Any(a =>
                    a is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson))
                .OrderBy(tc => tc.Week)
                .ToList();

            var beforeMid = lessonTurns.Where(tc => tc.Week < midExamWeek).ToList();
            var afterMid = lessonTurns.Where(tc => tc.Week > midExamWeek).ToList();

            // 中間前: メイン1:メイン2 = 1:1 (交互に割り当て)
            bool toggle = false;
            foreach (var tc in beforeMid)
            {
                var action = toggle ? main2Action : main1Action;
                if (tc.AvailableActions.Contains(action))
                    tc.SelectedAction = action;
                else if (tc.AvailableActions.Contains(toggle ? main1Action : main2Action))
                    tc.SelectedAction = toggle ? main1Action : main2Action;
                toggle = !toggle;
            }

            // 中間後: メイン1:メイン2 = 2:1 (メイン1を多めに)
            // 3回中: メイン1, メイン2, メイン1 の順 (1回だけの属性を2回目に配置)
            int afterCount = 0;
            foreach (var tc in afterMid)
            {
                var action = (afterCount % 3 == 1) ? main2Action : main1Action;
                if (tc.AvailableActions.Contains(action))
                    tc.SelectedAction = action;
                else
                {
                    var fallback = (action == main2Action) ? main1Action : main2Action;
                    if (tc.AvailableActions.Contains(fallback))
                        tc.SelectedAction = fallback;
                }
                afterCount++;
            }
        }

        // 授業週: サブ属性の授業を選択
        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent) continue;

            var hasLesson = tc.AvailableActions.Any(a =>
                a is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson);
            var hasClass = tc.AvailableActions.Any(a =>
                a is ActionType.VoClass or ActionType.DaClass or ActionType.ViClass);

            if (hasLesson) continue; // レッスン週は上で設定済み

            // 道中テンプレートで週ごとのアクションが指定されていれば優先
            if (template?.WeekActions != null
                && template.WeekActions.TryGetValue(tc.Week, out var overrideStr)
                && TurnChoiceViewModel.TryParseAction(overrideStr, out var overrideAction)
                && tc.AvailableActions.Contains(overrideAction))
            {
                tc.SelectedAction = overrideAction;
                continue;
            }

            if (hasClass && tc.AvailableActions.Contains(subClassAction))
            {
                tc.SelectedAction = subClassAction;
            }
            else if (tc.AvailableActions.Contains(ActionType.ActivitySupply))
                tc.SelectedAction = ActionType.ActivitySupply;
            else if (tc.AvailableActions.Contains(ActionType.Outing))
                tc.SelectedAction = ActionType.Outing;
            else if (tc.AvailableActions.Contains(ActionType.Consultation))
                tc.SelectedAction = ActionType.Consultation;
            else if (tc.AvailableActions.Contains(ActionType.SpecialTraining))
                tc.SelectedAction = ActionType.SpecialTraining;
            else if (hasClass)
            {
                // サブの授業がない場合、メイン属性の授業を選択
                var mainClassAction = mainStats[0] switch
                {
                    "vo" => ActionType.VoClass,
                    "da" => ActionType.DaClass,
                    _ => ActionType.ViClass
                };
                if (tc.AvailableActions.Contains(mainClassAction))
                    tc.SelectedAction = mainClassAction;
                else if (tc.AvailableActions.Count > 0)
                    tc.SelectedAction = tc.AvailableActions[0];
            }
            else if (tc.AvailableActions.Count > 0)
                tc.SelectedAction = tc.AvailableActions[0];
        }
    }

    private AdditionalCounts BuildAdditionalCounts()
    {
        return new AdditionalCounts
        {
            PDrinkAcquire = PDrinkAcquire,
            PItemAcquire = PItemAcquire,
            SkillAcquire = SkillAcquire,
            SkillSsrAcquire = SkillSsrAcquire,
            SkillEnhance = SkillEnhance,
            SkillDelete = SkillDelete,
            SkillCustom = SkillCustom,
            SkillChange = SkillChange,
            ActiveEnhance = ActiveEnhance,
            ActiveDelete = ActiveDelete,
            MentalAcquire = MentalAcquire,
            MentalEnhance = MentalEnhance,
            MentalDelete = MentalDelete,
            ActiveAcquire = ActiveAcquire,
            GenkiAcquire = GenkiAcquire,
            GoodConditionAcquire = GoodConditionAcquire,
            GoodImpressionAcquire = GoodImpressionAcquire,
            ConserveAcquire = ConserveAcquire,
            ConcentrateAcquire = ConcentrateAcquire,
            MotivationAcquire = MotivationAcquire,
            FullpowerAcquire = FullpowerAcquire,
            AggressiveAcquire = AggressiveAcquire,
            ConsultationDrink = ConsultationDrink,
        };
    }

    private void ApplyEventTemplate(EventCountTemplate template)
    {
        var c = template.Counts;
        PDrinkAcquire = c.PDrinkAcquire;
        PItemAcquire = c.PItemAcquire;
        SkillAcquire = c.SkillAcquire;
        SkillSsrAcquire = c.SkillSsrAcquire;
        SkillEnhance = c.SkillEnhance;
        SkillDelete = c.SkillDelete;
        SkillCustom = c.SkillCustom;
        SkillChange = c.SkillChange;
        ActiveEnhance = c.ActiveEnhance;
        ActiveDelete = c.ActiveDelete;
        MentalAcquire = c.MentalAcquire;
        MentalEnhance = c.MentalEnhance;
        MentalDelete = c.MentalDelete;
        ActiveAcquire = c.ActiveAcquire;
        GenkiAcquire = c.GenkiAcquire;
        GoodConditionAcquire = c.GoodConditionAcquire;
        GoodImpressionAcquire = c.GoodImpressionAcquire;
        ConserveAcquire = c.ConserveAcquire;
        ConcentrateAcquire = c.ConcentrateAcquire;
        MotivationAcquire = c.MotivationAcquire;
        FullpowerAcquire = c.FullpowerAcquire;
        AggressiveAcquire = c.AggressiveAcquire;
        ConsultationDrink = c.ConsultationDrink;
    }

    private void LoadEventCountTemplates(YamlDataService yamlService, string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var file = yamlService.LoadFromFile<EventCountTemplateFile>(filePath);
                _allEventCountTemplates = file.Templates;
                FilterEventCountTemplates();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"テンプレート読み込みエラー: {ex.Message}");
        }
    }

    private void FilterEventCountTemplates()
    {
        var planId = _selectedPlan?.Id ?? string.Empty;
        var planTypeKeyword = _selectedPlanType switch
        {
            "sense" => "センス",
            "logic" => "ロジック",
            "anomaly" => "アノマリー",
            _ => null
        };

        EventCountTemplates.Clear();
        HifEventCountTemplates.Clear();
        foreach (var t in _allEventCountTemplates)
        {
            if (planTypeKeyword != null && !t.Name.Contains(planTypeKeyword)) continue;

            // 通常モード: 選択中プランで絞り込み (HIF プランのテンプレートは除外)
            if (!string.IsNullOrEmpty(t.PlanId) && t.PlanId == planId && t.PlanId != "hif")
                EventCountTemplates.Add(t);
            else if (string.IsNullOrEmpty(t.PlanId))
                EventCountTemplates.Add(t);

            // HIFタブ: plan_id="hif" のテンプレートのみ
            if (t.PlanId == "hif")
                HifEventCountTemplates.Add(t);
        }
        SelectedEventTemplate = null;
        SelectedHifEventTemplate = null;
    }

    /// <summary>
    /// サポカを変えずに現在のターン選択で再計算する
    /// </summary>
    private void ExecuteRecalcLesson()
    {
        if (_selectedPlan == null || _selectedPattern == null) return;
        if (_selectedPattern.Index < 0 || _selectedPattern.Index >= _deckResults.Count) return;

        var pattern = _deckResults[_selectedPattern.Index];
        var selectedCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();

        // 現在のターン選択をそのまま使って再計算
        var choices = TurnChoices.Select(tc => tc.ToTurnChoice()).ToList();
        var effectiveChar = GetEffectiveCharacter();
        var memoryBonuses = BuildMemoryBonuses();
        _resultWithoutCharacter = (_selectedCharacter != null || HasAnyMemoryBonus)
            ? _calculationService.Calculate(_selectedPlan, selectedCards, choices, BuildUncapLevels(), BuildAdditionalCounts(), null, null)
            : null;
        Result = _calculationService.Calculate(_selectedPlan, selectedCards, choices, BuildUncapLevels(), BuildAdditionalCounts(), effectiveChar, memoryBonuses);
    }

    /// <summary>
    /// 所持フィルタ・コンテストモードフィルタを適用したカードリストを返す
    /// </summary>
    private List<SupportCard> GetCandidateCards()
    {
        IEnumerable<SupportCard> cards = _allCards;

        if (OwnedOnly)
        {
            var ownedIds = _inventory
                .Where(e => e.Owned)
                .Select(e => e.CardId)
                .ToHashSet();
            cards = cards.Where(c => ownedIds.Contains(c.Id));
        }

        if (ContestMode)
        {
            cards = cards.Where(c => c.Tag is not ("skill" or "exam_item"));
        }

        return cards.ToList();
    }

    /// <summary>
    /// 凸数辞書を構築する。所持モード時はインベントリの凸数、それ以外は全カード4凸。
    /// </summary>
    private Dictionary<string, int> BuildUncapLevels()
    {
        if (OwnedOnly)
            return _inventory.ToDictionary(e => e.CardId, e => e.Uncap);

        // 全カード4凸
        return _allCards.ToDictionary(c => c.Id, _ => 4);
    }

    private void ExecuteAddRequiredCard()
    {
        if (SelectedRequiredCard == null || RequiredCards.Count >= 4) return;
        if (RequiredCards.Any(c => c.Id == SelectedRequiredCard.Id)) return;
        RequiredCards.Add(SelectedRequiredCard);
        SelectedRequiredCard = null;
        OnPropertyChanged(nameof(CanAddRequiredCard));
    }

    private void ExecuteRemoveRequiredCard(object? parameter)
    {
        if (parameter is SupportCard card)
        {
            RequiredCards.Remove(card);
            OnPropertyChanged(nameof(CanAddRequiredCard));
        }
    }

    private void ExecuteReset()
    {
        VoRole = "サブ"; DaRole = "サブ"; ViRole = "サブ";
        VoSpCount = 0; DaSpCount = 0; ViSpCount = 0;
        PDrinkAcquire = 0; PItemAcquire = 0; SkillAcquire = 0; SkillSsrAcquire = 0;
        SkillEnhance = 0; SkillDelete = 0; SkillCustom = 0; SkillChange = 0;
        ActiveEnhance = 0; ActiveDelete = 0;
        MentalAcquire = 0; MentalEnhance = 0; MentalDelete = 0; ActiveAcquire = 0;
        GenkiAcquire = 0; GoodConditionAcquire = 0;
        GoodImpressionAcquire = 0; ConserveAcquire = 0;
        ConcentrateAcquire = 0; MotivationAcquire = 0;
        FullpowerAcquire = 0; AggressiveAcquire = 0; ConsultationDrink = 0;
        DeckCards.Clear();
        RequiredCards.Clear();
        OnPropertyChanged(nameof(CanAddRequiredCard));
        OnPlanChanged();
    }

    private void ExecuteCopyResult()
    {
        if (Result == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Vo: {ResultVo:N0}  Da: {ResultDa:N0}  Vi: {ResultVi:N0}");
        sb.AppendLine($"合計: {ResultTotal:N0}");

        if (SelectedPattern != null)
        {
            sb.AppendLine();
            sb.AppendLine($"[{SelectedPattern.Label}]");
            foreach (var card in SelectedPattern.Cards)
            {
                var spMark = card.HasSpRate ? " [SP]" : "";
                sb.AppendLine($"  {card.CardTypeDisplay} {card.CardName}{spMark} ({card.StatValue:N0})");
            }
        }

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>
    /// 現在の設定・選択編成・計算結果を平文でクリップボードへコピーする（問題報告用）。
    /// Web版 services/diagnostics.ts の buildDiagnosticReport と同じ書式に揃える。
    /// </summary>
    private void ExecuteCopyDiagnostic()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 学マス計算ツール 診断情報 ===");

        // --- 設定 ---
        sb.AppendLine();
        sb.AppendLine("[設定]");
        sb.AppendLine(_selectedPlan != null
            ? $"プラン: {_selectedPlan.Name} ({_selectedPlan.Id})"
            : "プラン: (未選択)");
        sb.AppendLine($"プランタイプ: {PlanTypeLabel(SelectedPlanType)}");
        sb.AppendLine($"ロール: Vo={VoRole} / Da={DaRole} / Vi={ViRole}");
        sb.AppendLine($"SP回数: Vo={VoSpCount} / Da={DaSpCount} / Vi={ViSpCount}");
        sb.AppendLine($"所持カードのみ: {(OwnedOnly ? "ON" : "OFF")}");
        sb.AppendLine($"コンテストモード: {(ContestMode ? "ON" : "OFF")}");
        sb.AppendLine(_selectedCharacter != null
            ? $"キャラ: {_selectedCharacter.Name} / 3凸ボーナス: {(_uncap3BonusEnabled ? "ON" : "OFF")}"
            : "キャラ: (なし)");

        if (_selectedEventTemplate != null)
            sb.AppendLine($"テンプレート: {_selectedEventTemplate.Name}");

        if (RequiredCards.Count > 0)
            sb.AppendLine($"必須カード: {string.Join(", ", RequiredCards.Select(c => $"{c.Name} ({c.Id})"))}");

        AppendEventCounts(sb);
        AppendMemory(sb);
        AppendSelectedPattern(sb);
        AppendResult(sb);

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>
    /// HIFタブ用の診断情報をコピーする。結果・パターンは MainViewModel と共有しつつ、
    /// HIF固有 (ボーナスLv / overflow罰則 / スケジュール) を HifVm から追記する。
    /// Web版 buildHifDiagnosticReport と書式を揃える。
    /// </summary>
    private void ExecuteCopyHifDiagnostic()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 学マス計算ツール 診断情報 (HIF) ===");

        // --- 設定 ---
        sb.AppendLine();
        sb.AppendLine("[設定]");
        sb.AppendLine("モード: HIF (Hatsuboshi IDOL FESTIVAL)");
        sb.AppendLine($"プランタイプ: {PlanTypeLabel(SelectedPlanType)}");
        sb.AppendLine($"SP回数: Vo={VoSpCount} / Da={DaSpCount} / Vi={ViSpCount}");
        sb.AppendLine($"所持カードのみ: {(OwnedOnly ? "ON" : "OFF")}");
        sb.AppendLine($"コンテストモード: {(ContestMode ? "ON" : "OFF")}");
        sb.AppendLine(_selectedCharacter != null
            ? $"キャラ: {_selectedCharacter.Name} / 3凸ボーナス: {(_uncap3BonusEnabled ? "ON" : "OFF")}"
            : "キャラ: (なし)");

        if (_selectedHifEventTemplate != null)
            sb.AppendLine($"テンプレート: {_selectedHifEventTemplate.Name}");

        if (RequiredCards.Count > 0)
            sb.AppendLine($"必須カード: {string.Join(", ", RequiredCards.Select(c => $"{c.Name} ({c.Id})"))}");

        // --- HIFボーナス ---
        var bl = HifVm.BonusLevels;
        sb.AppendLine();
        sb.AppendLine("[HIFボーナス] (Lv)");
        sb.AppendLine($"Vo上昇={bl.VoUpLevel} / Da上昇={bl.DaUpLevel} / Vi上昇={bl.ViUpLevel}");
        sb.AppendLine($"本戦パラメータ上限増加={bl.FinalStatLimitLevel}");
        if (bl.OverflowPenaltyEnabled)
            sb.AppendLine($"MAX超過再抽選: ON (閾値 {bl.OverflowPenaltyThreshold})");

        // --- HIFスケジュール ---
        var schedLines = new List<string>();
        foreach (var item in HifVm.ScheduleItems)
        {
            if (item.IsPublicLesson && item.MainStat != null && item.SubStat != null)
            {
                schedLines.Add($"W{item.Week}: {item.MainStat.ToUpper()}レッスン（サブ{item.SubStat.ToUpper()}）");
            }
            else if (item.IsExam && (item.ExamVoAlloc > 0 || item.ExamDaAlloc > 0 || item.ExamViAlloc > 0))
            {
                schedLines.Add($"W{item.Week} 試験配分: Vo{item.ExamVoAlloc}/Da{item.ExamDaAlloc}/Vi{item.ExamViAlloc}");
            }
            else if (!item.IsFixed && item.SelectedAction != null)
            {
                schedLines.Add($"W{item.Week}: {HifActionLabel(item.SelectedAction)}");
            }
        }
        if (schedLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[HIFスケジュール]");
            foreach (var line in schedLines)
                sb.AppendLine(line);
        }

        AppendEventCounts(sb);
        AppendMemory(sb);
        AppendSelectedPattern(sb);
        AppendResult(sb);

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>イベントカウント (0以外のみ) を sb に追記。通常/HIF で共有。</summary>
    private void AppendEventCounts(System.Text.StringBuilder sb)
    {
        var counts = new (string Label, int Value)[]
        {
            ("Pドリンク獲得", PDrinkAcquire),
            ("Pアイテム獲得", PItemAcquire),
            ("スキルカード獲得", SkillAcquire),
            ("スキル(SSR)獲得", SkillSsrAcquire),
            ("スキル強化", SkillEnhance),
            ("スキル削除", SkillDelete),
            ("スキルカスタム", SkillCustom),
            ("スキルチェンジ", SkillChange),
            ("アクティブ強化", ActiveEnhance),
            ("アクティブ削除", ActiveDelete),
            ("メンタル獲得", MentalAcquire),
            ("メンタル強化", MentalEnhance),
            ("メンタル削除", MentalDelete),
            ("アクティブ獲得", ActiveAcquire),
            ("好調カード獲得", GoodConditionAcquire),
            ("集中カード獲得", ConcentrateAcquire),
            ("元気カード獲得", GenkiAcquire),
            ("好印象カード獲得", GoodImpressionAcquire),
            ("やる気カード獲得", MotivationAcquire),
            ("温存カード獲得", ConserveAcquire),
            ("全力カード獲得", FullpowerAcquire),
            ("強気カード獲得", AggressiveAcquire),
            ("相談Pドリンク交換", ConsultationDrink),
        };
        var activeCounts = counts.Where(c => c.Value > 0).ToList();
        if (activeCounts.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("[イベントカウント]");
        foreach (var (label, value) in activeCounts)
            sb.AppendLine($"{label}: {value}");
    }

    /// <summary>持ち込みメモリー (空でないもの) を sb に追記。</summary>
    private void AppendMemory(System.Text.StringBuilder sb)
    {
        var memoryLines = new List<string>();
        for (int i = 0; i < MemoryBonuses.Count; i++)
        {
            var m = MemoryBonuses[i];
            if (m.IsEmpty) continue;
            var parts = new List<string>();
            if (m.VoValue != 0) parts.Add($"Vo {FormatMemoryValue(m.VoValue, m.VoType)}");
            if (m.DaValue != 0) parts.Add($"Da {FormatMemoryValue(m.DaValue, m.DaType)}");
            if (m.ViValue != 0) parts.Add($"Vi {FormatMemoryValue(m.ViValue, m.ViType)}");
            if (parts.Count > 0)
                memoryLines.Add($"メモリー{i + 1}: {string.Join(" / ", parts)}");
        }
        if (memoryLines.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("[持ち込みメモリー]");
        foreach (var line in memoryLines)
            sb.AppendLine(line);
    }

    /// <summary>選択編成 (パターン名 + カード一覧) を sb に追記。</summary>
    private void AppendSelectedPattern(System.Text.StringBuilder sb)
    {
        if (_selectedPattern == null
            || _selectedPattern.Index < 0 || _selectedPattern.Index >= _deckResults.Count)
            return;
        var pattern = _deckResults[_selectedPattern.Index];
        sb.AppendLine();
        sb.AppendLine($"[選択編成] パターン: {pattern.Label}");
        int idx = 1;
        foreach (var cs in pattern.SelectedCards)
        {
            var tags = new List<string>();
            if (cs.IsRental) tags.Add("レンタル");
            if (cs.IsRequired) tags.Add("必須");
            var tagStr = tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "";
            sb.AppendLine($"{idx}. {cs.Card.Name} ({cs.Card.Id}){tagStr}");
            idx++;
        }
    }

    /// <summary>計算結果 (cap適用後 / 超過時はcap前も) を sb に追記。</summary>
    private void AppendResult(System.Text.StringBuilder sb)
    {
        if (Result == null)
        {
            sb.AppendLine();
            sb.AppendLine("[計算結果] 未計算");
            return;
        }
        sb.AppendLine();
        sb.AppendLine($"[計算結果] (上限 {StatCap})");
        sb.AppendLine($"Vo: {ResultVo}{(ResultVoOverflow > 0 ? $" (cap前 {ResultVoRaw})" : "")}");
        sb.AppendLine($"Da: {ResultDa}{(ResultDaOverflow > 0 ? $" (cap前 {ResultDaRaw})" : "")}");
        sb.AppendLine($"Vi: {ResultVi}{(ResultViOverflow > 0 ? $" (cap前 {ResultViRaw})" : "")}");
        sb.AppendLine($"合計: {ResultTotal}");
    }

    private static string PlanTypeLabel(string planType) => planType switch
    {
        "sense" => "センス",
        "logic" => "ロジック",
        "anomaly" => "アノマリー",
        _ => planType,
    };

    private static string HifActionLabel(string action) => action switch
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

    private static string FormatMemoryValue(double value, MemoryBonusType type)
    {
        var sign = value > 0 ? "+" : "";
        var suffix = type == MemoryBonusType.ParaBonus ? "%(パラボ)" : "";
        return $"{sign}{value:0.#}{suffix}";
    }

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

public class EffectBreakdownViewModel
{
    public string Reason { get; set; } = string.Empty;
    public string Stat { get; set; } = string.Empty;
    public double Value { get; set; }

    /// <summary>UI 表示: 0 のときは空 (ヘッダ行用)、そうでなければ「+80」「-3」</summary>
    public string ValueDisplay =>
        Value == 0 ? string.Empty : (Value > 0 ? $"+{Value:0.#}" : $"{Value:0.#}");

    /// <summary>属性カラー (vo=赤系 / da=青系 / vi=黄系 / all=灰)</summary>
    public System.Windows.Media.Brush StatColor => Stat switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x8A)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x9F, 0xFF)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xA3, 0x00)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
    };
}

public class DeckCardViewModel : ViewModelBase
{
    private bool _isExpanded;

    public string CardName { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string CardRarity { get; set; } = string.Empty;
    public string CardPlan { get; set; } = string.Empty;
    public int StatValue { get; set; }
    public int TeamBonusTotal { get; set; }
    public List<(string CardName, int Value)> TeamBonusContributors { get; set; } = new();
    public int RawVo { get; set; }
    public int RawDa { get; set; }
    public int RawVi { get; set; }

    /// <summary>クリック展開で表示する内訳行</summary>
    public ObservableCollection<EffectBreakdownViewModel> Breakdowns { get; set; } = new();

    /// <summary>クリックで展開・折りたたみ</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>クリック時に IsExpanded をトグル</summary>
    public System.Windows.Input.ICommand ToggleExpandCommand =>
        new RelayCommand(() => IsExpanded = !IsExpanded);

    /// <summary>UI 表示用: 「+71」または「+107 (+240)」形式 (詳細はクリック展開で見れる)</summary>
    public string StatValueDisplay =>
        TeamBonusTotal > 0
            ? $"+{StatValue} (+{TeamBonusTotal})"
            : $"+{StatValue}";
    public string DeckLabel { get; set; } = string.Empty;
    public string BreakdownText { get; set; } = string.Empty;
    public bool IsRental { get; set; }
    public bool IsRequired { get; set; }
    public bool HasSpRate { get; set; }

    public System.Windows.Visibility SpRateVisibility =>
        HasSpRate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string CardTypeDisplay => CardType switch
    {
        "vo" => "Vo",
        "da" => "Da",
        "vi" => "Vi",
        "all" => "All",
        _ => CardType
    };

    public string CardPlanDisplay => CardPlan switch
    {
        "sense" => "セ",
        "logic" => "ロ",
        "anomaly" => "ア",
        "free" => "フ",
        _ => ""
    };

    // 属性バッジの色
    public System.Windows.Media.Brush TypeBadgeForeground => CardType switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x8A)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x9F, 0xFF)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD3, 0x6B)),
        "all" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
    };

    public System.Windows.Media.Brush TypeBadgeBackground => CardType switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xEB, 0xEE)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF8, 0xE1)),
        "all" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xF5, 0xE9)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0)),
    };

    // カード名の色 (レンタル=オレンジ、必須=紫、通常=黒)
    public System.Windows.Media.Brush CardNameForeground =>
        IsRental ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0x58, 0x0C))
        : IsRequired ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
}

public class PatternResultViewModel : ViewModelBase
{
    private bool _isSelected;

    public string Label { get; set; } = string.Empty;
    public ObservableCollection<DeckCardViewModel> Cards { get; set; } = new();
    public int Total => Cards.Sum(c => c.StatValue);
    public int Index { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
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

public class EventCountTemplate
{
    public string Name { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public AdditionalCounts Counts { get; set; } = new();
    public Dictionary<int, string>? WeekActions { get; set; }

    public override string ToString() => Name;
}

public class EventCountTemplateFile
{
    public List<EventCountTemplate> Templates { get; set; } = new();
}
