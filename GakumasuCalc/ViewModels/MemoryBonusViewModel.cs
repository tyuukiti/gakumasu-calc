using GakumasuCalc.Models;

namespace GakumasuCalc.ViewModels;

/// <summary>
/// 持ち込みメモリー1スロット分の ViewModel。Vo/Da/Vi 各属性に値と種別（実数/パラボ%）を持つ。
/// 値変更時は親（MainViewModel）の再計算コールバックを発火する。
/// </summary>
public class MemoryBonusViewModel : ViewModelBase
{
    private readonly Action? _onChanged;
    private double _voValue;
    private double _daValue;
    private double _viValue;
    private MemoryBonusType _voType = MemoryBonusType.Flat;
    private MemoryBonusType _daType = MemoryBonusType.Flat;
    private MemoryBonusType _viType = MemoryBonusType.Flat;

    public int Index { get; }

    public MemoryBonusViewModel(int index, Action? onChanged = null)
    {
        Index = index;
        _onChanged = onChanged;
    }

    public double VoValue
    {
        get => _voValue;
        set { if (SetProperty(ref _voValue, value)) _onChanged?.Invoke(); }
    }

    public double DaValue
    {
        get => _daValue;
        set { if (SetProperty(ref _daValue, value)) _onChanged?.Invoke(); }
    }

    public double ViValue
    {
        get => _viValue;
        set { if (SetProperty(ref _viValue, value)) _onChanged?.Invoke(); }
    }

    public MemoryBonusType VoType
    {
        get => _voType;
        set { if (SetProperty(ref _voType, value)) _onChanged?.Invoke(); }
    }

    public MemoryBonusType DaType
    {
        get => _daType;
        set { if (SetProperty(ref _daType, value)) _onChanged?.Invoke(); }
    }

    public MemoryBonusType ViType
    {
        get => _viType;
        set { if (SetProperty(ref _viType, value)) _onChanged?.Invoke(); }
    }

    /// <summary>
    /// ComboBox バインディング用の列挙値リスト。表示は EnumToLabelConverter または ItemTemplate で「実」「%」に変換。
    /// </summary>
    public static IReadOnlyList<MemoryBonusType> TypeOptions { get; } = new[]
    {
        MemoryBonusType.Flat,
        MemoryBonusType.ParaBonus,
    };

    public bool IsEmpty => _voValue == 0 && _daValue == 0 && _viValue == 0;

    /// <summary>値を全て0、種別をFlatにリセット。</summary>
    public void Reset()
    {
        VoValue = 0; DaValue = 0; ViValue = 0;
        VoType = MemoryBonusType.Flat;
        DaType = MemoryBonusType.Flat;
        ViType = MemoryBonusType.Flat;
    }

    /// <summary>計算モデルへ変換する。</summary>
    public MemoryBonus ToModel()
    {
        return new MemoryBonus
        {
            Vo = new MemoryAttributeBonus(_voValue, _voType),
            Da = new MemoryAttributeBonus(_daValue, _daType),
            Vi = new MemoryAttributeBonus(_viValue, _viType),
        };
    }
}
