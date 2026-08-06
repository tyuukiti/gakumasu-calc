using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GakumasuCalc.Views;

public partial class HifTabContent : UserControl
{
    private bool _examDragging;
    private ViewModels.HifViewModel? _subscribedVm;

    /// <summary>属性キーと、対応するセグメント色ブラシのリソースキー (Vo→Da→Vi 順)。</summary>
    private static readonly (string Key, string BrushKey, string Label)[] ExamStats =
    {
        ("vo", "VoBrush", "Vo"), ("da", "DaBrush", "Da"), ("vi", "ViBrush", "Vi"),
    };

    /// <summary>動的生成した「有効属性カラム」(比率算出・ドラッグ中ラベル更新に使用)。</summary>
    private readonly List<(string Stat, ColumnDefinition Col, TextBlock Label)> _examCols = new();

    public HifTabContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private ViewModels.HifViewModel? HifVmOrNull => (DataContext as ViewModels.MainViewModel)?.HifVm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = HifVmOrNull;
        if (vm != null && !ReferenceEquals(vm, _subscribedVm))
        {
            if (_subscribedVm != null) _subscribedVm.ExamRatioChanged -= BuildExamBar;
            vm.ExamRatioChanged += BuildExamBar;
            _subscribedVm = vm;
        }
        BuildExamBar();
    }

    /// <summary>
    /// VM の比率から試験配分バーを再構築する (プリセット適用・初期表示・ドラッグ確定時)。
    /// 配分が0の属性はセグメント・スプリッターとも生成しないため、ドラッグで復活しない。
    /// 有効属性が隣り合うので、その間の GridSplitter だけが両者を再配分する。
    /// </summary>
    private void BuildExamBar()
    {
        var vm = HifVmOrNull;
        if (vm == null) return;
        if (_examDragging) return; // ドラッグ中はネイティブの列リサイズに任せる

        var ratios = new Dictionary<string, int>
        {
            ["vo"] = vm.ExamRatioVo, ["da"] = vm.ExamRatioDa, ["vi"] = vm.ExamRatioVi,
        };
        var active = ExamStats.Where(s => ratios[s.Key] > 0).ToList();

        ExamRatioBarGrid.ColumnDefinitions.Clear();
        ExamRatioBarGrid.Children.Clear();
        _examCols.Clear();

        int gridCol = 0;
        for (int n = 0; n < active.Count; n++)
        {
            var s = active[n];

            var col = new ColumnDefinition { Width = new GridLength(ratios[s.Key], GridUnitType.Star) };
            ExamRatioBarGrid.ColumnDefinitions.Add(col);

            var label = new TextBlock
            {
                Text = $"{s.Label} {ratios[s.Key]}%",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var border = new Border
            {
                Background = (Brush)FindResource(s.BrushKey),
                CornerRadius = SegmentCorner(n, active.Count),
                Child = label,
            };
            Grid.SetColumn(border, gridCol);
            ExamRatioBarGrid.Children.Add(border);
            _examCols.Add((s.Key, col, label));
            gridCol++;

            // 次の有効属性との境界に GridSplitter を挟む
            if (n < active.Count - 1)
            {
                ExamRatioBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var splitter = new GridSplitter
                {
                    Width = 6,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.White,
                    ShowsPreview = false,
                    Cursor = Cursors.SizeWE,
                };
                splitter.DragStarted += ExamSplitter_DragStarted;
                splitter.DragDelta += ExamSplitter_DragDelta;
                splitter.DragCompleted += ExamSplitter_DragCompleted;
                Grid.SetColumn(splitter, gridCol);
                ExamRatioBarGrid.Children.Add(splitter);
                gridCol++;
            }
        }
    }

    /// <summary>セグメント位置に応じた角丸 (両端のみ丸める。単独なら全周)。</summary>
    private static CornerRadius SegmentCorner(int index, int count)
    {
        if (count <= 1) return new CornerRadius(4);
        if (index == 0) return new CornerRadius(4, 0, 0, 4);
        if (index == count - 1) return new CornerRadius(0, 4, 4, 0);
        return new CornerRadius(0);
    }

    private void ExamSplitter_DragStarted(object sender, DragStartedEventArgs e) => _examDragging = true;

    private void ExamSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var vm = HifVmOrNull;
        if (vm != null) PushExamRatioFromBar(vm);
    }

    private void ExamSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _examDragging = false;
        var vm = HifVmOrNull;
        if (vm != null) PushExamRatioFromBar(vm);
        BuildExamBar(); // 整数比率にスナップして再構築
    }

    /// <summary>
    /// バーの現在カラム幅 → VM の比率へ反映し、全試験へ按分。
    /// 非表示(0)の属性は _examCols に含まれないため 0 のまま維持される。
    /// </summary>
    private void PushExamRatioFromBar(ViewModels.HifViewModel vm)
    {
        double sum = _examCols.Sum(c => c.Col.ActualWidth);
        if (sum <= 0) return;

        var pct = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var c in _examCols)
            pct[c.Stat] = (int)Math.Round(c.Col.ActualWidth / sum * 100);

        vm.ApplyExamRatio(pct["vo"], pct["da"], pct["vi"]);

        // ドラッグ中のライブ%表示更新 (再構築はドラッグ確定時に行う)
        foreach (var c in _examCols)
            c.Label.Text = $"{StatLabelOf(c.Stat)} {pct[c.Stat]}%";
    }

    private static string StatLabelOf(string stat) =>
        ExamStats.First(s => s.Key == stat).Label;

    /// <summary>
    /// プリセット ComboBox のドロップダウン閉鎖時。
    /// 同じ項目を再選択した場合は SelectionChanged が発火しないため、
    /// ここで明示的に再読み込みを呼ぶ。
    /// </summary>
    private void OnMemoryPresetDropDownClosed(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ReloadSelectedMemoryPreset();
    }

    /// <summary>
    /// イベント回数プリセット ComboBox のドロップダウン閉鎖時。
    /// 同じ項目を再選択した場合も明示的に再読み込みする。
    /// </summary>
    private void OnEventCountPresetDropDownClosed(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ReloadSelectedEventCountPreset();
    }

    /// <summary>
    /// 条件プリセット ComboBox のドロップダウン閉鎖時。
    /// 同じ項目を再選択した場合も明示的に再読み込みする。
    /// </summary>
    private void OnHifConditionPresetDropDownClosed(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ReloadSelectedHifConditionPreset();
    }

    /// <summary>個別調整パネル下端のつまみドラッグで表示高さを変更する (範囲制限は VM 側)。</summary>
    private void ScheduleResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.HifSchedulePanelHeight += e.VerticalChange;
    }

    /// <summary>つまみドラッグ確定時に高さを保存する (次回起動時に復元)。</summary>
    private void ScheduleResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.PersistSchedulePanelHeights();
    }
}
