using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    /// <summary>
    /// スケジュール方式 (初レジェンド/NIA) で、ユーザが各レッスン週に指定した属性を
    /// week→stat の辞書で返す。turnChoices 未指定 (自動ピックモード) では null を返し、
    /// 呼び出し側は従来の配分回数ベース近似にフォールバックする。
    /// </summary>
    private static Dictionary<int, string>? LessonStatByWeek(List<TurnChoice>? turnChoices)
    {
        if (turnChoices == null) return null;
        var map = new Dictionary<int, string>();
        foreach (var tc in turnChoices)
        {
            string? stat = tc.ChosenAction switch
            {
                ActionType.VoLesson => "vo",
                ActionType.DaLesson => "da",
                ActionType.ViLesson => "vi",
                _ => null,
            };
            if (stat != null) map[tc.Week] = stat;
        }
        return map;
    }

    /// <summary>
    /// カード無しのベースステータス推定（レッスン＋授業＋イベント等の基礎値）
    /// </summary>
    private StatusValues EstimateBaseStats(TrainingPlan plan, Dictionary<string, int> lessonAllocation, List<TurnChoice>? turnChoices = null)
    {
        int vo = 0, da = 0, vi = 0;

        // レッスンのSPパーフェクト基礎値を加算
        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        var choiceByWeek = LessonStatByWeek(turnChoices);
        if (choiceByWeek != null)
        {
            // スケジュール方式: ユーザが各週に指定した属性をそのまま使う。配分回数ベースの
            // 「多い属性を高値週へ」近似だと DaDaDaDaVi 指定が ViDaDaDaDa 扱いになり、
            // パラボ土台が実際の踏み順と食い違う。
            foreach (var w in lessonWeeks)
            {
                if (!choiceByWeek.TryGetValue(w.Week, out var stat)) continue;
                var lesson = w.GetLesson(stat);
                if (lesson != null)
                {
                    vo += lesson.SpBonus.Vo;
                    da += lesson.SpBonus.Da;
                    vi += lesson.SpBonus.Vi;
                }
            }
        }
        else
        {
            // 自動ピックモード: 各属性のレッスン回数分、後ろの週(高い値)から割り当て (近似)
            var weekQueue = new Queue<WeekSchedule>(lessonWeeks.OrderByDescending(w => w.Week));
            foreach (var stat in lessonAllocation.OrderByDescending(kv => kv.Value))
            {
                int count = stat.Value;
                for (int i = 0; i < count && weekQueue.Count > 0; i++)
                {
                    var w = weekQueue.Dequeue();
                    var lesson = w.GetLesson(stat.Key);
                    if (lesson != null)
                    {
                        vo += lesson.SpBonus.Vo;
                        da += lesson.SpBonus.Da;
                        vi += lesson.SpBonus.Vi;
                    }
                }
            }
        }

        // 授業の基礎値（メイン属性に全額配分と仮定）
        foreach (var week in plan.Schedule)
        {
            if (week.Classes.Count > 0)
            {
                // 最大値の授業を加算
                var bestClass = week.Classes.OrderByDescending(c => c.SpBonus.Total).First();
                vo += bestClass.SpBonus.Vo;
                da += bestClass.SpBonus.Da;
                vi += bestClass.SpBonus.Vi;
            }

            // 固定イベント
            if (week.IsFixedEvent && week.StatusGain != null)
            {
                vo += week.StatusGain.Vo;
                da += week.StatusGain.Da;
                vi += week.StatusGain.Vi;
            }
        }

        return new StatusValues(vo, da, vi);
    }

    /// <summary>trigger_count_bonus 用、消費側カード1枚分の per-fire 寄与情報</summary>
    public class TriggerBonusContributor
    {
        public string CardId { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public StatusValues PerFire { get; set; } = StatusValues.Zero;
    }

    /// <summary>trigger_count_bonus 用、対象トリガーごとの集計情報</summary>
    public class TriggerBonusEntry
    {
        public StatusValues Total { get; set; } = StatusValues.Zero;
        public List<TriggerBonusContributor> Contributors { get; set; } = new();
    }

    /// <summary>
    /// trigger_count_bonus 効果の単体スコアリングのため、対象トリガーごとに
    /// プール内全ての消費側カードの per-fire ステータスを事前計算する。
    /// </summary>
    private static Dictionary<string, TriggerBonusEntry> ComputeTriggerBonusInfo(
        List<SupportCard> pool,
        Dictionary<string, int>? uncapLevels)
    {
        var targets = new HashSet<string>();
        foreach (var card in pool)
        {
            foreach (var effect in card.Effects)
            {
                if (effect.ValueType == "trigger_count_bonus" && !string.IsNullOrEmpty(effect.TriggerTarget))
                {
                    targets.Add(effect.TriggerTarget);
                }
            }
        }

        var result = new Dictionary<string, TriggerBonusEntry>();
        foreach (var target in targets)
        {
            var candidates = new List<(TriggerBonusContributor c, double total)>();
            foreach (var card in pool)
            {
                int uncap = StatusCalculationService.GetUncapLevel(card, uncapLevels);
                int cVo = 0, cDa = 0, cVi = 0;
                foreach (var effect in card.Effects)
                {
                    if (effect.Trigger != target || effect.ValueType != "flat") continue;
                    int v = (int)Math.Floor(effect.GetValue(uncap));
                    switch (effect.Stat)
                    {
                        case "vo": cVo += v; break;
                        case "da": cDa += v; break;
                        case "vi": cVi += v; break;
                        case "all": cVo += v; cDa += v; cVi += v; break;
                    }
                }
                int total = cVo + cDa + cVi;
                if (total > 0)
                {
                    candidates.Add((new TriggerBonusContributor
                    {
                        CardId = card.Id,
                        CardName = card.Name,
                        PerFire = new StatusValues(cVo, cDa, cVi),
                    }, total));
                }
            }
            candidates.Sort((a, b) => b.total.CompareTo(a.total));
            result[target] = new TriggerBonusEntry
            {
                Total = new StatusValues(
                    candidates.Sum(x => x.c.PerFire.Vo),
                    candidates.Sum(x => x.c.PerFire.Da),
                    candidates.Sum(x => x.c.PerFire.Vi)),
                Contributors = candidates.Select(x => x.c).ToList(),
            };
        }
        return result;
    }

    /// <summary>
    /// カード1枚の属性別寄与を計算
    /// </summary>
    private CardScore CalculateCardContribution(
        SupportCard card,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, TriggerBonusEntry>? triggerBonusInfo = null,
        bool skipTriggerBonusSelfContribution = false)
    {
        int uncap = StatusCalculationService.GetUncapLevel(card, uncapLevels);
        double vo = 0, da = 0, vi = 0;
        double teamBonusTotal = 0;
        var teamBonusContributors = new List<TeamBonusContributor>();
        var breakdowns = new List<EffectBreakdown>();

        foreach (var effect in card.Effects)
        {
            // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
            if (effect.ValueType == "sp_rate") continue;

            // trigger_count_bonus: 自カードは追加でステータスを得ないが、他カードのトリガー発火回数を増やす
            if (effect.ValueType == "trigger_count_bonus")
            {
                var target = effect.TriggerTarget;
                if (string.IsNullOrEmpty(target)) continue;
                double perScale = effect.GetValue(uncap);
                int scaleCount = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? triggerCounts.GetValueOrDefault(effect.ScalesWith)
                    : 1;
                double bonus = perScale * scaleCount;
                if (effect.MaxCount.HasValue) bonus = Math.Min(bonus, effect.MaxCount.Value);
                int bonusFires = (int)Math.Floor(bonus);
                if (bonusFires <= 0) continue;

                if (triggerBonusInfo == null || !triggerBonusInfo.TryGetValue(target, out var entry)) continue;

                // 自カード除外で消費側カードを集計
                double synergyVoSum = 0, synergyDaSum = 0, synergyViSum = 0;
                var contribRows = new List<EffectBreakdown>();
                foreach (var c in entry.Contributors)
                {
                    if (c.CardId == card.Id) continue;
                    int cVo = c.PerFire.Vo * bonusFires;
                    int cDa = c.PerFire.Da * bonusFires;
                    int cVi = c.PerFire.Vi * bonusFires;
                    int cTotal = cVo + cDa + cVi;
                    if (cTotal <= 0) continue;
                    synergyVoSum += cVo;
                    synergyDaSum += cDa;
                    synergyViSum += cVi;
                    var parts = new List<string>();
                    if (c.PerFire.Vo > 0) parts.Add($"Vo+{c.PerFire.Vo}");
                    if (c.PerFire.Da > 0) parts.Add($"Da+{c.PerFire.Da}");
                    if (c.PerFire.Vi > 0) parts.Add($"Vi+{c.PerFire.Vi}");
                    var perFireDesc = string.Join("/", parts);
                    var mainStat = (cVo >= cDa && cVo >= cVi) ? "vo" : (cDa >= cVi ? "da" : "vi");
                    contribRows.Add(new EffectBreakdown
                    {
                        Reason = $"  ↳ {c.CardName} ({perFireDesc}/回)",
                        Stat = mainStat,
                        Value = Math.Round((double)cTotal, 1),
                    });
                    teamBonusContributors.Add(new TeamBonusContributor
                    {
                        CardName = c.CardName,
                        Value = cTotal,
                    });
                }
                if (contribRows.Count == 0) continue;

                teamBonusTotal += synergyVoSum + synergyDaSum + synergyViSum;
                if (!skipTriggerBonusSelfContribution)
                {
                    vo += synergyVoSum;
                    da += synergyDaSum;
                    vi += synergyViSum;
                }

                var targetName = TriggerDisplayName(target);
                var formula = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? $"{TriggerDisplayName(effect.ScalesWith)}×{scaleCount} × {perScale}"
                    : $"×{perScale}";
                var headerSuffix = skipTriggerBonusSelfContribution ? " → 他カードへ寄与" : "";
                breakdowns.Add(new EffectBreakdown
                {
                    Reason = $"[アイテム] {targetName}+{bonusFires}回 ({formula}){headerSuffix}",
                    Stat = "all",
                    Value = 0,
                });
                breakdowns.AddRange(contribRows);
                continue;
            }

            if (effect.ValueType == "para_bonus")
            {
                // パラボは該当属性のレッスン上昇値にのみ適用
                double pct = effect.GetValue(uncap) / 100.0;
                double bonus = 0;
                switch (effect.Stat)
                {
                    case "vo": bonus = lessonStatTotals.Vo * pct; vo += bonus; break;
                    case "da": bonus = lessonStatTotals.Da * pct; da += bonus; break;
                    case "vi": bonus = lessonStatTotals.Vi * pct; vi += bonus; break;
                    case "all":
                        double bVo = lessonStatTotals.Vo * pct;
                        double bDa = lessonStatTotals.Da * pct;
                        double bVi = lessonStatTotals.Vi * pct;
                        vo += bVo; da += bDa; vi += bVi;
                        bonus = bVo + bDa + bVi;
                        break;
                }

                if (Math.Abs(bonus) < 0.01) continue;

                var reason = $"パラボ({effect.Stat.ToUpper()})+{effect.GetValue(uncap)}%";
                breakdowns.Add(new EffectBreakdown
                {
                    Reason = reason,
                    Stat = effect.Stat,
                    Value = Math.Round(bonus, 1)
                });
                continue;
            }

            double value = effect.ValueType switch
            {
                "flat" => CalculateFlatValue(effect, triggerCounts, uncap, card),
                _ => 0
            };

            if (Math.Abs(value) < 0.01) continue;

            // 内訳の理由テキスト生成
            var reason2 = BuildReasonText(effect, triggerCounts, uncap, card);

            switch (effect.Stat)
            {
                case "vo": vo += value; break;
                case "da": da += value; break;
                case "vi": vi += value; break;
                case "all":
                    vo += value / 3.0;
                    da += value / 3.0;
                    vi += value / 3.0;
                    break;
                default:
                    vo += value / 3.0;
                    da += value / 3.0;
                    vi += value / 3.0;
                    break;
            }

            breakdowns.Add(new EffectBreakdown
            {
                Reason = reason2,
                Stat = effect.Stat,
                Value = Math.Round(value, 1)
            });
        }

        int iVo = (int)Math.Floor(vo);
        int iDa = (int)Math.Floor(da);
        int iVi = (int)Math.Floor(vi);

        return new CardScore
        {
            Card = card,
            RawVo = iVo,
            RawDa = iDa,
            RawVi = iVi,
            TeamBonusTotal = (int)Math.Floor(teamBonusTotal),
            TeamBonusContributors = teamBonusContributors,
            TotalValue = iVo + iDa + iVi,
            Breakdowns = breakdowns,
            UncapLevel = uncap
        };
    }

    private Dictionary<string, int> CountTriggers(
        TrainingPlan plan,
        Dictionary<string, int> lessonAllocation,
        List<string> mainStats,
        List<TurnChoice>? turnChoices = null)
    {
        var counts = new Dictionary<string, int>();

        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        int totalLessons = lessonAllocation.Values.Sum();
        counts["sp_end"] = Math.Min(totalLessons, lessonWeeks.Count);
        counts["lesson_end"] = counts["sp_end"];

        // 属性別SP終了・レッスン終了トリガー
        foreach (var kvp in lessonAllocation)
        {
            if (kvp.Value <= 0) continue;
            counts[$"{kvp.Key}_sp_end"] = kvp.Value;       // vo_sp_end, da_sp_end, vi_sp_end
            counts[$"{kvp.Key}_lesson_end"] = kvp.Value;    // vo_lesson_end, da_lesson_end, vi_lesson_end
        }

        // 試験イベント数はスケジュールから確定
        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent)
                counts["exam_end"] = counts.GetValueOrDefault("exam_end") + 1;
        }

        // HIFモード等、ユーザがターン選択を明示している場合は実選択ベースで集計する。
        // available_actions の優先度ベースだと「Day を 活動支給→お出かけ に変えても活動支給回数が減らない」
        // という不整合が起きるため。
        if (turnChoices != null)
        {
            foreach (var tc in turnChoices)
            {
                switch (tc.ChosenAction)
                {
                    case ActionType.VoLesson:
                    case ActionType.DaLesson:
                    case ActionType.ViLesson:
                        break;
                    case ActionType.VoClass:
                    case ActionType.DaClass:
                    case ActionType.ViClass:
                        counts["class_end"] = counts.GetValueOrDefault("class_end") + 1;
                        break;
                    case ActionType.Outing:
                        counts["outing_end"] = counts.GetValueOrDefault("outing_end") + 1;
                        break;
                    case ActionType.Consultation:
                        counts["consultation"] = counts.GetValueOrDefault("consultation") + 1;
                        break;
                    case ActionType.ActivitySupply:
                        counts["activity_supply"] = counts.GetValueOrDefault("activity_supply") + 1;
                        break;
                    case ActionType.SpecialTraining:
                        counts["special_training"] = counts.GetValueOrDefault("special_training") + 1;
                        break;
                    case ActionType.Rest:
                        counts["rest"] = counts.GetValueOrDefault("rest") + 1;
                        break;
                }
            }
            return counts;
        }

        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent) continue;
            if (week.Lessons.Count > 0) continue;

            var actions = week.AvailableActions;
            if (actions.Contains("activity_supply"))
                counts["activity_supply"] = counts.GetValueOrDefault("activity_supply") + 1;
            else if (actions.Contains("outing"))
                counts["outing_end"] = counts.GetValueOrDefault("outing_end") + 1;
            else if (actions.Contains("consultation"))
                counts["consultation"] = counts.GetValueOrDefault("consultation") + 1;
            else if (actions.Contains("special_training"))
                counts["special_training"] = counts.GetValueOrDefault("special_training") + 1;
            else if (actions.Contains("vo_class") || actions.Contains("da_class") || actions.Contains("vi_class"))
                counts["class_end"] = counts.GetValueOrDefault("class_end") + 1;
        }

        return counts;
    }

    /// <summary>
    /// レッスン配分に基づいて、全レッスンのSpBonusを属性別に合計する。
    /// パラメータボーナスの属性別寄与計算に使用。
    /// </summary>
    private StatusValues CalculateLessonStatTotals(TrainingPlan plan, Dictionary<string, int> lessonAllocation, List<TurnChoice>? turnChoices = null)
    {
        int vo = 0, da = 0, vi = 0;

        var lessonWeeks = plan.Schedule
            .Where(w => w.Lessons.Count > 0)
            .OrderByDescending(w => w.Week)
            .ToList();

        var choiceByWeek = LessonStatByWeek(turnChoices);
        if (choiceByWeek != null)
        {
            // スケジュール方式: ユーザが各週に指定した属性をそのまま使う (踏み順を保持)。
            foreach (var w in lessonWeeks)
            {
                if (!choiceByWeek.TryGetValue(w.Week, out var stat)) continue;
                var lesson = w.GetLesson(stat);
                if (lesson != null)
                {
                    vo += lesson.SpBonus.Vo;
                    da += lesson.SpBonus.Da;
                    vi += lesson.SpBonus.Vi;
                }
            }
        }
        else
        {
            // 自動ピックモード: 配分回数ベースの近似 (多い属性を高値週へ)
            var weekQueue = new Queue<WeekSchedule>(lessonWeeks);
            foreach (var stat in lessonAllocation.OrderByDescending(kv => kv.Value))
            {
                int count = stat.Value;
                for (int i = 0; i < count && weekQueue.Count > 0; i++)
                {
                    var w = weekQueue.Dequeue();
                    var lesson = w.GetLesson(stat.Key);
                    if (lesson != null)
                    {
                        vo += lesson.SpBonus.Vo;
                        da += lesson.SpBonus.Da;
                        vi += lesson.SpBonus.Vi;
                    }
                }
            }
        }

        // 試験/オーディション (基礎値+配分値 / 種別理論値) もパラボ対象になるので加算する。
        // HIF選抜試験は base+alloc、NIAオーディションは種別理論値が StatusGain に反映済み
        // (BuildNiaAuditionPlan)。実 Calculate 側 (StatusCalculationService) はどちらにもパラボを
        // 適用するため、スコアリング/内訳のパラボ土台でも両方を含める。
        foreach (var w in plan.Schedule)
        {
            if (w.Type == "audition"
                && (w.HifExamBase != null || w.HifExamDistributed != null
                    || w.NiaAuditionTiers is { Count: > 0 })
                && w.StatusGain != null)
            {
                vo += w.StatusGain.Vo;
                da += w.StatusGain.Da;
                vi += w.StatusGain.Vi;
            }
        }

        return new StatusValues(vo, da, vi);
    }
}
