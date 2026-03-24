using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GakumasuCalc.Converters;

public class StatusTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = parameter?.ToString() ?? value?.ToString() ?? "";
        return type.ToLower() switch
        {
            "vo" => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)),   // 赤系
            "da" => new SolidColorBrush(Color.FromRgb(0x6B, 0x9F, 0xFF)),   // 青系
            "vi" => new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x6B)),   // 黄系
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? false : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? false : true;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ActionTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GakumasuCalc.Models.ActionType action)
        {
            return action switch
            {
                GakumasuCalc.Models.ActionType.VoLesson => "Voレッスン",
                GakumasuCalc.Models.ActionType.DaLesson => "Daレッスン",
                GakumasuCalc.Models.ActionType.ViLesson => "Viレッスン",
                GakumasuCalc.Models.ActionType.VoClass => "Vo授業",
                GakumasuCalc.Models.ActionType.DaClass => "Da授業",
                GakumasuCalc.Models.ActionType.ViClass => "Vi授業",
                GakumasuCalc.Models.ActionType.Outing => "お出かけ",
                GakumasuCalc.Models.ActionType.Rest => "休憩",
                GakumasuCalc.Models.ActionType.Consultation => "相談",
                GakumasuCalc.Models.ActionType.ActivitySupply => "活動支給",
                GakumasuCalc.Models.ActionType.SpecialTraining => "特別指導",
                _ => value.ToString() ?? ""
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
