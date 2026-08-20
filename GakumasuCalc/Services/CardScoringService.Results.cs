using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    /// <summary>
    /// プランとメイン属性からターン選択を生成する。
    /// </summary>
    internal static List<TurnChoice> BuildTurnChoices(TrainingPlan plan, List<string> mainStats)
    {
        var choices = new List<TurnChoice>();
        var subStat = new[] { "vo", "da", "vi" }.First(s => !mainStats.Contains(s));

        static ActionType LessonAction(string stat) => stat switch
        {
            "vo" => ActionType.VoLesson,
            "da" => ActionType.DaLesson,
            _ => ActionType.ViLesson
        };
        static ActionType ClassAction(string stat) => stat switch
        {
            "vo" => ActionType.VoClass,
            "da" => ActionType.DaClass,
            _ => ActionType.ViClass
        };

        var main1Action = LessonAction(mainStats[0]);
        var main2Action = mainStats.Count > 1 ? LessonAction(mainStats[1]) : main1Action;
        var subClassAction = ClassAction(subStat);

        int midExamWeek = plan.Schedule
            .Where(w => w.IsFixedEvent && w.EventName == "中間試験")
            .Select(w => w.Week)
            .FirstOrDefault();
        if (midExamWeek == 0) midExamWeek = 10;

        var lessonWeeks = plan.Schedule
            .Where(w => !w.IsFixedEvent && w.Lessons.Count > 0)
            .OrderBy(w => w.Week)
            .ToList();

        // 中間前: 交互
        bool toggle = false;
        foreach (var w in lessonWeeks.Where(w => w.Week < midExamWeek))
        {
            choices.Add(new TurnChoice { Week = w.Week, ChosenAction = toggle ? main2Action : main1Action });
            toggle = !toggle;
        }

        // 中間後: メイン1:メイン2 = 2:1
        int afterCount = 0;
        foreach (var w in lessonWeeks.Where(w => w.Week > midExamWeek))
        {
            choices.Add(new TurnChoice { Week = w.Week, ChosenAction = (afterCount % 3 == 1) ? main2Action : main1Action });
            afterCount++;
        }

        // 非レッスン週
        foreach (var w in plan.Schedule)
        {
            if (w.IsFixedEvent || w.Lessons.Count > 0) continue;
            var actions = w.AvailableActions;

            bool hasClass = actions.Any(a => a.Contains("class"));
            if (hasClass)
            {
                var subClassStr = subStat + "_class";
                if (actions.Contains(subClassStr))
                    choices.Add(new TurnChoice { Week = w.Week, ChosenAction = subClassAction });
                else
                {
                    var mainClassStr = mainStats[0] + "_class";
                    if (actions.Contains(mainClassStr))
                        choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ClassAction(mainStats[0]) });
                }
            }
            else if (actions.Contains("activity_supply"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.ActivitySupply });
            else if (actions.Contains("outing"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.Outing });
            else if (actions.Contains("consultation"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.Consultation });
            else if (actions.Contains("special_training"))
                choices.Add(new TurnChoice { Week = w.Week, ChosenAction = ActionType.SpecialTraining });
        }

        return choices;
    }

    /// <summary>
    /// キャップを考慮して最も有効なカードを選択する。
    /// 各候補について、追加した場合のキャップ後合計の増分が最大のものを選ぶ。
    /// </summary>
    private CardScore? SelectBestCard(
        List<CardScore> candidates,
        HashSet<string> usedIds,
        int currentVo, int currentDa, int currentVi,
        int statCap = DEFAULT_STAT_CAP,
        Character? character = null,
        OverflowPenaltyConfig? overflowPenalty = null)
    {
        CardScore? best = null;
        int bestGain = int.MinValue;

        // キャラの para_bonus はカード貢献にも乗る (calculate 時)
        double voMul = 1.0 + (character?.ParaBonus.Vo ?? 0) / 100.0;
        double daMul = 1.0 + (character?.ParaBonus.Da ?? 0) / 100.0;
        double viMul = 1.0 + (character?.ParaBonus.Vi ?? 0) / 100.0;

        // overflow罰則を適用するなら現在の overflow を計算
        int overflowCurrent = overflowPenalty != null
            ? Math.Max(0, currentVo - statCap) + Math.Max(0, currentDa - statCap) + Math.Max(0, currentVi - statCap)
            : 0;

        foreach (var cs in candidates)
        {
            if (usedIds.Contains(cs.Card.Id)) continue;

            int rawNewVo = currentVo + (int)(cs.RawVo * voMul);
            int rawNewDa = currentDa + (int)(cs.RawDa * daMul);
            int rawNewVi = currentVi + (int)(cs.RawVi * viMul);

            // キャップ適用後の実効増分 (合計stat)
            int cappedNewSum = Math.Min(rawNewVo, statCap) + Math.Min(rawNewDa, statCap) + Math.Min(rawNewVi, statCap);
            int cappedCurrentSum = Math.Min(currentVo, statCap) + Math.Min(currentDa, statCap) + Math.Min(currentVi, statCap);
            int gain = cappedNewSum - cappedCurrentSum;

            // overflow罰則: ピック後の合計overflowが閾値を超える場合のみ、追加overflow分を × 2 罰則
            if (overflowPenalty != null)
            {
                int overflowNew =
                    Math.Max(0, rawNewVo - statCap) + Math.Max(0, rawNewDa - statCap) + Math.Max(0, rawNewVi - statCap);
                if (overflowNew > overflowPenalty.Threshold)
                {
                    int newOverflow = Math.Max(0, overflowNew - overflowCurrent);
                    gain -= newOverflow * 2;
                }
            }

            if (gain > bestGain)
            {
                bestGain = gain;
                best = cs;
            }
        }

        return best;
    }

    /// <summary>
    /// カードリスト＋レンタル1枚のキャップ適用後の合計ステータスを算出する。
    /// スワップ検証用。
    /// </summary>
    private int CalculateCappedTotal(StatusValues baseStats, List<CardScore> owned, CardScore? rental, int statCap)
    {
        int vo = baseStats.Vo, da = baseStats.Da, vi = baseStats.Vi;
        foreach (var cs in owned)
        {
            vo += cs.RawVo;
            da += cs.RawDa;
            vi += cs.RawVi;
        }
        if (rental != null)
        {
            vo += rental.RawVo;
            da += rental.RawDa;
            vi += rental.RawVi;
        }
        return Math.Min(vo, statCap) + Math.Min(da, statCap) + Math.Min(vi, statCap);
    }

    /// <summary>
    /// 選択完了後、キャップ適用後の実効TotalValueを再計算する。
    /// </summary>
    private void RecalculateWithCap(List<CardScore> selected, StatusValues baseStats, int statCap = DEFAULT_STAT_CAP)
    {
        // カード無しのベースステータスから順に積み上げてキャップ適用
        int accVo = baseStats.Vo, accDa = baseStats.Da, accVi = baseStats.Vi;

        foreach (var cs in selected)
        {
            int prevTotal = Math.Min(accVo, statCap) + Math.Min(accDa, statCap) + Math.Min(accVi, statCap);

            accVo += cs.RawVo;
            accDa += cs.RawDa;
            accVi += cs.RawVi;

            int newTotal = Math.Min(accVo, statCap) + Math.Min(accDa, statCap) + Math.Min(accVi, statCap);

            cs.TotalValue = newTotal - prevTotal;
        }
    }

    /// <summary>
    /// デッキ確定後の deck-aware 再計算。
    /// - producer の trigger_count_bonus 効果による消費側カードへのバフ分を triggerCounts に加算
    /// - producer 側では trigger_count_bonus を raw_* に加算しない (二重カウント回避)
    /// - team_bonus_total はデッキ内 consumer のみを対象に計算
    /// </summary>
    private Dictionary<string, int> RecomputeBreakdownsDeckAware(
        List<CardScore> selected,
        Dictionary<string, int> baseTriggerCounts,
        Dictionary<string, int> lessonAllocation,
        StatusValues lessonStatTotals,
        Dictionary<string, int>? uncapLevels)
    {
        // レンタル枠は所持凸数に依らず常に4凸として評価する
        var effectiveUncapLevels = uncapLevels != null
            ? new Dictionary<string, int>(uncapLevels)
            : new Dictionary<string, int>();
        foreach (var cs in selected)
        {
            if (cs.IsRental) effectiveUncapLevels[cs.Card.Id] = 4;
        }

        // 1. デッキ内 producer の trigger_count_bonus 集計
        var deckBonuses = new Dictionary<string, int>();
        foreach (var cs in selected)
        {
            int uncap = StatusCalculationService.GetUncapLevel(cs.Card, effectiveUncapLevels);
            foreach (var effect in cs.Card.Effects)
            {
                if (effect.ValueType != "trigger_count_bonus") continue;
                var target = effect.TriggerTarget;
                if (string.IsNullOrEmpty(target)) continue;
                double perScale = effect.GetValue(uncap);
                int scaleCount = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? baseTriggerCounts.GetValueOrDefault(effect.ScalesWith)
                    : 1;
                double bonus = perScale * scaleCount;
                if (effect.MaxCount.HasValue) bonus = Math.Min(bonus, effect.MaxCount.Value);
                int bonusFires = (int)Math.Floor(bonus);
                if (bonusFires > 0)
                {
                    deckBonuses[target] = deckBonuses.GetValueOrDefault(target) + bonusFires;
                }
            }
        }
        if (deckBonuses.Count == 0) return baseTriggerCounts;

        // 2. adjustedCounts = base + producer-derived bonus
        var adjustedCounts = new Dictionary<string, int>(baseTriggerCounts);
        foreach (var kvp in deckBonuses)
        {
            adjustedCounts[kvp.Key] = adjustedCounts.GetValueOrDefault(kvp.Key) + kvp.Value;
        }

        // 3. デッキ内カードのみで TriggerBonusInfo を計算
        var deckCards = selected.Select(cs => cs.Card).ToList();
        var deckTriggerBonusInfo = ComputeTriggerBonusInfo(deckCards, effectiveUncapLevels);

        // 4. 各 selected card を再計算 (skipTriggerBonusSelfContribution=true)
        for (int i = 0; i < selected.Count; i++)
        {
            var cs = selected[i];
            var recomputed = CalculateCardContribution(
                cs.Card,
                adjustedCounts,
                lessonAllocation,
                lessonStatTotals,
                effectiveUncapLevels,
                deckTriggerBonusInfo,
                skipTriggerBonusSelfContribution: true);
            recomputed.IsRental = cs.IsRental;
            recomputed.IsRequired = cs.IsRequired;
            selected[i] = recomputed;
        }

        return adjustedCounts;
    }

    /// <summary>
    /// アビリティまとめ (行動別) を構築する。
    ///
    /// 選択カードの flat 効果 (trigger != "equip") を (行動トリガー × 属性) でグループ化し、
    /// 発動回数を掛けて合算する。「どの行動を取るとパラメが伸びるか」の比較用。
    /// - 値は CalculateCardContribution / 各カード内訳と同じ生寄与 (cap前・キャラパラボ前)
    /// - 装備 (初期値/SP率)・パラボ・trigger_count_bonus は行動選択で変動しないため除外
    /// - レンタル枠は4凸として評価 (内訳パネルと同じ)
    /// - triggerCounts は RecomputeBreakdownsDeckAware が返す adjustedCounts (trigger_count_bonus 反映済み)
    ///
    /// MaxCount でカードごとに実効発動回数が異なる稀ケースでは、Total は各カードの
    /// 実効回数で正確に合算し、表示の発動回数 N は当該トリガーの発動回数を用いる
    /// (PerFire × N が Total と一致しない場合があるが Total が権威値)。
    /// </summary>
    internal List<AbilitySummaryEntry> BuildAbilitySummary(
        List<CardScore> selected,
        Dictionary<string, int> triggerCounts,
        Dictionary<string, int>? uncapLevels)
    {
        // レンタル枠は内訳パネルと同様に常に4凸として評価する
        var effectiveUncap = uncapLevels != null
            ? new Dictionary<string, int>(uncapLevels)
            : new Dictionary<string, int>();
        foreach (var cs in selected)
        {
            if (cs.IsRental) effectiveUncap[cs.Card.Id] = 4;
        }

        var groups = new Dictionary<string, AbilitySummaryEntry>();
        var capValues = new Dictionary<string, HashSet<int>>();
        var order = new List<string>();

        foreach (var cs in selected)
        {
            int uncap = StatusCalculationService.GetUncapLevel(cs.Card, effectiveUncap);
            foreach (var effect in cs.Card.Effects)
            {
                if (effect.ValueType != "flat") continue;
                if (effect.Trigger == "equip") continue;

                double perFire = effect.GetValue(uncap);
                if (Math.Abs(perFire) < 0.01) continue;

                // 行動を1回も取っていなくても、編成カードの行動アビリティは ×0回 として出す。
                // (triggerFires=0 / effFires=0 を許容。Total は 0 になる)
                int triggerFires = triggerCounts.GetValueOrDefault(effect.Trigger);
                int effFires = effect.MaxCount.HasValue
                    ? Math.Min(triggerFires, effect.MaxCount.Value)
                    : triggerFires;

                string key = $"{effect.Trigger}|{effect.Stat}";
                if (!groups.TryGetValue(key, out var acc))
                {
                    acc = new AbilitySummaryEntry
                    {
                        Trigger = effect.Trigger,
                        TriggerName = TriggerDisplayName(effect.Trigger),
                        Stat = effect.Stat,
                        Fires = triggerFires,
                    };
                    groups[key] = acc;
                    capValues[key] = new HashSet<int>();
                    order.Add(key);
                }
                acc.PerFire += perFire;
                acc.Parts.Add(Math.Round(perFire, 1));
                acc.Total += perFire * effFires;
                // 上限が行動回数を実際に下回って効いている場合のみ「上限N回」を表示
                if (effect.MaxCount.HasValue && triggerFires > effect.MaxCount.Value)
                    capValues[key].Add(effect.MaxCount.Value);
            }
        }

        var entries = order.Select(k => groups[k]).ToList();
        foreach (var k in order)
        {
            var e = groups[k];
            e.PerFire = Math.Round(e.PerFire, 1);
            e.Total = Math.Round(e.Total, 1);
            e.Parts.Sort((a, b) => b.CompareTo(a));
            // 複数カードで上限値が異なる稀ケースは最も厳しい(最小)上限を表示
            e.MaxCount = capValues[k].Count > 0 ? capValues[k].Min() : (int?)null;
        }

        // 同一トリガーをまとめ、グループ合計 (= その行動で得られる総パラメ) の降順に並べる。
        // グループ内は Vo→Da→Vi→All の順 (同じ行動の Vo/Da/Vi がバラけて読みづらいのを防ぐ)。
        var groupTotal = entries.GroupBy(e => e.Trigger).ToDictionary(g => g.Key, g => g.Sum(e => e.Total));
        static int StatRank(string s) => s switch { "vo" => 0, "da" => 1, "vi" => 2, "all" => 3, _ => 4 };
        return entries
            .OrderByDescending(e => groupTotal[e.Trigger])
            .ThenBy(e => e.Trigger, StringComparer.Ordinal)
            .ThenBy(e => StatRank(e.Stat))
            .ToList();
    }

    private string GenerateLabel(Dictionary<string, int> cardTypeSlots, int freeSlots = 0)
    {
        var parts = new List<string>();
        foreach (var kvp in cardTypeSlots.OrderByDescending(k => k.Value))
        {
            if (kvp.Value > 0)
            {
                var name = kvp.Key switch
                {
                    "vo" => "Vocal",
                    "da" => "Dance",
                    "vi" => "Visual",
                    _ => kvp.Key
                };
                parts.Add($"{name} {kvp.Value}");
            }
        }
        if (freeSlots > 0)
            parts.Add($"フリー {freeSlots}");
        return string.Join(" / ", parts) + " 編成";
    }
}
