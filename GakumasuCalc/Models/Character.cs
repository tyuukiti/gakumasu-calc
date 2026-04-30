using YamlDotNet.Serialization;

namespace GakumasuCalc.Models;

public class CharacterFile
{
    public List<Character> Characters { get; set; } = new();
}

/// <summary>
/// キャラ用の属性別パーセント値。para_bonus / uncap3_bonus などで小数を扱うため double。
/// </summary>
public class StatBonusPercent
{
    public double Vo { get; set; }
    public double Da { get; set; }
    public double Vi { get; set; }

    public static StatBonusPercent Zero => new();

    public StatBonusPercent Add(StatBonusPercent? other)
    {
        if (other == null) return new StatBonusPercent { Vo = Vo, Da = Da, Vi = Vi };
        return new StatBonusPercent { Vo = Vo + other.Vo, Da = Da + other.Da, Vi = Vi + other.Vi };
    }

    public StatBonusPercent Subtract(StatBonusPercent? other)
    {
        if (other == null) return new StatBonusPercent { Vo = Vo, Da = Da, Vi = Vi };
        return new StatBonusPercent { Vo = Vo - other.Vo, Da = Da - other.Da, Vi = Vi - other.Vi };
    }
}

/// <summary>
/// キャラクター固有データ。基礎ステータス加算と属性別パラメータボーナス%を持つ。
/// uncap3_bonus は3凸時に付くレッスンボーナス（任意、null可）。
/// </summary>
public class Character
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#888888";
    public string Initial { get; set; } = "?";

    [YamlMember(Alias = "base_status_bonus")]
    public StatusValues BaseStatusBonus { get; set; } = StatusValues.Zero;

    [YamlMember(Alias = "para_bonus")]
    public StatBonusPercent ParaBonus { get; set; } = StatBonusPercent.Zero;

    [YamlMember(Alias = "uncap3_bonus")]
    public StatBonusPercent? Uncap3Bonus { get; set; }

    public override string ToString() => Name;
}
