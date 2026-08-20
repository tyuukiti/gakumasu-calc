using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    private void ExecuteCopyResult()
    {
        if (Result == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Vo: {ResultVo:N0}  Da: {ResultDa:N0}  Vi: {ResultVi:N0}");
        sb.AppendLine($"合計: {ResultTotal:N0}");

        if (SelectedPattern != null)
        {
            sb.AppendLine();
            sb.AppendLine($"[{SelectedPattern.Label}]");
            foreach (var card in SelectedPattern.Cards)
            {
                var spMark = card.HasSpRate ? " [SP]" : "";
                sb.AppendLine($"  {card.CardTypeDisplay} {card.CardName}{spMark} ({card.StatValue:N0})");
            }
        }

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>
    /// 現在の設定・選択編成・計算結果を平文でクリップボードへコピーする（問題報告用）。
    /// Web版 services/diagnostics.ts の buildDiagnosticReport と同じ書式に揃える。
    /// </summary>
    private void ExecuteCopyDiagnostic()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 学マス計算ツール 診断情報 ===");

        // --- 設定 ---
        sb.AppendLine();
        sb.AppendLine("[設定]");
        sb.AppendLine(_selectedPlan != null
            ? $"プラン: {_selectedPlan.Name} ({_selectedPlan.Id})"
            : "プラン: (未選択)");
        sb.AppendLine($"プランタイプ: {PlanTypeLabel(SelectedPlanType)}");
        sb.AppendLine($"ロール: Vo={VoRole} / Da={DaRole} / Vi={ViRole}");
        sb.AppendLine($"SP回数: Vo={VoSpCount} / Da={DaSpCount} / Vi={ViSpCount}");
        sb.AppendLine($"所持カードのみ: {(OwnedOnly ? "ON" : "OFF")}");
        sb.AppendLine($"コンテストモード: {(ContestMode ? "ON" : "OFF")}");
        sb.AppendLine(_selectedCharacter != null
            ? $"キャラ: {_selectedCharacter.Name} / 3凸ボーナス: {(_uncap3BonusEnabled ? "ON" : "OFF")}"
            : "キャラ: (なし)");

        if (_selectedEventTemplate != null)
            sb.AppendLine($"テンプレート: {_selectedEventTemplate.Name}");

        if (RequiredCards.Count > 0)
            sb.AppendLine($"必須カード: {string.Join(", ", RequiredCards.Select(c => $"{c.Name} ({c.Id})"))}");

        AppendEventCounts(sb);
        AppendMemory(sb);
        AppendSelectedPattern(sb);
        AppendResult(sb);

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>
    /// HIFタブ用の診断情報をコピーする。結果・パターンは MainViewModel と共有しつつ、
    /// HIF固有 (ボーナスLv / overflow罰則 / スケジュール) を HifVm から追記する。
    /// Web版 buildHifDiagnosticReport と書式を揃える。
    /// </summary>
    private void ExecuteCopyHifDiagnostic()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 学マス計算ツール 診断情報 (HIF) ===");

        // --- 設定 ---
        sb.AppendLine();
        sb.AppendLine("[設定]");
        sb.AppendLine("モード: HIF (Hatsuboshi IDOL FESTIVAL)");
        sb.AppendLine($"プランタイプ: {PlanTypeLabel(SelectedPlanType)}");
        sb.AppendLine($"SP回数: Vo={VoSpCount} / Da={DaSpCount} / Vi={ViSpCount}");
        sb.AppendLine($"所持カードのみ: {(OwnedOnly ? "ON" : "OFF")}");
        sb.AppendLine($"コンテストモード: {(ContestMode ? "ON" : "OFF")}");
        sb.AppendLine(_selectedCharacter != null
            ? $"キャラ: {_selectedCharacter.Name} / 3凸ボーナス: {(_uncap3BonusEnabled ? "ON" : "OFF")}"
            : "キャラ: (なし)");

        if (_selectedHifEventTemplate != null)
            sb.AppendLine($"テンプレート: {_selectedHifEventTemplate.Name}");

        if (RequiredCards.Count > 0)
            sb.AppendLine($"必須カード: {string.Join(", ", RequiredCards.Select(c => $"{c.Name} ({c.Id})"))}");

        // --- HIFボーナス ---
        var bl = HifVm.BonusLevels;
        sb.AppendLine();
        sb.AppendLine("[HIFボーナス] (Lv)");
        sb.AppendLine($"Vo上昇={bl.VoUpLevel} / Da上昇={bl.DaUpLevel} / Vi上昇={bl.ViUpLevel}");
        sb.AppendLine($"本戦パラメータ上限増加={bl.FinalStatLimitLevel}");
        if (bl.OverflowPenaltyEnabled)
            sb.AppendLine($"MAX超過再抽選: ON (閾値 {bl.OverflowPenaltyThreshold})");

        // --- HIFスケジュール ---
        var schedLines = new List<string>();
        foreach (var item in HifVm.ScheduleItems)
        {
            if (item.IsPublicLesson && item.MainStat != null && item.SubStat != null)
            {
                schedLines.Add($"W{item.Week}: {item.MainStat.ToUpper()}レッスン（サブ{item.SubStat.ToUpper()}）");
            }
            else if (item.IsExam && (item.ExamVoAlloc > 0 || item.ExamDaAlloc > 0 || item.ExamViAlloc > 0))
            {
                schedLines.Add($"W{item.Week} 試験配分: Vo{item.ExamVoAlloc}/Da{item.ExamDaAlloc}/Vi{item.ExamViAlloc}");
            }
            else if (!item.IsFixed && item.SelectedAction != null)
            {
                schedLines.Add($"W{item.Week}: {HifActionLabel(item.SelectedAction)}");
            }
        }
        if (schedLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[HIFスケジュール]");
            foreach (var line in schedLines)
                sb.AppendLine(line);
        }

        AppendEventCounts(sb);
        AppendMemory(sb);
        AppendSelectedPattern(sb);
        AppendResult(sb);

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    /// <summary>イベントカウント (0以外のみ) を sb に追記。通常/HIF で共有。</summary>
    private void AppendEventCounts(System.Text.StringBuilder sb)
    {
        var counts = new (string Label, int Value)[]
        {
            ("Pドリンク獲得", PDrinkAcquire),
            ("Pアイテム獲得", PItemAcquire),
            ("スキルカード獲得", SkillAcquire),
            ("スキル(SSR)獲得", SkillSsrAcquire),
            ("スキル強化", SkillEnhance),
            ("スキル削除", SkillDelete),
            ("スキルカスタム", SkillCustom),
            ("スキルチェンジ", SkillChange),
            ("アクティブ強化", ActiveEnhance),
            ("アクティブ削除", ActiveDelete),
            ("メンタル獲得", MentalAcquire),
            ("メンタル強化", MentalEnhance),
            ("メンタル削除", MentalDelete),
            ("アクティブ獲得", ActiveAcquire),
            ("好調カード獲得", GoodConditionAcquire),
            ("集中カード獲得", ConcentrateAcquire),
            ("元気カード獲得", GenkiAcquire),
            ("好印象カード獲得", GoodImpressionAcquire),
            ("やる気カード獲得", MotivationAcquire),
            ("温存カード獲得", ConserveAcquire),
            ("全力カード獲得", FullpowerAcquire),
            ("強気カード獲得", AggressiveAcquire),
            ("相談Pドリンク交換", ConsultationDrink),
        };
        var activeCounts = counts.Where(c => c.Value > 0).ToList();
        if (activeCounts.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("[イベントカウント]");
        foreach (var (label, value) in activeCounts)
            sb.AppendLine($"{label}: {value}");
    }

    /// <summary>持ち込みメモリー (空でないもの) を sb に追記。</summary>
    private void AppendMemory(System.Text.StringBuilder sb)
    {
        var memoryLines = new List<string>();
        for (int i = 0; i < MemoryBonuses.Count; i++)
        {
            var m = MemoryBonuses[i];
            if (m.IsEmpty) continue;
            var parts = new List<string>();
            if (m.VoValue != 0) parts.Add($"Vo {FormatMemoryValue(m.VoValue, m.VoType)}");
            if (m.DaValue != 0) parts.Add($"Da {FormatMemoryValue(m.DaValue, m.DaType)}");
            if (m.ViValue != 0) parts.Add($"Vi {FormatMemoryValue(m.ViValue, m.ViType)}");
            if (parts.Count > 0)
                memoryLines.Add($"メモリー{i + 1}: {string.Join(" / ", parts)}");
        }
        if (memoryLines.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("[持ち込みメモリー]");
        foreach (var line in memoryLines)
            sb.AppendLine(line);
    }

    /// <summary>選択編成 (パターン名 + カード一覧) を sb に追記。</summary>
    private void AppendSelectedPattern(System.Text.StringBuilder sb)
    {
        if (_selectedPattern == null
            || _selectedPattern.Index < 0 || _selectedPattern.Index >= _deckResults.Count)
            return;
        var pattern = _deckResults[_selectedPattern.Index];
        sb.AppendLine();
        sb.AppendLine($"[選択編成] パターン: {pattern.Label}");
        int idx = 1;
        foreach (var cs in pattern.SelectedCards)
        {
            var tags = new List<string>();
            if (cs.IsRental) tags.Add("レンタル");
            if (cs.IsRequired) tags.Add("必須");
            var tagStr = tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "";
            sb.AppendLine($"{idx}. {cs.Card.Name} ({cs.Card.Id}){tagStr}");
            idx++;
        }
    }

    /// <summary>計算結果 (cap適用後 / 超過時はcap前も) を sb に追記。</summary>
    private void AppendResult(System.Text.StringBuilder sb)
    {
        if (Result == null)
        {
            sb.AppendLine();
            sb.AppendLine("[計算結果] 未計算");
            return;
        }
        sb.AppendLine();
        sb.AppendLine($"[計算結果] (上限 {StatCap})");
        sb.AppendLine($"Vo: {ResultVo}{(ResultVoOverflow > 0 ? $" (cap前 {ResultVoRaw})" : "")}");
        sb.AppendLine($"Da: {ResultDa}{(ResultDaOverflow > 0 ? $" (cap前 {ResultDaRaw})" : "")}");
        sb.AppendLine($"Vi: {ResultVi}{(ResultViOverflow > 0 ? $" (cap前 {ResultViRaw})" : "")}");
        sb.AppendLine($"合計: {ResultTotal}");
    }

    private static string PlanTypeLabel(string planType) => planType switch
    {
        "sense" => "センス",
        "logic" => "ロジック",
        "anomaly" => "アノマリー",
        _ => planType,
    };

    private static string HifActionLabel(string action) => action switch
    {
        "outing" => "お出かけ",
        "consultation" => "相談",
        "activity_supply" => "活動支給",
        "special_training" => "特別指導",
        "vo_class" => "Vo授業",
        "da_class" => "Da授業",
        "vi_class" => "Vi授業",
        "vo_lesson" => "Voレッスン",
        "da_lesson" => "Daレッスン",
        "vi_lesson" => "Viレッスン",
        _ => action,
    };

    private static string FormatMemoryValue(double value, MemoryBonusType type)
    {
        var sign = value > 0 ? "+" : "";
        var suffix = type == MemoryBonusType.ParaBonus ? "%(パラボ)" : "";
        return $"{sign}{value:0.#}{suffix}";
    }
}
