using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    public const int DEFAULT_STAT_CAP = 2800;

    public class CardScore
    {
        public SupportCard Card { get; set; } = null!;
        public int TotalValue { get; set; }
        /// <summary>属性別の寄与内訳 (キャップ適用前)</summary>
        public int RawVo { get; set; }
        public int RawDa { get; set; }
        public int RawVi { get; set; }
        /// <summary>trigger_count_bonus 由来で「他カードへ寄与する」推定総量 (表示専用)</summary>
        public int TeamBonusTotal { get; set; }
        /// <summary>trigger_count_bonus 由来の寄与内訳 (UI で全件並べる用)</summary>
        public List<TeamBonusContributor> TeamBonusContributors { get; set; } = new();
        /// <summary>効果別の内訳</summary>
        public List<EffectBreakdown> Breakdowns { get; set; } = new();
        /// <summary>レンタルカードかどうか</summary>
        public bool IsRental { get; set; }
        /// <summary>必須カードかどうか</summary>
        public bool IsRequired { get; set; }
        /// <summary>計算に使われた凸数 (0-4)。レンタルは4凸借用、所持のみOFFの未所持カードは4。</summary>
        public int UncapLevel { get; set; }
    }

    public class TeamBonusContributor
    {
        public string CardName { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class EffectBreakdown
    {
        public string Reason { get; set; } = string.Empty;
        public string Stat { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class DeckResult
    {
        public string Label { get; set; } = string.Empty;
        public List<CardScore> SelectedCards { get; set; } = new();
        public int TotalValue => SelectedCards.Sum(c => c.TotalValue);
        /// <summary>アビリティまとめ (行動別)。total 降順。行動トリガーが1件も無ければ空。</summary>
        public List<AbilitySummaryEntry> AbilitySummary { get; set; } = new();
    }

    /// <summary>
    /// アビリティまとめ (行動別) の1エントリ。選択6枚の flat 効果 (trigger != "equip") を
    /// (行動トリガー × 属性) で合算したもの。「どの行動を取るとパラメが伸びるか」の比較用。
    /// 値は各カード個別内訳と同じ生寄与 (cap前・キャラパラボ前)。
    /// </summary>
    public class AbilitySummaryEntry
    {
        /// <summary>トリガーキー (例: "class_end")</summary>
        public string Trigger { get; set; } = string.Empty;
        /// <summary>トリガー表示名 (例: "授業終了")</summary>
        public string TriggerName { get; set; } = string.Empty;
        /// <summary>属性 ("vo" | "da" | "vi" | "all")</summary>
        public string Stat { get; set; } = string.Empty;
        /// <summary>1発動あたりの合計上昇値 X = Σ(各カードの per-fire 値)</summary>
        public double PerFire { get; set; }
        /// <summary>per-fire 値のカード別内訳 (降順)。表示の (a+b+c) 用</summary>
        public List<double> Parts { get; set; } = new();
        /// <summary>発動回数 (N)。行動を取っていない場合は 0 (×0回として表示)</summary>
        public int Fires { get; set; }
        /// <summary>上限回数。max_count が行動回数を実際に下回って効いている場合のみ非null (「上限N回」表記用)</summary>
        public int? MaxCount { get; set; }
        /// <summary>合計寄与 (権威値) = Σ(各カードの per-fire × 実効発動回数)</summary>
        public double Total { get; set; }
    }

    /// <summary>
    /// overflow罰則オプション。指定された場合、合計overflow が Threshold を超えた時のみ
    /// × 2 罰則を適用 (cap を大幅に超過するピックを抑制し、別属性カードへの差し替えを誘導)。
    /// null の場合は罰則無し。
    /// </summary>
    public class OverflowPenaltyConfig
    {
        public int Threshold { get; set; }
    }
}
