using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GakumasuCalc.Views;

public partial class CalculatorTabContent : UserControl
{
    public CalculatorTabContent()
    {
        InitializeComponent();
    }

    /// <summary>
    /// メモリープリセット ComboBox のドロップダウン閉鎖時。
    /// 同じ項目を再選択した場合も SelectionChanged が発火しないため、ここで明示的に再読み込みする。
    /// </summary>
    private void OnMemoryPresetDropDownClosed(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ReloadSelectedMemoryPreset();
    }

    /// <summary>
    /// イベント回数プリセット ComboBox のドロップダウン閉鎖時。
    /// </summary>
    private void OnEventCountPresetDropDownClosed(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ReloadSelectedEventCountPreset();
    }

    /// <summary>個別調整パネル下端のつまみドラッグで表示高さを変更する (範囲制限は VM 側)。</summary>
    private void ScheduleResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.CalcSchedulePanelHeight += e.VerticalChange;
    }

    /// <summary>つまみドラッグ確定時に高さを保存する (次回起動時に復元)。</summary>
    private void ScheduleResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.PersistSchedulePanelHeights();
    }
}
