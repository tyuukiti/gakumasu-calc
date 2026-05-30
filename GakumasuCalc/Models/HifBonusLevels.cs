namespace GakumasuCalc.Models;

/// <summary>
/// HIFモード固有のボーナス (パネル方式の永続強化) のレベル設定。
/// </summary>
public class HifBonusLevels
{
    /// <summary>ボーカル上昇 (Lv1-5): flat +20×Lv, paraBonus +2%×Lv</summary>
    public int VoUpLevel { get; set; } = 5;
    /// <summary>ダンス上昇 (Lv1-5)</summary>
    public int DaUpLevel { get; set; } = 5;
    /// <summary>ビジュアル上昇 (Lv1-5)</summary>
    public int ViUpLevel { get; set; } = 5;
    /// <summary>SPレッスン発生率増加 (Lv1-5) (計算非関与)</summary>
    public int SpRateLevel { get; set; } = 5;
    /// <summary>試験前体力回復% (Lv1-6) (計算非関与)</summary>
    public int HpRecoveryLevel { get; set; } = 6;
    /// <summary>【本戦】パラメータ上限増加 (Lv1-6)</summary>
    public int FinalStatLimitLevel { get; set; } = 6;
    /// <summary>【選抜試験】初期Pポイント (Lv1-6) (計算非関与)</summary>
    public int PreExamPpLevel { get; set; } = 6;
    /// <summary>【本戦】初期Pポイント (Lv1-6) (計算非関与)</summary>
    public int FinalPpLevel { get; set; } = 6;
    /// <summary>相談スキルカード割引 (Lv1-6) (計算非関与)</summary>
    public int ConsultationDiscountLevel { get; set; } = 6;

    /// <summary>
    /// MAX大幅超過時のカード再抽選オプション。
    /// true の場合、合計overflow が OverflowPenaltyThreshold を超えた時のみ × 2 罰則を適用。
    /// </summary>
    public bool OverflowPenaltyEnabled { get; set; } = false;

    /// <summary>overflow罰則の閾値 (Vo+Da+Vi のキャップ超過量合計)</summary>
    public int OverflowPenaltyThreshold { get; set; } = HifOverflowPenaltyConstants.Default;
}

/// <summary>overflow罰則の閾値定数。Web版と数値を揃える。</summary>
public static class HifOverflowPenaltyConstants
{
    public const int Min = 50;
    public const int Max = 500;
    public const int Default = 100;
}

public class HifBonusLevelsFile
{
    public HifBonusLevels Levels { get; set; } = new();
}

/// <summary>
/// HIFボーナスパネルの効果テーブル。index = level、Lv0 は効果なし。
/// </summary>
public static class HifBonusTables
{
    public static readonly int[] StatUpFlat = { 0, 20, 40, 60, 80, 100 };
    public static readonly int[] StatUpPara = { 0, 2, 4, 6, 8, 10 };
    public static readonly int[] FinalCapBonus = { 0, 50, 80, 110, 140, 170, 200 };
    public static readonly int[] SpRateIncrease = { 0, 1, 2, 3, 4, 5 };
    public static readonly int[] HpRecovery = { 0, 5, 7, 9, 11, 13, 15 };
    public static readonly int[] PpIncrease = { 0, 50, 80, 110, 140, 170, 200 };
    public static readonly int[] ConsultationDiscount = { 0, 5, 10, 15, 20, 25, 30 };

    public static int GetStatUpFlat(int lv) => StatUpFlat[Math.Max(0, Math.Min(lv, 5))];
    public static int GetStatUpPara(int lv) => StatUpPara[Math.Max(0, Math.Min(lv, 5))];
    public static int GetFinalCapBonus(int lv) => FinalCapBonus[Math.Max(0, Math.Min(lv, 6))];
}
