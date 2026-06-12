using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GakumasuCalc.Views;

/// <summary>
/// 上下ボタン付きの整数入力 (Web版の number input 相当)。
/// Value は既定で双方向バインド。Minimum/Maximum で範囲をクランプする。
/// </summary>
public partial class NumberSpinner : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumberSpinner),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                propertyChangedCallback: null, coerceValueCallback: CoerceValueCallback));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberSpinner),
            new PropertyMetadata(0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberSpinner),
            new PropertyMetadata(99, OnRangeChanged));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public NumberSpinner()
    {
        InitializeComponent();
    }

    private static object CoerceValueCallback(DependencyObject d, object baseValue)
    {
        var s = (NumberSpinner)d;
        int v = (int)baseValue;
        if (v < s.Minimum) return s.Minimum;
        if (v > s.Maximum) return s.Maximum;
        return v;
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(ValueProperty);
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        if (Value < Maximum) Value++;
    }

    private void OnDownClick(object sender, RoutedEventArgs e)
    {
        if (Value > Minimum) Value--;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 数字のみ許可 (負数は SP枚数用途では不要)
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
