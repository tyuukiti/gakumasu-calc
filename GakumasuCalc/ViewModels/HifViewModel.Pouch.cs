using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

/// <summary>
/// HifViewModel: カスタムPアイテム (ポーチ) の選択とプレビュー。
/// 色 → ぬいぐるみ → デコレーション の従属選択 (親を変えると子はリセット) と、
/// 日程 (ScheduleItems) から段階ごとの発動回数を集計したプレビュー行・合計行を持つ。
/// 発動回数の計算本体は HifPouchService (Web版 hifPouch.ts と対)。
/// </summary>
public partial class HifViewModel
{
    /// <summary>ComboBox の「未選択」を表す Value</summary>
    public const string PouchNone = "";

    private HifPouchDefinition? _pouchDefinition;
    private string _pouchColor = PouchNone;
    private string _pouchPlush = PouchNone;
    private string _pouchDecoration = PouchNone;

    /// <summary>ポーチ定義 (Data/Hif/pouch.yaml)。MainViewModel から注入。null ならポーチ機能は非表示</summary>
    public HifPouchDefinition? PouchDefinition
    {
        get => _pouchDefinition;
        set
        {
            if (!SetProperty(ref _pouchDefinition, value)) return;
            // 定義が変わったら選択を整合させる
            SetPouchSelection(PouchColorId, PouchPlushId, PouchDecorationId);
            OnPropertyChanged(nameof(IsPouchAvailable));
            OnPropertyChanged(nameof(PouchSectionVisibility));
            OnPropertyChanged(nameof(PouchColorOptions));
        }
    }

    public bool IsPouchAvailable => _pouchDefinition != null && _pouchDefinition.Colors.Count > 0;
    public Visibility PouchSectionVisibility => IsPouchAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>色の選択 (ComboBox 用。未選択は PouchNone)。変更するとぬいぐるみ・デコレーションはリセット</summary>
    public string PouchColor
    {
        get => _pouchColor;
        set
        {
            var v = value ?? PouchNone;
            if (v == _pouchColor) return;
            SetPouchSelection(string.IsNullOrEmpty(v) ? null : v, null, null);
        }
    }

    /// <summary>ぬいぐるみの選択。変更するとデコレーションはリセット</summary>
    public string PouchPlush
    {
        get => _pouchPlush;
        set
        {
            var v = value ?? PouchNone;
            if (v == _pouchPlush) return;
            SetPouchSelection(PouchColorId, string.IsNullOrEmpty(v) ? null : v, null);
        }
    }

    public string PouchDecoration
    {
        get => _pouchDecoration;
        set
        {
            var v = value ?? PouchNone;
            if (v == _pouchDecoration) return;
            SetPouchSelection(PouchColorId, PouchPlushId, string.IsNullOrEmpty(v) ? null : v);
        }
    }

    /// <summary>未選択を null にした id (計算・プリセット・診断用)</summary>
    public string? PouchColorId => string.IsNullOrEmpty(_pouchColor) ? null : _pouchColor;
    public string? PouchPlushId => string.IsNullOrEmpty(_pouchPlush) ? null : _pouchPlush;
    public string? PouchDecorationId => string.IsNullOrEmpty(_pouchDecoration) ? null : _pouchDecoration;

    /// <summary>
    /// 選択を一括設定する (プリセット・定番ボタン・共有)。定義と突き合わせて整合させ (定義に無い id は未選択、
    /// 親が未選択なら子も未選択)、プレビューを更新する。
    /// </summary>
    public void SetPouchSelection(string? color, string? plush, string? deco)
    {
        var (c, p, d) = HifPouchService.Sanitize(_pouchDefinition, color, plush, deco);
        _pouchColor = c ?? PouchNone;
        _pouchPlush = p ?? PouchNone;
        _pouchDecoration = d ?? PouchNone;
        OnPropertyChanged(nameof(PouchColor));
        OnPropertyChanged(nameof(PouchPlush));
        OnPropertyChanged(nameof(PouchDecoration));
        OnPropertyChanged(nameof(AvailablePouchPlushes));
        OnPropertyChanged(nameof(AvailablePouchDecorations));
        OnPropertyChanged(nameof(PouchHeaderText));
        RefreshPouchPreview();
    }

