using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GakumasuCalc.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// メインタブ切り替え時、初レジェンド/NIA タブに対応する育成プランを固定する。
    /// HIF タブ (Tag なし) は HifVm を使うため SelectedPlan を変更しない。
    /// </summary>
    private void OnMainTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // タブ内 ComboBox 等のバブリングを除外し、メインタブ切り替えだけを拾う
        if (e.Source is not TabControl tc) return;
        if (DataContext is not ViewModels.MainViewModel vm) return;

        var planId = (tc.SelectedItem as TabItem)?.Tag as string;
        if (string.IsNullOrEmpty(planId)) return;

        var plan = vm.AvailablePlans.FirstOrDefault(p => p.Id == planId);
        if (plan != null && !ReferenceEquals(vm.SelectedPlan, plan))
            vm.SelectedPlan = plan;
    }
}
