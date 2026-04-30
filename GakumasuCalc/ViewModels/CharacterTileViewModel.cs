using System.Windows.Media;
using GakumasuCalc.Models;

namespace GakumasuCalc.ViewModels;

public class CharacterTileViewModel : ViewModelBase
{
    public Character Character { get; }
    public string Name => Character.Name;
    public string Initial => Character.Initial;

    public Brush BackgroundBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(Character.Color);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
    }

    /// <summary>
    /// 背景の明度に応じて文字色（白 or 濃いグレー）を返す。YIQ式で算出。
    /// </summary>
    public Brush ForegroundBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(Character.Color);
                var yiq = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                return yiq >= 160
                    ? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))
                    : new SolidColorBrush(Colors.White);
            }
            catch
            {
                return new SolidColorBrush(Colors.White);
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(BorderThickness));
        }
    }

    public System.Windows.Thickness BorderThickness =>
        _isSelected ? new System.Windows.Thickness(3) : new System.Windows.Thickness(0);

    public CharacterTileViewModel(Character character)
    {
        Character = character;
    }
}