    private static readonly HifActionOption PouchNoneOption = new() { Value = PouchNone, Label = "未選択" };

    public List<HifActionOption> PouchColorOptions
    {
        get
        {
            var list = new List<HifActionOption> { PouchNoneOption };
            if (_pouchDefinition != null)
                list.AddRange(_pouchDefinition.Colors.Select(c => new HifActionOption { Value = c.Id, Label = c.Name }));
            return list;
        }
    }

    /// <summary>選択中の色に属するぬいぐるみ候補 (色が未選択なら「未選択」のみ)</summary>
    public List<HifActionOption> AvailablePouchPlushes
    {
        get
        {
            var list = new List<HifActionOption> { PouchNoneOption };
            var color = _pouchDefinition?.Colors.FirstOrDefault(c => c.Id == PouchColorId);
            if (color != null)
                list.AddRange(color.Plushes.Select(p => new HifActionOption { Value = p.Id, Label = p.Name }));
            return list;
        }
    }

    /// <summary>選択中のぬいぐるみに属するデコレーション候補</summary>
    public List<HifActionOption> AvailablePouchDecorations
    {
        get
        {
            var list = new List<HifActionOption> { PouchNoneOption };
            var color = _pouchDefinition?.Colors.FirstOrDefault(c => c.Id == PouchColorId);
            var plush = color?.Plushes.FirstOrDefault(p => p.Id == PouchPlushId);
            if (plush != null)
                list.AddRange(plush.Decorations.Select(d => new HifActionOption { Value = d.Id, Label = d.Name }));
            return list;
        }
    }

    // ===== プリセット (他セクションと同じ 保存・読込・削除。初回は Wiki の 3 ルートを投入) =====

    private HifPouchPresetService? _pouchPresetService;

    public ObservableCollection<HifPouchPreset> PouchPresets { get; } = new();
    public int MaxPouchPresets => HifPouchPresetService.MaxPresets;
    public string PouchPresetCountText => $"{PouchPresets.Count}/{MaxPouchPresets}";

    private HifPouchPreset? _selectedPouchPreset;
    public HifPouchPreset? SelectedPouchPreset
    {
        get => _selectedPouchPreset;
        set
        {
            if (SetProperty(ref _selectedPouchPreset, value) && value != null)
            {
                SetPouchSelection(value.Color, value.Plush, value.Decoration);
                // 上書き保存しやすいよう、選択したプリセット名を保存欄に入れる
                NewPouchPresetName = value.Name;
            }
        }
    }

    private string _newPouchPresetName = string.Empty;
    public string NewPouchPresetName
    {
        get => _newPouchPresetName;
        set => SetProperty(ref _newPouchPresetName, value);
    }

    private ICommand? _savePouchPresetCommand;
    public ICommand SavePouchPresetCommand => _savePouchPresetCommand ??= new RelayCommand(
        _ => ExecuteSavePouchPreset(),
        _ => PresetOps.CanSave(PouchPresets, _newPouchPresetName, p => p.Name, MaxPouchPresets));

    private ICommand? _deletePouchPresetCommand;
    public ICommand DeletePouchPresetCommand => _deletePouchPresetCommand ??= new RelayCommand(
        _ => ExecuteDeletePouchPreset(),
        _ => _selectedPouchPreset != null);

