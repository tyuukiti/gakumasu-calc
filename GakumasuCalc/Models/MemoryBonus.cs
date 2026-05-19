using YamlDotNet.Serialization;

namespace GakumasuCalc.Models;

/// <summary>
/// メモリーボーナスの種別。Flat=基礎ステータス実数値加算、ParaBonus=レッスンパラメーターボーナス%。
/// </summary>
public enum MemoryBonusType
{
    Flat,
    ParaBonus,
}

/// <summary>
/// メモリー1属性分のボーナス値と種別。
/// </summary>
public class MemoryAttributeBonus
{
    public double Value { get; set; }
    public MemoryBonusType Type { get; set; } = MemoryBonusType.Flat;

    [YamlIgnore]
    public bool IsEmpty => Value == 0;

    public MemoryAttributeBonus() { }

    public MemoryAttributeBonus(double value, MemoryBonusType type)
    {
        Value = value;
        Type = type;
    }
}

/// <summary>
/// 持ち込みメモリー1枚分のデータ。Vo/Da/Vi 各属性ごとに「実数値加算」または「パラボ%」を1値持つ。
/// 計算時はキャラ補正と並列に StatusCalculationService.Calculate へ渡される。
/// </summary>
public class MemoryBonus
{
    public MemoryAttributeBonus Vo { get; set; } = new();
    public MemoryAttributeBonus Da { get; set; } = new();
    public MemoryAttributeBonus Vi { get; set; } = new();

    [YamlIgnore]
    public bool IsEmpty => Vo.IsEmpty && Da.IsEmpty && Vi.IsEmpty;

    /// <summary>
    /// 複数メモリーの flat 種別のみを属性別に合計して StatusValues として返す。
    /// </summary>
    public static StatusValues SumFlat(IEnumerable<MemoryBonus>? memories)
    {
        if (memories == null) return StatusValues.Zero;

        double vo = 0, da = 0, vi = 0;
        foreach (var m in memories)
        {
            if (m.Vo.Type == MemoryBonusType.Flat) vo += m.Vo.Value;
            if (m.Da.Type == MemoryBonusType.Flat) da += m.Da.Value;
            if (m.Vi.Type == MemoryBonusType.Flat) vi += m.Vi.Value;
        }
        return new StatusValues((int)Math.Floor(vo), (int)Math.Floor(da), (int)Math.Floor(vi));
    }

    /// <summary>
    /// 複数メモリーの para_bonus 種別のみを属性別に合計して StatBonusPercent として返す。
    /// </summary>
    public static StatBonusPercent SumParaBonus(IEnumerable<MemoryBonus>? memories)
    {
        if (memories == null) return StatBonusPercent.Zero;

        double vo = 0, da = 0, vi = 0;
        foreach (var m in memories)
        {
            if (m.Vo.Type == MemoryBonusType.ParaBonus) vo += m.Vo.Value;
            if (m.Da.Type == MemoryBonusType.ParaBonus) da += m.Da.Value;
            if (m.Vi.Type == MemoryBonusType.ParaBonus) vi += m.Vi.Value;
        }
        return new StatBonusPercent { Vo = vo, Da = da, Vi = vi };
    }

    /// <summary>
    /// リスト内に1枚でも 0 以外の値があれば true。
    /// </summary>
    public static bool HasAny(IEnumerable<MemoryBonus>? memories)
    {
        if (memories == null) return false;
        foreach (var m in memories)
        {
            if (!m.IsEmpty) return true;
        }
        return false;
    }
}
