using YamlDotNet.Serialization;

namespace GakumasuCalc.Models;

public class SupportCardFile
{
    [YamlMember(Alias = "support_cards")]
    public List<SupportCard> SupportCards { get; set; } = new();
}

public class SupportCard
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "rarity")]
    public string Rarity { get; set; } = "SSR";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "vo";

    [YamlMember(Alias = "plan")]
    public string Plan { get; set; } = string.Empty;

    [YamlMember(Alias = "tag")]
    public string Tag { get; set; } = string.Empty;

    [YamlMember(Alias = "effects")]
    public List<CardEffect> Effects { get; set; } = new();

    public override string ToString() => $"{Name} ({Rarity})";

    // ---- 計算用ヘルパー ----

    /// <summary>初期値ボーナス (装備時固定加算)</summary>
    public StatusValues GetInitialBonus(int uncapLevel = 4)
    {
        int vo = 0, da = 0, vi = 0;
        foreach (var e in Effects.Where(e => e.Trigger == "equip" && e.ValueType == "flat"))
        {
            var v = (int)e.GetValue(uncapLevel);
            switch (e.Stat)
            {
                case "vo": vo += v; break;
                case "da": da += v; break;
                case "vi": vi += v; break;
            }
        }
        return new StatusValues(vo, da, vi);
    }

    /// <summary>SP率% (装備時)</summary>
    public LessonBonusPercent GetSpRate(int uncapLevel = 4)
    {
        var result = new LessonBonusPercent();
        foreach (var e in Effects.Where(e => e.Trigger == "equip" && e.ValueType == "sp_rate"))
        {
            var val = (int)Math.Round(e.GetValue(uncapLevel));
            switch (e.Stat)
            {
                case "vo": result.VoPercent += val; break;
                case "da": result.DaPercent += val; break;
                case "vi": result.ViPercent += val; break;
                case "all":
                    result.VoPercent += val;
                    result.DaPercent += val;
                    result.ViPercent += val;
                    break;
            }
        }
        return result;
    }

    /// <summary>パラメータボーナス% (装備時)</summary>
    public double GetParaBonus(int uncapLevel = 4)
    {
        return Effects
            .Where(e => e.Trigger == "equip" && e.ValueType == "para_bonus")
            .Sum(e => e.GetValue(uncapLevel));
    }

    /// <summary>指定トリガーの効果一覧を取得</summary>
    public IEnumerable<CardEffect> GetEffectsByTrigger(string trigger)
    {
        return Effects.Where(e => e.Trigger == trigger);
    }
}

/// <summary>
/// サポートカードの個別効果
/// </summary>
public class CardEffect
{
    /// <summary>
    /// トリガー種別:
    /// "equip" = 装備時(常時), "sp_end" = SPレッスン終了時,
    /// "lesson_end" = レッスン終了時, "class_end" = 授業終了時,
    /// "outing_end" = お出かけ終了時, "consultation" = 相談選択時,
    /// "activity_supply" = 活動支給選択時, "exam_end" = 試験・オーディション終了時,
    /// "special_training" = 特別指導開始時,
    /// "skill_acquire" = スキル獲得時, "skill_ssr_acquire" = スキル(SSR)獲得時,
    /// "skill_delete" = スキル削除時, "skill_enhance" = スキル強化時,
    /// "skill_custom" = スキルカスタム時, "skill_change" = スキルチェンジ時,
    /// "active_enhance" = アクティブ強化時, "active_delete" = アクティブ削除時,
    /// "mental_acquire" = メンタル獲得時, "genki_acquire" = 元気カード獲得時,
    /// "good_condition_acquire" = 好調カード獲得時,
    /// "good_impression_acquire" = 好印象カード獲得時,
    /// "conserve_acquire" = 温存カード獲得時,
    /// "concentrate_acquire" = 集中カード獲得時,
    /// "motivation_acquire" = やる気カード獲得時,
    /// "fullpower_acquire" = 全力カード獲得時,
    /// "aggressive_acquire" = 強気カード獲得時,
    /// "p_item_acquire" = Pアイテム獲得時, "p_drink_acquire" = Pドリンク獲得時,
    /// "consultation_drink" = 相談Pドリンク交換後
    /// </summary>
    [YamlMember(Alias = "trigger")]
    public string Trigger { get; set; } = string.Empty;

    /// <summary>対象ステータス: "vo", "da", "vi", "all"</summary>
    [YamlMember(Alias = "stat")]
    public string Stat { get; set; } = string.Empty;

    /// <summary>凸数別の効果値 [0凸, 1凸, 2凸, 3凸, 4凸]</summary>
    [YamlMember(Alias = "values")]
    public List<double> Values { get; set; } = new();

    /// <summary>指定凸数の効果値を取得</summary>
    public double GetValue(int uncapLevel = 4) => Values.Count > 0
        ? Values[Math.Clamp(uncapLevel, 0, Math.Min(4, Values.Count - 1))]
        : 0;

    /// <summary>
    /// 値の種類: "flat" = 実数値加算, "sp_rate" = SP率%,
    /// "para_bonus" = パラメータボーナス%, "percent" = 上昇量%
    /// </summary>
    [YamlMember(Alias = "value_type")]
    public string ValueType { get; set; } = "flat";

    /// <summary>発動回数上限 (null=無制限)</summary>
    [YamlMember(Alias = "max_count")]
    public int? MaxCount { get; set; }

    /// <summary>発動条件 (例: "vo>=400", "deck>=20")</summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>説明テキスト</summary>
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    /// <summary>効果の出典 (例: "item" = プロデュースアイテム効果)</summary>
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }
}

public class LessonBonusPercent
{
    [YamlMember(Alias = "vo_percent")]
    public int VoPercent { get; set; }

    [YamlMember(Alias = "da_percent")]
    public int DaPercent { get; set; }

    [YamlMember(Alias = "vi_percent")]
    public int ViPercent { get; set; }
}
