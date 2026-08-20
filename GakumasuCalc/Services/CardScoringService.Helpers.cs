using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    private static string TriggerDisplayName(string trigger) => trigger switch
    {
        "equip" => "装備",
        "sp_end" => "SP終了",
        "lesson_end" => "レッスン終了",
        "class_end" => "授業終了",
        "outing_end" => "お出かけ終了",
        "consultation" => "相談",
        "activity_supply" => "活動支給",
        "exam_end" => "試験終了",
        "special_training" => "特別指導",
        "skill_acquire" => "スキル獲得",
        "skill_ssr_acquire" => "スキル(SSR)獲得",
        "skill_enhance" => "スキル強化",
        "skill_delete" => "スキル削除",
        "skill_custom" => "スキルカスタム",
        "skill_change" => "スキルチェンジ",
        "active_enhance" => "アクティブ強化",
        "active_delete" => "アクティブ削除",
        "mental_acquire" => "メンタル獲得",
        "mental_enhance" => "メンタル強化",
        "mental_delete" => "メンタル削除",
        "active_acquire" => "アクティブ獲得",
        "genki_acquire" => "元気獲得",
        "good_condition_acquire" => "好調獲得",
        "good_impression_acquire" => "好印象獲得",
        "conserve_acquire" => "温存獲得",
        "concentrate_acquire" => "集中獲得",
        "motivation_acquire" => "やる気獲得",
        "fullpower_acquire" => "全力獲得",
        "aggressive_acquire" => "強気獲得",
        "p_item_acquire" => "Pアイテム獲得",
        "p_drink_acquire" => "Pドリンク獲得",
        "consultation_drink" => "相談ドリンク交換",
        "rest" => "休む",
        "vo_sp_end" => "VoSP終了",
        "da_sp_end" => "DaSP終了",
        "vi_sp_end" => "ViSP終了",
        "vo_lesson_end" => "Voレッスン終了",
        "da_lesson_end" => "Daレッスン終了",
        "vi_lesson_end" => "Viレッスン終了",
        "vo_normal_end" => "Vo通常終了",
        "da_normal_end" => "Da通常終了",
        "vi_normal_end" => "Vi通常終了",
        _ => trigger
    };

    private string BuildReasonText(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel, SupportCard card)
    {
        var prefix = effect.Source == "item" ? "[アイテム] " : "";
        var triggerName = TriggerDisplayName(effect.Trigger);
        var stat = effect.Stat.ToUpper();
        var val = effect.GetValue(uncapLevel);

        if (effect.Trigger == "equip")
        {
            if (effect.ValueType == "flat" && effect.EventParam)
            {
                var boost = card.GetEventParamBoostPercent(uncapLevel);
                var result = (int)(val * (1.0 + boost / 100.0));
                return $"{prefix}{stat} 初期値+{(int)val}(+{(int)boost}%)={result}";
            }
            return effect.ValueType switch
            {
                "sp_rate" => $"{prefix}{stat} SP率+{val}%",
                "para_bonus" => $"{prefix}パラボ+{val}%",
                _ => $"{prefix}{stat} 初期値+{(int)val}"
            };
        }

        int fires = triggerCounts.GetValueOrDefault(effect.Trigger, 0);
        if (effect.MaxCount.HasValue)
            fires = Math.Min(fires, effect.MaxCount.Value);

        var countInfo = effect.MaxCount.HasValue
            ? $"({fires}/{effect.MaxCount}回)"
            : $"(×{fires})";

        return effect.ValueType switch
        {
            "flat" => $"{prefix}{triggerName} {stat}+{(int)val} {countInfo}",
            _ => $"{prefix}{triggerName} {stat}+{val}% {countInfo}"
        };
    }

    private double CalculateFlatValue(CardEffect effect, Dictionary<string, int> triggerCounts, int uncapLevel, SupportCard card)
    {
        var val = effect.GetValue(uncapLevel);
        if (effect.Trigger == "equip")
        {
            if (effect.EventParam)
            {
                val *= 1.0 + card.GetEventParamBoostPercent(uncapLevel) / 100.0;
            }
            return val;
        }

        int fires = triggerCounts.GetValueOrDefault(effect.Trigger, 0);

        if (effect.MaxCount.HasValue)
            fires = Math.Min(fires, effect.MaxCount.Value);

        return val * fires;
    }
}