    /// <summary>保存済みプリセットを読み込む。ファイルが無ければ (サービス無しも含め) 初期プリセットを使う</summary>
    private void LoadPouchPresets()
    {
        List<HifPouchPreset>? loaded = null;
        try { loaded = _pouchPresetService?.Load(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ポーチプリセット読込エラー: {ex.Message}"); }
        PouchPresets.Clear();
        foreach (var p in loaded ?? HifPouchPresetService.DefaultPresets())
            PouchPresets.Add(p);
        OnPropertyChanged(nameof(PouchPresetCountText));
    }

    private void ExecuteSavePouchPreset()
    {
        var name = _newPouchPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var preset = new HifPouchPreset { Name = name, Color = PouchColorId, Plush = PouchPlushId, Decoration = PouchDecorationId };
        if (!PresetOps.Upsert(PouchPresets, preset, name, p => p.Name, MaxPouchPresets)) return;
        if (_pouchPresetService != null &&
            !PresetOps.Persist(PouchPresets, _pouchPresetService.Save,
                "ポーチプリセット保存エラー", "プリセットの保存に失敗しました。", "保存失敗")) return;
        OnPropertyChanged(nameof(PouchPresetCountText));
        // セッター経由だと保存直後に読込が走るため、バッキングフィールドへ直接反映
        _selectedPouchPreset = preset;
        OnPropertyChanged(nameof(SelectedPouchPreset));
        NewPouchPresetName = string.Empty;
    }

    private void ExecuteDeletePouchPreset()
    {
        if (_selectedPouchPreset == null) return;
        PouchPresets.Remove(_selectedPouchPreset);
        if (_pouchPresetService != null)
        {
            PresetOps.Persist(PouchPresets, _pouchPresetService.Save,
                "ポーチプリセット削除エラー", "プリセットの削除を保存できませんでした。", "削除失敗");
        }
        _selectedPouchPreset = null;
        OnPropertyChanged(nameof(SelectedPouchPreset));
        OnPropertyChanged(nameof(PouchPresetCountText));
    }

    /// <summary>見出しに添える選択の表示 ": 黄 → もじゃ → 花" (未選択なら空)</summary>
    public string PouchHeaderText
    {
        get
        {
            var desc = HifPouchService.Describe(_pouchDefinition, PouchColorId, PouchPlushId, PouchDecorationId);
            return desc != null ? $": {desc}" : string.Empty;
        }
    }

    /// <summary>段階別プレビュー行 (常に 3 行。未選択の段階は "—")</summary>
    public ObservableCollection<HifPouchStageRowViewModel> PouchStageRows { get; } = new();

    private string _pouchSummaryText = string.Empty;
    /// <summary>合計行 "イベント回数へ自動加算: Pドリンク獲得 +6 ／ ..."</summary>
    public string PouchSummaryText
    {
        get => _pouchSummaryText;
        private set => SetProperty(ref _pouchSummaryText, value);
    }

    private string _pouchSummaryLine = string.Empty;
    /// <summary>イベント回数セクションに出す 1 行 "ポーチ自動加算: ..." (未選択なら空)</summary>
    public string PouchSummaryLine
    {
        get => _pouchSummaryLine;
        private set
        {
            if (SetProperty(ref _pouchSummaryLine, value))
                OnPropertyChanged(nameof(PouchSummaryLineVisibility));
        }
    }

    public Visibility PouchSummaryLineVisibility =>
        string.IsNullOrEmpty(_pouchSummaryLine) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// ScheduleItems の選択から計算エンジンに渡す TurnChoice 配列を構築 (固定日・選択肢なしの日はスキップ)。
    /// 計算本体 (BuildHifPlanAndChoices) とポーチのプレビューが同じ規則で日程を解釈するために共有する。
    /// </summary>
    public List<TurnChoice> BuildTurnChoices()
    {
        var turnChoices = new List<TurnChoice>();
        if (_hifPlan == null) return turnChoices;
        var itemsByWeek = ScheduleItems.ToDictionary(it => it.Week);
        foreach (var w in _hifPlan.Schedule)
        {
            if (w.IsFixedEvent) continue;
            // 選択肢なしの日 (本戦インターバル等) は計算対象外。
            // 相談/特別指導はユーザ操作でありサポート効果が発動しないため。
            if (w.AvailableActions.Count == 0) continue;
            if (!itemsByWeek.TryGetValue(w.Week, out var item)) continue;
            if (string.IsNullOrEmpty(item.SelectedAction)) continue;
            if (TurnChoiceViewModel.TryParseAction(item.SelectedAction, out var actionType))
                turnChoices.Add(new TurnChoice { Week = w.Week, ChosenAction = actionType });
        }
        return turnChoices;
    }

    /// <summary>現在の選択で日程からポーチの発動回数を集計する</summary>
    public HifPouchService.Result ComputePouchCounts(IReadOnlyList<TurnChoice> turnChoices)
        => HifPouchService.Compute(_pouchDefinition, PouchColorId, PouchPlushId, PouchDecorationId, turnChoices);

    /// <summary>プレビュー行・合計行を現在の選択と日程で作り直す (選択変更時・日程変更時)</summary>
    public void RefreshPouchPreview()
    {
        var result = ComputePouchCounts(BuildTurnChoices());
        PouchStageRows.Clear();
        var stageNames = new[] { "未カスタマイズ", "1段階目", "2段階目" };
        for (int s = 0; s < 3; s++)
        {
            var st = result.Stages[s];
            if (st == null)
            {
                PouchStageRows.Add(new HifPouchStageRowViewModel
                {
                    StageLabel = stageNames[s],
                    TriggerLabel = "—",
                    EffectText = "—",
                    FiredText = "—",
                    Foreground = "#AAA",
                });
                continue;
            }
            var trigger = HifPouchService.TriggerLabels.GetValueOrDefault(st.Trigger, st.Trigger);
            PouchStageRows.Add(new HifPouchStageRowViewModel
            {
                StageLabel = st.Label,
                TriggerLabel = $"{trigger} ×{st.MaxCount}",
                EffectText = HifPouchService.FormatEffects(st.Effects),
                FiredText = st.Fires > 0
                    ? $"{string.Join(", ", st.FiredWeeks.Select(DayLabel))} → {st.Fires}回"
                    : "発動なし",
                Foreground = st.Fires > 0 ? "#555" : "#DC2626",
            });
        }
        bool selected = PouchColorId != null;
        PouchSummaryText = selected
            ? $"イベント回数へ自動加算: {HifPouchService.FormatCountSummary(result.Counts)}"
            : "ポーチ未選択 (イベント回数への加算なし)";
        PouchSummaryLine = selected
            ? $"ポーチ自動加算: {HifPouchService.FormatCountSummary(result.Counts)}"
            : string.Empty;
    }

    /// <summary>診断情報の [カスタムPアイテム] ブロック (Web版 buildHifDiagnosticReport と同一文面)。未選択なら何も出さない</summary>
    public void AppendPouchDiagnostic(System.Text.StringBuilder sb)
    {
        var desc = HifPouchService.Describe(_pouchDefinition, PouchColorId, PouchPlushId, PouchDecorationId);
        if (desc == null) return;
        var result = ComputePouchCounts(BuildTurnChoices());
        sb.AppendLine();
        sb.AppendLine("[カスタムPアイテム]");
        sb.AppendLine($"選択: {desc}");
        foreach (var st in result.Stages)
            if (st != null) sb.AppendLine(HifPouchService.FormatStageLine(st));
        sb.AppendLine($"加算: {HifPouchService.FormatCountSummary(result.Counts)}");
    }

    /// <summary>日程項目の選択変更でプレビューを更新する (PopulateScheduleItems で購読)</summary>
    private void OnScheduleItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HifScheduleItemViewModel.SelectedAction)
            or nameof(HifScheduleItemViewModel.MainStat))
        {
            RefreshPouchPreview();
        }
    }

    /// <summary>週番号の表示ラベル ("Day 9" / "本戦6日目" / "本戦R1" 等)</summary>
    public static string DayLabel(int week)
    {
        if (week <= 20) return $"Day {week}";
        return week switch
        {
            27 => "本戦R1",
            28 => "本戦インターバル",
            29 => "本戦R2",
            _ => $"本戦{week - 20}日目",
        };
    }
}

/// <summary>ポーチの段階別プレビュー 1 行</summary>
public class HifPouchStageRowViewModel
{
    public string StageLabel { get; init; } = string.Empty;
    public string TriggerLabel { get; init; } = string.Empty;
    public string EffectText { get; init; } = string.Empty;
    public string FiredText { get; init; } = string.Empty;
    /// <summary>行の文字色 (発動 0 回は注意色)</summary>
    public string Foreground { get; init; } = "#555";
}
