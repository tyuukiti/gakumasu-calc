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
    private List<SupportCard> _allCards = new();
    private List<CardInventoryEntry> _inventory = new();

    private TrainingPlan? _selectedPlan;
    private CalculationResult? _result;
    private int _resultMaxValue = 1;

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
    private int _skillSsrAcquire;
    private int _skillEnhance;
    private int _skillDelete;
    private int _skillCustom;
    private int _skillChange;
    private int _activeEnhance;
    private int _activeDelete;
    private int _mentalAcquire;
    private int _mentalEnhance;
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

    public ObservableCollection<TrainingPlan> AvailablePlans { get; } = new();
    public ObservableCollection<TurnChoiceViewModel> TurnChoices { get; } = new();
    public ObservableCollection<DeckCardViewModel> DeckCards { get; } = new();
    public ObservableCollection<PatternResultViewModel> PatternResults { get; } = new();

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
    public int SkillSsrAcquire { get => _skillSsrAcquire; set => SetProperty(ref _skillSsrAcquire, value); }
    public int SkillEnhance { get => _skillEnhance; set => SetProperty(ref _skillEnhance, value); }
    public int SkillDelete { get => _skillDelete; set => SetProperty(ref _skillDelete, value); }
    public int SkillCustom { get => _skillCustom; set => SetProperty(ref _skillCustom, value); }
    public int SkillChange { get => _skillChange; set => SetProperty(ref _skillChange, value); }
    public int ActiveEnhance { get => _activeEnhance; set => SetProperty(ref _activeEnhance, value); }
    public int ActiveDelete { get => _activeDelete; set => SetProperty(ref _activeDelete, value); }
    public int MentalAcquire { get => _mentalAcquire; set => SetProperty(ref _mentalAcquire, value); }
    public int MentalEnhance { get => _mentalEnhance; set => SetProperty(ref _mentalEnhance, value); }
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
            OnPropertyChanged(nameof(ResultVo));
            OnPropertyChanged(nameof(ResultDa));
            OnPropertyChanged(nameof(ResultVi));
            OnPropertyChanged(nameof(ResultTotal));
            OnPropertyChanged(nameof(VoBarWidth));
            OnPropertyChanged(nameof(DaBarWidth));
            OnPropertyChanged(nameof(ViBarWidth));
            OnPropertyChanged(nameof(IsVoAtCap));
            OnPropertyChanged(nameof(IsDaAtCap));
            OnPropertyChanged(nameof(IsViAtCap));
        }
    }

    public bool HasResult => Result != null;
    public int ResultVo => Result?.FinalStatus.Vo ?? 0;
    public int ResultDa => Result?.FinalStatus.Da ?? 0;
    public int ResultVi => Result?.FinalStatus.Vi ?? 0;
    public int ResultTotal => Result?.FinalStatus.Total ?? 0;

    private int StatCap => _selectedPlan?.StatusLimit ?? 2800;
    public bool IsVoAtCap => ResultVo >= StatCap;
    public bool IsDaAtCap => ResultDa >= StatCap;
    public bool IsViAtCap => ResultVi >= StatCap;

    public int ResultMaxValue
    {
        get => _resultMaxValue;
        private set => SetProperty(ref _resultMaxValue, value);
    }

    public double VoBarWidth => ResultMaxValue > 0 ? (double)ResultVo / ResultMaxValue * 300 : 0;
    public double DaBarWidth => ResultMaxValue > 0 ? (double)ResultDa / ResultMaxValue * 300 : 0;
    public double ViBarWidth => ResultMaxValue > 0 ? (double)ResultVi / ResultMaxValue * 300 : 0;

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

        LoadData();
    }

    private void LoadData()
    {
        try
        {
            var plans = _planLoader.LoadAllPlans();
            AvailablePlans.Clear();
            foreach (var plan in plans)
                AvailablePlans.Add(plan);

            _allCards = _cardLoader.LoadAllCards();
            _inventory = _inventoryService.Load();

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
                    .Select(b => $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
                vm.Cards.Add(new DeckCardViewModel
                {
                    CardName = displayName,
                    CardType = cs.Card.Type,
                    CardRarity = cs.Card.Rarity,
                    CardPlan = cs.Card.Plan,
                    StatValue = cs.TotalValue,
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
        if (_selectedPlan == null || patternIndex < 0 || patternIndex >= _deckResults.Count)
            return;

        var pattern = _deckResults[patternIndex];

        // このパターンのレッスン配分を復元
        var allocation = BuildLessonAllocationFromPattern(pattern, _lastMainStats, _lastLessonWeekCount);
        AutoAssignTurnChoices(allocation, _lastMainStats, _selectedEventTemplate);

        var selectedCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
        var choices = TurnChoices.Select(tc => tc.ToTurnChoice()).ToList();
        var uncapLevels = BuildUncapLevels();
        // レンタルカードは4凸として計算
        foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
            uncapLevels[cs.Card.Id] = 4;
        Result = _calculationService.Calculate(_selectedPlan, selectedCards, choices, uncapLevels, BuildAdditionalCounts());

        DeckCards.Clear();
        foreach (var cs in pattern.SelectedCards)
        {
            var suffix = cs.IsRental ? " (レンタル)" : cs.IsRequired ? " (必須)" : "";
            var displayName = cs.Card.Name + suffix;
            var breakdown = string.Join("\n", cs.Breakdowns
                .Select(b => $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
            DeckCards.Add(new DeckCardViewModel
            {
                CardName = displayName,
                CardType = cs.Card.Type,
                CardRarity = cs.Card.Rarity,
                CardPlan = cs.Card.Plan,
                StatValue = cs.TotalValue,
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

        var maxStat = Math.Max(ResultVo, Math.Max(ResultDa, ResultVi));
        ResultMaxValue = maxStat > 0 ? maxStat : 1;
        OnPropertyChanged(nameof(VoBarWidth));
        OnPropertyChanged(nameof(DaBarWidth));
        OnPropertyChanged(nameof(ViBarWidth));
    }

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
            SkillSsrAcquire = SkillSsrAcquire,
            SkillEnhance = SkillEnhance,
            SkillDelete = SkillDelete,
            SkillCustom = SkillCustom,
            SkillChange = SkillChange,
            ActiveEnhance = ActiveEnhance,
            ActiveDelete = ActiveDelete,
            MentalAcquire = MentalAcquire,
            MentalEnhance = MentalEnhance,
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
        SkillSsrAcquire = c.SkillSsrAcquire;
        SkillEnhance = c.SkillEnhance;
        SkillDelete = c.SkillDelete;
        SkillCustom = c.SkillCustom;
        SkillChange = c.SkillChange;
        ActiveEnhance = c.ActiveEnhance;
        ActiveDelete = c.ActiveDelete;
        MentalAcquire = c.MentalAcquire;
        MentalEnhance = c.MentalEnhance;
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
        foreach (var t in _allEventCountTemplates)
        {
            if (!string.IsNullOrEmpty(t.PlanId) && t.PlanId != planId) continue;
            if (planTypeKeyword != null && !t.Name.Contains(planTypeKeyword)) continue;
            EventCountTemplates.Add(t);
        }
        SelectedEventTemplate = null;
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
        Result = _calculationService.Calculate(_selectedPlan, selectedCards, choices, BuildUncapLevels(), BuildAdditionalCounts());

        var maxStat = Math.Max(ResultVo, Math.Max(ResultDa, ResultVi));
        ResultMaxValue = maxStat > 0 ? maxStat : 1;
        OnPropertyChanged(nameof(VoBarWidth));
        OnPropertyChanged(nameof(DaBarWidth));
        OnPropertyChanged(nameof(ViBarWidth));
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
        PDrinkAcquire = 0; PItemAcquire = 0; SkillSsrAcquire = 0;
        SkillEnhance = 0; SkillDelete = 0; SkillCustom = 0; SkillChange = 0;
        ActiveEnhance = 0; ActiveDelete = 0;
        MentalAcquire = 0; MentalEnhance = 0; ActiveAcquire = 0;
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

public class DeckCardViewModel : ViewModelBase
{
    public string CardName { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string CardRarity { get; set; } = string.Empty;
    public string CardPlan { get; set; } = string.Empty;
    public int StatValue { get; set; }
    public int RawVo { get; set; }
    public int RawDa { get; set; }
    public int RawVi { get; set; }
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
