using System.Windows;
using System.Windows.Input;
using CardInventoryManager.ViewModels;

namespace CardInventoryManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CardInventoryItemViewModel item)
        {
            item.Owned = !item.Owned;
        }
    }
}
