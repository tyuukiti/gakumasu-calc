using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GakumasuCalc.Views;

public partial class HifTabContent : UserControl
{
    private bool _examDragging;
    private ViewModels.HifViewModel? _subscribedVm;

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
            if (_subscribedVm != null) _subscribedVm.ExamRatioChanged -= SyncExamBarFromRatio;
            vm.ExamRatioChanged += SyncExamBarFromRatio;
            _subscribedVm = vm;
        }
        SyncExamBarFromRatio();
    }

    /// <summary>VM の比率 → バーの星形カラム幅へ反映 (プリセット適用・初期表示・ドラッグ確定時)。</summary>
    private void SyncExamBarFromRatio()
    {
        var vm = HifVmOrNull;
        if (vm == null) return;
        if (_examDragging) return; // ドラッグ中はネイティブの列リサイズに任せる
        ExamVoCol.Width = new GridLength(Math.Max(0, vm.ExamRatioVo), GridUnitType.Star);
        ExamDaCol.Width = new GridLength(Math.Max(0, vm.ExamRatioDa), GridUnitType.Star);
        ExamViCol.Width = new GridLength(Math.Max(0, vm.ExamRatioVi), GridUnitType.Star);
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
        SyncExamBarFromRatio(); // 整数比率にスナップ
    }

    /// <summary>バーの現在カラム幅 → VM の比率へ反映し、全試験へ按分。</summary>
    private void PushExamRatioFromBar(ViewModels.HifViewModel vm)
    {
        double vo = ExamVoCol.ActualWidth, da = ExamDaCol.ActualWidth, vi = ExamViCol.ActualWidth;
        double sum = vo + da + vi;
        if (sum <= 0) return;
        vm.ApplyExamRatio(
            (int)Math.Round(vo / sum * 100),
            (int)Math.Round(da / sum * 100),
            (int)Math.Round(vi / sum * 100));
    }

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
}
