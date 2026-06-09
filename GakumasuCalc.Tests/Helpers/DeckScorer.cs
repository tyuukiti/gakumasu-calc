using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>
/// デッキ採点オラクル。最適化器の内部スコアではなく「実 Calculate の cap 後合計」を返す。
/// ターン選択は最適化器が内部評価に使う BuildTurnChoices に揃える (TS 版 scoreDeck と同設計)。
/// StatusValues は int なので合計も整数 → 厳密比較できる。
/// </summary>
public static class DeckScorer
{
    private static readonly StatusCalculationService Calc = new();

    public static int ScoreDeck(
        TrainingPlan plan,
        List<SupportCard> cards,
        List<string> mainStats,
        Dictionary<string, int>? uncapLevels = null,
        List<TurnChoice>? turnChoices = null,
        AdditionalCounts? additionalCounts = null)
    {
        var tc = turnChoices ?? CardScoringService.BuildTurnChoices(plan, mainStats);
        var result = Calc.Calculate(plan, cards, tc, uncapLevels, additionalCounts);
        var cap = plan.StatusLimit;
        var fs = result.FinalStatus;
        return Math.Min(fs.Vo, cap) + Math.Min(fs.Da, cap) + Math.Min(fs.Vi, cap);
    }
}
