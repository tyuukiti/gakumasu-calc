using System;
using System.Windows.Controls;

namespace GakumasuCalc.Views;

public partial class HifTabContent : UserControl
{
    public HifTabContent()
    {
        InitializeComponent();
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
}
