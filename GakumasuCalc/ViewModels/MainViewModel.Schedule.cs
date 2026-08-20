using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    // スケジュール個別調整パネルの高さ (下端のつまみドラッグで調整し、UiState に記憶)
    private const string CalcSchedulePanelKey = "calc_schedule";
    private const string HifSchedulePanelKey = "hif_schedule";
    private const double SchedulePanelDefaultHeight = 400;
    public const double SchedulePanelMinHeight = 150;
    public const double SchedulePanelMaxHeight = 1600;

    private double _calcSchedulePanelHeight = SchedulePanelDefaultHeight;
    /// <summary>日程方式タブの個別調整パネルの表示高さ</summary>
    public double CalcSchedulePanelHeight
    {
        get => _calcSchedulePanelHeight;
        set => SetProperty(ref _calcSchedulePanelHeight,
            Math.Clamp(value, SchedulePanelMinHeight, SchedulePanelMaxHeight));
    }

    private double _hifSchedulePanelHeight = SchedulePanelDefaultHeight;
    /// <summary>HIFタブの個別調整パネルの表示高さ</summary>
    public double HifSchedulePanelHeight
    {
        get => _hifSchedulePanelHeight;
        set => SetProperty(ref _hifSchedulePanelHeight,
            Math.Clamp(value, SchedulePanelMinHeight, SchedulePanelMaxHeight));
    }

    /// <summary>個別調整パネルの現在高さを保存する (つまみドラッグ確定時に呼ぶ)。</summary>
    public void PersistSchedulePanelHeights()
    {
        _uiStateService.SavePanelHeight(CalcSchedulePanelKey, CalcSchedulePanelHeight);
        _uiStateService.SavePanelHeight(HifSchedulePanelKey, HifSchedulePanelHeight);
    }

    /// <summary>現在の TurnChoices 選択を planId キャッシュへ保存 (タブ切替後に復元するため)。</summary>
    private void CacheScheduleSelections()
    {
        if (!IsExplicitSchedulePlan || _selectedPlan == null) return;
        var map = new Dictionary<int, ActionType>();
        foreach (var tc in TurnChoices)
            if (!tc.IsFixedEvent) map[tc.Week] = tc.SelectedAction;
        _scheduleSelectionCache[_selectedPlan.Id] = map;
    }

    // ----- 一括設定 -----
    public List<HifStatOption> ScheduleStatOptions { get; } = new()
    {
        new() { Value = "vo", Label = "Vocal" },
        new() { Value = "da", Label = "Dance" },
        new() { Value = "vi", Label = "Visual" },
    };

    /// <summary>一括レッスン属性（全レッスン週に適用する単一属性。メイン1/2の概念は廃止）。</summary>
    private string _scheduleBulkLessonStat = "vo";
    public string ScheduleBulkLessonStat
    {
        get => _scheduleBulkLessonStat;
        set => SetProperty(ref _scheduleBulkLessonStat, value);
    }

    private string _scheduleBulkClassStat = "vo";
    public string ScheduleBulkClassStat
    {
        get => _scheduleBulkClassStat;
        set => SetProperty(ref _scheduleBulkClassStat, value);
    }

    public ICommand ApplyScheduleBulkLessonCommand { get; private set; } = null!;
    public ICommand ApplyScheduleBulkClassCommand { get; private set; } = null!;

    private void ExecuteApplyScheduleBulkLesson()
    {
        if (_selectedPlan == null) return;
        // 全レッスン週に選択属性を適用（DistributeLessonsInto の単一メイン分岐＝全週その属性）
        DistributeLessonsInto(new List<string> { _scheduleBulkLessonStat });
        CacheScheduleSelections();
    }

    private void ExecuteApplyScheduleBulkClass()
    {
        if (!TurnChoiceViewModel.TryParseAction($"{_scheduleBulkClassStat}_class", out var targetAction)) return;
        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent || tc.AvailableActions.Count == 0) continue;
            // 授業を含む週 (休む等が混在する週もあるため Any 判定)
            bool hasClass = tc.AvailableActions.Any(a =>
                a is ActionType.VoClass or ActionType.DaClass or ActionType.ViClass);
            if (!hasClass) continue;
            if (tc.AvailableActions.Contains(targetAction))
                tc.SelectedAction = targetAction;
        }
        CacheScheduleSelections();
    }

    private static string ActionToYaml(ActionType a) => a switch
    {
        ActionType.VoLesson => "vo_lesson",
        ActionType.DaLesson => "da_lesson",
        ActionType.ViLesson => "vi_lesson",
        ActionType.VoClass => "vo_class",
        ActionType.DaClass => "da_class",
        ActionType.ViClass => "vi_class",
        ActionType.Outing => "outing",
        ActionType.Rest => "rest",
        ActionType.Consultation => "consultation",
        ActionType.ActivitySupply => "activity_supply",
        ActionType.SpecialTraining => "special_training",
        _ => "outing",
    };

    /// <summary>
    /// 日程方式の計算実行。ユーザ確定の TurnChoices を主入力にし、HIFスコアラー (turnChoicesOverride) で
    /// カードを選出する。HIF固有の試験配分/公開レッスン/上限パネル/overflow罰則は使わない。
    /// </summary>
    private void ExecuteScheduleCalculate()
    {
        if (_selectedPlan == null) return;

        var turnChoices = TurnChoices.Where(tc => !tc.IsFixedEvent).Select(tc => tc.ToTurnChoice()).ToList();
        if (turnChoices.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "スケジュールが未設定です。",
                "スケジュール未設定", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // 休むはプロデュース中4回まで (初レジェンド仕様)
        int restCount = turnChoices.Count(tc => tc.ChosenAction == ActionType.Rest);
        if (restCount > 4)
        {
            System.Windows.MessageBox.Show(
                $"休むはプロデュース中4回までです（現在 {restCount} 回）。",
                "休む回数超過", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        _isHifMode = false;
        _isScheduleMode = true;

        var mainStats = InferMainStatsFromTurnChoices(turnChoices);
        var lessonWeekCount = _selectedPlan.Schedule.Count(w => w.Lessons.Count > 0);

        var additional = BuildAdditionalCounts();
        var candidateCards = GetCandidateCards();
        var uncapLevels = BuildUncapLevels();

        List<SupportCard>? rentalPool = null;
        if (OwnedOnly)
        {
            rentalPool = ContestMode
                ? _allCards.Where(c => c.Tag is not ("skill" or "exam_item")).ToList()
                : _allCards;
        }

        // 除外カードを候補・レンタルプールから除去
        if (ExcludedCards.Count > 0)
        {
            var excludedIdSet = ExcludedCards.Select(c => c.Id).ToHashSet();
            candidateCards = candidateCards.Where(c => !excludedIdSet.Contains(c.Id)).ToList();
            if (rentalPool != null)
                rentalPool = rentalPool.Where(c => !excludedIdSet.Contains(c.Id)).ToList();
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
                    if (!candidateIdSet.Contains(card.Id)) candidateCards.Add(card);
                if (rentalPool != null)
                {
                    var rentalIdSet = rentalPool.Select(c => c.Id).ToHashSet();
                    foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                        if (!rentalIdSet.Contains(card.Id)) rentalPool.Add(card);
                }
            }
            else
            {
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                    if (!candidateIdSet.Contains(card.Id)) candidateCards.Add(card);
            }
        }

        var spCounts = new Dictionary<string, int>();
        if (VoSpCount > 0) spCounts["vo"] = VoSpCount;
        if (DaSpCount > 0) spCounts["da"] = DaSpCount;
        if (ViSpCount > 0) spCounts["vi"] = ViSpCount;

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

        _scheduleTurnChoices = turnChoices;
        _lastMainStats = mainStats;
        _lastLessonWeekCount = lessonWeekCount;

        var lessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            switch (tc.ChosenAction)
            {
                case ActionType.VoLesson: lessonAllocation["vo"]++; break;
                case ActionType.DaLesson: lessonAllocation["da"]++; break;
                case ActionType.ViLesson: lessonAllocation["vi"]++; break;
            }
        }

        var effectiveChar = GetEffectiveCharacter();
        var memoryBonuses = BuildMemoryBonuses();

        // NIA: キャラの審査基準・流行でオーディション獲得を付与した有効プラン（未選択/流行なしは素のプラン＝0）
        var effPlan = BuildNiaAuditionPlan(_selectedPlan);

        var patterns = _scoringService.SelectMultiplePatternsHif(
            effPlan, candidateCards, mainStats, lessonAllocation,
            spCounts: spCounts, planType: SelectedPlanType, additionalCounts: additional,
            uncapLevels: uncapLevels, rentalPool: rentalPool,
            requiredCardIds: requiredCardIds.Count > 0 ? requiredCardIds : null,
            character: effectiveChar, memoryBonuses: memoryBonuses,
            turnChoicesOverride: turnChoices,
            overflowPenalty: null);

        _deckResults = patterns;

        // 選出はキャラ補正込みのキャップ後合計で比較 (TotalValue はキャラ非考慮のため)
        var cap = _selectedPlan.StatusLimit;
        PatternResults.Clear();
        int bestIndex = 0;
        int bestTotal = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            var pCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
            var pUncap = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
                pUncap[cs.Card.Id] = 4;
            var pFs = _calculationService.Calculate(effPlan, pCards, turnChoices, pUncap, additional, effectiveChar, memoryBonuses).FinalStatus;
            int cappedTotal = Math.Min(pFs.Vo, cap) + Math.Min(pFs.Da, cap) + Math.Min(pFs.Vi, cap);

            var vm = new PatternResultViewModel { Label = pattern.Label, Index = i };
            foreach (var cs in pattern.SelectedCards)
            {
                var suffix = (cs.IsRental ? "（レンタル）" : "") + (cs.IsRequired ? "（必須）" : "");
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

            if (cappedTotal > bestTotal)
            {
                bestTotal = cappedTotal;
                bestIndex = i;
            }
        }

        OnPropertyChanged(nameof(PatternResults));

        if (PatternResults.Count > 0)
            SelectedPattern = PatternResults[bestIndex];
        else
            System.Windows.MessageBox.Show(
                "有効な編成パターンが見つかりませんでした。",
                "計算結果なし", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // ----- NIAオーディション獲得パラメータ -----
}
