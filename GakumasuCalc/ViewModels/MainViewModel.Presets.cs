using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    private MemoryPreset? _selectedMemoryPreset;
    public MemoryPreset? SelectedMemoryPreset
    {
        get => _selectedMemoryPreset;
        set
        {
            if (SetProperty(ref _selectedMemoryPreset, value))
            {
                if (value != null)
                {
                    LoadMemoryPreset(value);
                    // 上書き保存しやすいよう、選択したプリセット名を保存欄に入れる
                    NewPresetName = value.Name;
                }
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

    // --- イベント回数プリセット ---
    public ObservableCollection<EventCountPreset> EventCountPresets { get; } = new();

    private EventCountPreset? _selectedEventCountPreset;
    public EventCountPreset? SelectedEventCountPreset
    {
        get => _selectedEventCountPreset;
        set
        {
            if (SetProperty(ref _selectedEventCountPreset, value))
            {
                if (value != null)
                {
                    LoadEventCountPreset(value);
                    // 上書き保存しやすいよう、選択したプリセット名を保存欄に入れる
                    NewEventCountPresetName = value.Name;
                }
            }
        }
    }

    private string _newEventCountPresetName = string.Empty;
    public string NewEventCountPresetName
    {
        get => _newEventCountPresetName;
        set => SetProperty(ref _newEventCountPresetName, value);
    }

    public int MaxEventCountPresets => EventCountPresetService.MaxPresets;
    public string EventCountPresetCountText => $"{EventCountPresets.Count}/{MaxEventCountPresets}";

    // --- HIF条件プリセット (HIFタブの入力条件一式) ---
    public ObservableCollection<HifConditionPreset> HifConditionPresets { get; } = new();

    private HifConditionPreset? _selectedHifConditionPreset;
    /// <summary>選択変更(セッター)による読込直後の DropDownClosed 再読込を1回だけ抑止するフラグ（計算の二重実行防止）。</summary>
    private bool _suppressNextHifConditionPresetReload;

    public HifConditionPreset? SelectedHifConditionPreset
    {
        get => _selectedHifConditionPreset;
        set
        {
            if (SetProperty(ref _selectedHifConditionPreset, value))
            {
                OnPropertyChanged(nameof(SelectedHifConditionPresetDisplay));
                if (value != null)
                {
                    LoadHifConditionPreset(value);
                    _suppressNextHifConditionPresetReload = true;
                    // 上書き保存しやすいよう、選択したプリセット名を保存欄に入れる
                    NewHifConditionPresetName = value.Name;
                }
            }
        }
    }

    /// <summary>Expanderヘッダ表示用。読込中の条件プリセット名（未選択なら空）。</summary>
    public string SelectedHifConditionPresetDisplay =>
        _selectedHifConditionPreset != null ? $": {_selectedHifConditionPreset.Name}" : "";

    private string _newHifConditionPresetName = string.Empty;
    public string NewHifConditionPresetName
    {
        get => _newHifConditionPresetName;
        set => SetProperty(ref _newHifConditionPresetName, value);
    }

    public int MaxHifConditionPresets => HifConditionPresetService.MaxPresets;
    public string HifConditionPresetCountText => $"{HifConditionPresets.Count}/{MaxHifConditionPresets}";

    public ICommand ClearMemoryBonusesCommand { get; private set; } = null!;
    public ICommand SaveMemoryPresetCommand { get; private set; } = null!;
    public ICommand DeleteMemoryPresetCommand { get; private set; } = null!;
    public ICommand SaveEventCountPresetCommand { get; private set; } = null!;
    public ICommand DeleteEventCountPresetCommand { get; private set; } = null!;
    public ICommand SaveHifConditionPresetCommand { get; private set; } = null!;
    public ICommand DeleteHifConditionPresetCommand { get; private set; } = null!;
    /// <summary>
    /// メモリースロットを計算用モデルのリストに変換。
    /// </summary>
    private List<MemoryBonus> BuildMemoryBonuses() =>
        MemoryBonuses.Select(vm => vm.ToModel()).ToList();

    /// <summary>プリセットファイルから読み込んで MemoryPresets コレクションに反映。</summary>
    private void LoadMemoryPresets()
    {
        PresetOps.LoadInto(MemoryPresets, _memoryPresetService.Load, "プリセット読み込みエラー",
            () => OnPropertyChanged(nameof(MemoryPresetCountText)));
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

    private bool CanSaveMemoryPreset() =>
        PresetOps.CanSave(MemoryPresets, _newPresetName, p => p.Name, MemoryPresetService.MaxPresets);

    private void ExecuteSaveMemoryPreset()
    {
        var name = _newPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = new MemoryPreset
        {
            Name = name,
            Bonuses = BuildMemoryBonuses(),
        };

        if (!PresetOps.Upsert(MemoryPresets, preset, name, p => p.Name, MemoryPresetService.MaxPresets)) return;
        if (!PresetOps.Persist(MemoryPresets, _memoryPresetService.Save,
                "メモリープリセット保存エラー", "プリセットの保存に失敗しました。", "保存失敗")) return;
        OnPropertyChanged(nameof(MemoryPresetCountText));
        SelectedMemoryPreset = preset;
        NewPresetName = string.Empty;
    }

    private void ExecuteDeleteMemoryPreset()
    {
        if (_selectedMemoryPreset == null) return;
        MemoryPresets.Remove(_selectedMemoryPreset);
        SelectedMemoryPreset = null;
        PresetOps.Persist(MemoryPresets, _memoryPresetService.Save,
            "メモリープリセット削除エラー", "プリセットの削除に失敗しました。", "削除失敗");
        OnPropertyChanged(nameof(MemoryPresetCountText));
    }

    /// <summary>プリセットファイルから読み込んで EventCountPresets コレクションに反映。</summary>
    private void LoadEventCountPresets()
    {
        PresetOps.LoadInto(EventCountPresets, _eventCountPresetService.Load, "イベント回数プリセット読み込みエラー",
            () => OnPropertyChanged(nameof(EventCountPresetCountText)));
    }

    /// <summary>
    /// 現在選択されているイベント回数プリセットを強制的に再読み込みする。
    /// ComboBox の DropDownClosed から呼ばれ、同じ項目を再選択した時にも値が反映されるようにする。
    /// </summary>
    public void ReloadSelectedEventCountPreset()
    {
        if (_selectedEventCountPreset != null)
            LoadEventCountPreset(_selectedEventCountPreset);
    }

    /// <summary>選択されたプリセットのイベント回数を現在の入力欄に反映する。</summary>
    private void LoadEventCountPreset(EventCountPreset preset)
    {
        ApplyCounts(preset.Counts);
    }

    private bool CanSaveEventCountPreset() =>
        PresetOps.CanSave(EventCountPresets, _newEventCountPresetName, p => p.Name, EventCountPresetService.MaxPresets);

    private void ExecuteSaveEventCountPreset()
    {
        var name = _newEventCountPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = new EventCountPreset
        {
            Name = name,
            Counts = BuildAdditionalCounts(),
        };

        if (!PresetOps.Upsert(EventCountPresets, preset, name, p => p.Name, EventCountPresetService.MaxPresets)) return;
        if (!PresetOps.Persist(EventCountPresets, _eventCountPresetService.Save,
                "イベント回数プリセット保存エラー", "プリセットの保存に失敗しました。", "保存失敗")) return;
        OnPropertyChanged(nameof(EventCountPresetCountText));
        SelectedEventCountPreset = preset;
        NewEventCountPresetName = string.Empty;
    }

    private void ExecuteDeleteEventCountPreset()
    {
        if (_selectedEventCountPreset == null) return;
        EventCountPresets.Remove(_selectedEventCountPreset);
        SelectedEventCountPreset = null;
        PresetOps.Persist(EventCountPresets, _eventCountPresetService.Save,
            "イベント回数プリセット削除エラー", "プリセットの削除に失敗しました。", "削除失敗");
        OnPropertyChanged(nameof(EventCountPresetCountText));
    }

    /// <summary>プリセットファイルから読み込んで HifConditionPresets コレクションに反映。</summary>
    private void LoadHifConditionPresets()
    {
        PresetOps.LoadInto(HifConditionPresets, _hifConditionPresetService.Load, "HIF条件プリセット読み込みエラー",
            () => OnPropertyChanged(nameof(HifConditionPresetCountText)));
    }

    /// <summary>
    /// 現在選択されている条件プリセットを強制的に再読み込みする。
    /// ComboBox の DropDownClosed から呼ばれ、同じ項目を再選択した時にも値が反映されるようにする。
    /// 選択変更直後の DropDownClosed は抑止フラグでスキップし、読込＋計算の二重実行を防ぐ。
    /// </summary>
    public void ReloadSelectedHifConditionPreset()
    {
        if (_suppressNextHifConditionPresetReload)
        {
            _suppressNextHifConditionPresetReload = false;
            return;
        }
        if (_selectedHifConditionPreset != null)
            LoadHifConditionPreset(_selectedHifConditionPreset);
    }

    /// <summary>
    /// 条件プリセットを HIFタブの入力条件一式へ復元し、そのまま計算を実行する。
    /// 凸トグル・HIFボーナスLv・MAX超過再抽選は対象外（別途永続化されるアカウント状態。
    /// 凸トグルは復元したキャラの永続設定から導出される）。
    /// </summary>
    private void LoadHifConditionPreset(HifConditionPreset preset)
    {
        // 1. 先に古い計算結果をクリア (Result=null によりメモリー入力変更の逐次再計算も無効化される)
        ClearResultState();
        HifVm.ErrorMessage = null;

        // 2. HIF側: スケジュール・試験配分・一括設定
        var hif = preset.Hif ?? new HifConditionHifFields();
        HifVm.ApplyScheduleState(hif.Choices ?? new(), hif.ExamAllocations ?? new());
        if (hif.ExamAllocations == null || hif.ExamAllocations.Count == 0)
        {
            // 旧スキーマ等で配分が無ければ、保存された比率から按分生成（比率が不正でも正規化される）
            HifVm.ApplyExamRatio(hif.ExamRatioVo, hif.ExamRatioDa, hif.ExamRatioVi);
        }
        if (IsValidStat(hif.BulkMainStat)) HifVm.BulkMainStat = hif.BulkMainStat;
        if (IsValidStat(hif.BulkSubStat) && hif.BulkSubStat != HifVm.BulkMainStat) HifVm.BulkSubStat = hif.BulkSubStat;
        if (IsValidStat(hif.BulkClassStat)) HifVm.BulkClassStat = hif.BulkClassStat;

        // 3. 共有側: 育成タイプ → テンプレ名 → イベント回数 → トグル（この順序が必須）
        var calc = preset.Calc ?? new HifConditionCalcFields();
        if (calc.SelectedPlanType is "sense" or "logic" or "anomaly")
            SelectedPlanType = calc.SelectedPlanType; // FilterEventCountTemplates が走りテンプレ選択が null 化される
        VoSpCount = Math.Max(0, calc.VoSpCount);
        DaSpCount = Math.Max(0, calc.DaSpCount);
        ViSpCount = Math.Max(0, calc.ViSpCount);
        // テンプレ名はバッキングフィールドへ直接復元（セッター経由だと ApplyEventTemplate が回数を上書きするため）
        _selectedHifEventTemplate = calc.SelectedTemplateName != null
            ? HifEventCountTemplates.FirstOrDefault(t => t.Name == calc.SelectedTemplateName)
            : null;
        OnPropertyChanged(nameof(SelectedHifEventTemplate));
        ApplyCounts(calc.Counts ?? new AdditionalCounts());
        OwnedOnly = calc.OwnedOnly;
        ContestMode = calc.ContestMode;

        // キャラ復元: null=解除、実在IDのみ採用（空文字=未保存(旧ファイル)や実在しないIDは現状維持）。
        // セッター経由なので凸トグルはキャラごとの永続設定から導出される
        if (calc.SelectedCharacterId != "")
        {
            var character = calc.SelectedCharacterId == null
                ? null
                : CharacterTiles.Select(t => t.Character).FirstOrDefault(c => c.Id == calc.SelectedCharacterId);
            if (calc.SelectedCharacterId == null || character != null)
                SelectedCharacter = character;
        }

        // 4. カード復元（実在IDのみ・必須は上限キャップ・必須と除外は相互排他）
        RequiredCards.Clear();
        ExcludedCards.Clear();
        foreach (var id in (calc.RequiredCardIds ?? new()).Distinct())
        {
            if (RequiredCards.Count >= MaxRequiredCards) break;
            var card = _allCards.FirstOrDefault(c => c.Id == id);
            if (card != null) RequiredCards.Add(card);
        }
        var requiredIdSet = RequiredCards.Select(c => c.Id).ToHashSet();
        foreach (var id in (calc.ExcludedCardIds ?? new()).Distinct())
        {
            if (requiredIdSet.Contains(id)) continue;
            var card = _allCards.FirstOrDefault(c => c.Id == id);
            if (card != null) ExcludedCards.Add(card);
        }
        OnPropertyChanged(nameof(CanAddRequiredCard));

        // 5. メモリー復元（4枠へ、不足は空）
        for (int i = 0; i < MemoryBonuses.Count; i++)
        {
            var src = calc.MemoryBonuses != null && i < calc.MemoryBonuses.Count
                ? calc.MemoryBonuses[i]
                : new MemoryBonus();
            var vm = MemoryBonuses[i];
            vm.VoValue = src.Vo.Value;
            vm.VoType = src.Vo.Type;
            vm.DaValue = src.Da.Value;
            vm.DaType = src.Da.Type;
            vm.ViValue = src.Vi.Value;
            vm.ViType = src.Vi.Type;
        }

        // 6. 読込と同時に計算実行（比較ワークフローを1クリックで回す）
        ExecuteHifCalculate();
    }

    private static bool IsValidStat(string? s) => s is "vo" or "da" or "vi";

    private bool CanSaveHifConditionPreset() =>
        PresetOps.CanSave(HifConditionPresets, _newHifConditionPresetName, p => p.Name, HifConditionPresetService.MaxPresets);

    private void ExecuteSaveHifConditionPreset()
    {
        var name = _newHifConditionPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var (choices, allocs) = HifVm.CaptureScheduleState();
        var preset = new HifConditionPreset
        {
            Name = name,
            Hif = new HifConditionHifFields
            {
                Choices = choices,
                ExamAllocations = allocs,
                ExamRatioVo = HifVm.ExamRatioVo,
                ExamRatioDa = HifVm.ExamRatioDa,
                ExamRatioVi = HifVm.ExamRatioVi,
                BulkMainStat = HifVm.BulkMainStat,
                BulkSubStat = HifVm.BulkSubStat,
                BulkClassStat = HifVm.BulkClassStat,
            },
            Calc = new HifConditionCalcFields
            {
                SelectedPlanType = SelectedPlanType,
                VoSpCount = VoSpCount,
                DaSpCount = DaSpCount,
                ViSpCount = ViSpCount,
                Counts = BuildAdditionalCounts(),
                SelectedTemplateName = SelectedHifEventTemplate?.Name,
                OwnedOnly = OwnedOnly,
                ContestMode = ContestMode,
                RequiredCardIds = RequiredCards.Select(c => c.Id).ToList(),
                ExcludedCardIds = ExcludedCards.Select(c => c.Id).ToList(),
                MemoryBonuses = BuildMemoryBonuses(),
                SelectedCharacterId = SelectedCharacter?.Id,
            },
        };

        if (!PresetOps.Upsert(HifConditionPresets, preset, name, p => p.Name, HifConditionPresetService.MaxPresets)) return;
        if (!PresetOps.Persist(HifConditionPresets, _hifConditionPresetService.Save,
                "HIF条件プリセット保存エラー", "プリセットの保存に失敗しました。", "保存失敗")) return;
        OnPropertyChanged(nameof(HifConditionPresetCountText));
        // セッター経由だと保存直後に読込（結果クリア）が走るため、バッキングフィールドへ直接反映
        _selectedHifConditionPreset = preset;
        OnPropertyChanged(nameof(SelectedHifConditionPreset));
        OnPropertyChanged(nameof(SelectedHifConditionPresetDisplay));
        NewHifConditionPresetName = string.Empty;
    }

    private void ExecuteDeleteHifConditionPreset()
    {
        if (_selectedHifConditionPreset == null) return;
        HifConditionPresets.Remove(_selectedHifConditionPreset);
        SelectedHifConditionPreset = null;
        PresetOps.Persist(HifConditionPresets, _hifConditionPresetService.Save,
            "HIF条件プリセット削除エラー", "プリセットの削除に失敗しました。", "削除失敗");
        OnPropertyChanged(nameof(HifConditionPresetCountText));
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

    // ----- プリセット -----
    public int MaxSchedulePresets => SchedulePresetService.MaxPresets;
    public string SchedulePresetCountText => $"{SchedulePresets.Count}/{MaxSchedulePresets}";
    public ObservableCollection<SchedulePreset> SchedulePresets { get; } = new();

    private SchedulePreset? _selectedSchedulePreset;
    public SchedulePreset? SelectedSchedulePreset
    {
        get => _selectedSchedulePreset;
        set
        {
            if (SetProperty(ref _selectedSchedulePreset, value))
            {
                if (value != null)
                {
                    LoadSchedulePresetIntoTurnChoices(value);
                    NewSchedulePresetName = value.Name;
                }
            }
        }
    }

    private string _newSchedulePresetName = string.Empty;
    public string NewSchedulePresetName
    {
        get => _newSchedulePresetName;
        set => SetProperty(ref _newSchedulePresetName, value);
    }

    public ICommand SaveSchedulePresetCommand { get; private set; } = null!;
    public ICommand DeleteSchedulePresetCommand { get; private set; } = null!;

    private SchedulePresetService? GetCurrentSchedulePresetService()
    {
        if (_selectedPlan == null) return null;
        return _schedulePresetServices.TryGetValue(_selectedPlan.Id, out var svc) ? svc : null;
    }

    private void LoadSchedulePresetsForCurrentPlan()
    {
        // 選択クリアはフィールド直接 (プロパティ経由だと再ロードが走るため)
        _selectedSchedulePreset = null;
        OnPropertyChanged(nameof(SelectedSchedulePreset));
        // タブ切替時に前プランのプリセット名入力を残さない (Web側 key=planId と同等)
        NewSchedulePresetName = string.Empty;
        SchedulePresets.Clear();
        var svc = GetCurrentSchedulePresetService();
        if (svc != null)
            PresetOps.LoadInto(SchedulePresets, svc.Load, "スケジュールプリセット読込エラー");
        OnPropertyChanged(nameof(SchedulePresetCountText));
    }

    private void LoadSchedulePresetIntoTurnChoices(SchedulePreset preset)
    {
        var byWeek = preset.Choices.ToDictionary(c => c.Week);
        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent) continue;
            if (byWeek.TryGetValue(tc.Week, out var c)
                && TurnChoiceViewModel.TryParseAction(c.Action, out var act)
                && tc.AvailableActions.Contains(act))
                tc.SelectedAction = act;
        }
        CacheScheduleSelections();
    }

    private void ExecuteSaveSchedulePreset()
    {
        var svc = GetCurrentSchedulePresetService();
        if (svc == null) return;
        var name = NewSchedulePresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var choices = new List<ScheduleChoiceEntry>();
        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent || tc.AvailableActions.Count == 0) continue;
            choices.Add(new ScheduleChoiceEntry { Week = tc.Week, Action = ActionToYaml(tc.SelectedAction) });
        }
        if (choices.Count == 0) return;

        var preset = new SchedulePreset { Name = name, Choices = choices };
        if (!PresetOps.Upsert(SchedulePresets, preset, name, p => p.Name, MaxSchedulePresets))
        {
            System.Windows.MessageBox.Show(
                $"プリセットは最大{MaxSchedulePresets}件まで保存できます。",
                "上限到達", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        PresetOps.PersistQuiet(SchedulePresets, svc.Save, "スケジュールプリセット保存エラー");
        OnPropertyChanged(nameof(SchedulePresetCountText));
        NewSchedulePresetName = string.Empty;
    }

    private void ExecuteDeleteSchedulePreset()
    {
        var svc = GetCurrentSchedulePresetService();
        if (svc == null || _selectedSchedulePreset == null) return;
        var target = SchedulePresets.FirstOrDefault(p => p.Name == _selectedSchedulePreset.Name);
        if (target == null) return;
        SchedulePresets.Remove(target);
        PresetOps.PersistQuiet(SchedulePresets, svc.Save, "スケジュールプリセット削除エラー");
        SelectedSchedulePreset = null;
        OnPropertyChanged(nameof(SchedulePresetCountText));
    }
}
