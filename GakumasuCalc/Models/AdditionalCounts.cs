namespace GakumasuCalc.Models;

/// <summary>
/// ユーザが入力するレッスン内・育成中のイベント発生回数。
/// CardScoringService でトリガー発火回数に加算される。
/// </summary>
public class AdditionalCounts
{
    // レッスン内イベント
    public int PDrinkAcquire { get; set; }
    public int PItemAcquire { get; set; }
    public int SkillSsrAcquire { get; set; }
    public int SkillEnhance { get; set; }
    public int SkillDelete { get; set; }
    public int SkillCustom { get; set; }
    public int SkillChange { get; set; }
    public int ActiveEnhance { get; set; }
    public int ActiveDelete { get; set; }

    // カード種別獲得
    public int MentalAcquire { get; set; }
    public int GenkiAcquire { get; set; }
    public int GoodConditionAcquire { get; set; }
    public int GoodImpressionAcquire { get; set; }
    public int ConserveAcquire { get; set; }
    public int ConcentrateAcquire { get; set; }
    public int MotivationAcquire { get; set; }
    public int FullpowerAcquire { get; set; }
    public int AggressiveAcquire { get; set; }

    // カード操作
    public int MentalEnhance { get; set; }
    public int MentalDelete { get; set; }
    public int ActiveAcquire { get; set; }

    // その他
    public int ConsultationDrink { get; set; }

    public Dictionary<string, int> ToDictionary()
    {
        return new Dictionary<string, int>
        {
            ["p_drink_acquire"] = PDrinkAcquire,
            ["p_item_acquire"] = PItemAcquire,
            ["skill_ssr_acquire"] = SkillSsrAcquire,
            ["skill_enhance"] = SkillEnhance,
            ["skill_delete"] = SkillDelete,
            ["skill_custom"] = SkillCustom,
            ["skill_change"] = SkillChange,
            ["active_enhance"] = ActiveEnhance,
            ["active_delete"] = ActiveDelete,
            ["mental_acquire"] = MentalAcquire,
            ["mental_enhance"] = MentalEnhance,
            ["mental_delete"] = MentalDelete,
            ["active_acquire"] = ActiveAcquire,
            ["genki_acquire"] = GenkiAcquire,
            ["good_condition_acquire"] = GoodConditionAcquire,
            ["good_impression_acquire"] = GoodImpressionAcquire,
            ["conserve_acquire"] = ConserveAcquire,
            ["concentrate_acquire"] = ConcentrateAcquire,
            ["motivation_acquire"] = MotivationAcquire,
            ["fullpower_acquire"] = FullpowerAcquire,
            ["aggressive_acquire"] = AggressiveAcquire,
            ["consultation_drink"] = ConsultationDrink,
        };
    }
}
