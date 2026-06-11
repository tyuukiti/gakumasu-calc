using System;
using System.Windows.Controls;

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
}
