using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class StatusCalculationService
{
    public CalculationResult Calculate(
        TrainingPlan plan,
        List<SupportCard> selectedCards,
        List<TurnChoice> turnChoices,
        Dictionary<string, int>? uncapLevels = null,
        AdditionalCounts? additionalCounts = null,
        Character? character = null,
        IReadOnlyList<MemoryBonus>? memoryBonuses = null)
    {
        // Step 1: 基礎ステータス（キャラ選択時はキャラの基礎加算を反映）
        var baseStatus = plan.BaseStatus.Clone();
        if (character != null)
            baseStatus = baseStatus.Add(character.BaseStatusBonus);
        // 持ち込みメモリーの実数値加算を基礎値に合流（para_bonus分は週次レッスン処理で合算）
        if (memoryBonuses != null)
            baseStatus = baseStatus.Add(MemoryBonus.SumFlat(memoryBonuses));

        // Step 2: サポートカード装備ボーナス (初期値)
        var supportBonus = CalculateEquipBonus(selectedCards, uncapLevels);

        // Step 3: ターン逐次計算
        var accumulated = StatusValues.Zero;
        var weekDetails = new List<WeekBreakdown>();
        var triggerCounters = new Dictionary<string, int>(); // カードID_トリガー → 発動回数

        foreach (var week in plan.Schedule)
        {
            var turnChoice = turnChoices.FirstOrDefault(tc => tc.Week == week.Week);
            var weekGain = CalculateWeekGain(week, turnChoice, selectedCards, plan, triggerCounters, uncapLevels, character, memoryBonuses);

            accumulated = accumulated.Add(weekGain);

            var actionName = GetActionName(week, turnChoice);
            weekDetails.Add(new WeekBreakdown
            {
                Week = week.Week,
                ActionName = actionName,
                Gain = weekGain
            });
        }

        // Step 3.5: 追加イベントトリガー発火
        // - ユーザ指定の additionalCounts (テンプレート由来)
        // - サポカの trigger_count_bonus 効果による動的加算 (例: ふわふわでもこもこ)
        var mergedAdditional = additionalCounts != null
            ? new Dictionary<string, int>(additionalCounts.ToDictionary())
            : new Dictionary<string, int>();

        var baseTriggerCounts = ComputeBaseTriggerCounts(plan, turnChoices);
        var scalesLookup = new Dictionary<string, int>(baseTriggerCounts);
        foreach (var kvp in mergedAdditional)
        {
            if (kvp.Value > 0)
            {
                scalesLookup[kvp.Key] = scalesLookup.GetValueOrDefault(kvp.Key) + kvp.Value;
            }
        }

        foreach (var card in selectedCards)
        {
            var uncap = GetUncapLevel(card, uncapLevels);
            foreach (var effect in card.Effects)
            {
                if (effect.ValueType != "trigger_count_bonus") continue;
                if (string.IsNullOrEmpty(effect.TriggerTarget)) continue;
                var perScale = effect.GetValue(uncap);
                var scaleCount = !string.IsNullOrEmpty(effect.ScalesWith)
                    ? scalesLookup.GetValueOrDefault(effect.ScalesWith)
                    : 1;
                double bonus = perScale * scaleCount;
                if (effect.MaxCount.HasValue) bonus = Math.Min(bonus, effect.MaxCount.Value);
                int bonusFires = (int)Math.Floor(bonus);
                if (bonusFires <= 0) continue;
                mergedAdditional[effect.TriggerTarget] = mergedAdditional.GetValueOrDefault(effect.TriggerTarget) + bonusFires;
            }
        }

        var additionalGain = FireAdditionalTriggersFromDict(selectedCards, triggerCounters, uncapLevels, mergedAdditional);
        if (additionalGain.Vo != 0 || additionalGain.Da != 0 || additionalGain.Vi != 0)
        {
            accumulated = accumulated.Add(additionalGain);
            weekDetails.Add(new WeekBreakdown
            {
                Week = 99,
                ActionName = "追加イベント効果",
                Gain = additionalGain
            });
        }

        // Step 4: 最終値
        var finalStatus = baseStatus.Add(supportBonus).Add(accumulated);

        return new CalculationResult(finalStatus, baseStatus, supportBonus, accumulated, weekDetails);
    }

    public static int GetUncapLevel(SupportCard card, Dictionary<string, int>? uncapLevels)
    {
        if (uncapLevels != null && uncapLevels.TryGetValue(card.Id, out var level))
            return level;
        return 4; // デフォルト4凸
    }

    private StatusValues CalculateEquipBonus(List<SupportCard> cards, Dictionary<string, int>? uncapLevels)
    {
        var bonus = StatusValues.Zero;
        foreach (var card in cards)
        {
            bonus = bonus.Add(card.GetInitialBonus(GetUncapLevel(card, uncapLevels)));
        }
        return bonus;
    }

    private StatusValues CalculateWeekGain(
        WeekSchedule week,
        TurnChoice? turnChoice,
        List<SupportCard> cards,
        TrainingPlan plan,
        Dictionary<string, int> triggerCounters,
        Dictionary<string, int>? uncapLevels,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses)
    {
        // 固定イベント
        if (week.IsFixedEvent)
        {
            var fixedGain = week.StatusGain?.Clone() ?? StatusValues.Zero;
            // HIFモードの選抜試験(基礎値+配分値)はゲーム内挙動と同じくパラメータボーナスを適用する。
            // NIAオーディション(種別表の理論値=パラボ適用前の基礎値)も同様に適用する。
            bool isHifExam = week.Type == "audition"
                && (week.HifExamBase != null || week.HifExamDistributed != null);
            bool isNiaAudition = week.Type == "audition"
                && week.NiaAuditionTiers is { Count: > 0 };
            if (isHifExam || isNiaAudition)
            {
                fixedGain = ApplyParaBonus(fixedGain, cards, uncapLevels, character, memoryBonuses);
            }
            // 試験・オーディション終了時トリガー
            var examTriggerGain = FireTrigger("exam_end", cards, triggerCounters, uncapLevels);
            return fixedGain.Add(examTriggerGain);
        }

        if (turnChoice == null)
            return StatusValues.Zero;

        var gain = turnChoice.ChosenAction switch
        {
            ActionType.VoLesson => CalculateLessonGain(week, "vo", cards, triggerCounters, uncapLevels, character, memoryBonuses),
            ActionType.DaLesson => CalculateLessonGain(week, "da", cards, triggerCounters, uncapLevels, character, memoryBonuses),
            ActionType.ViLesson => CalculateLessonGain(week, "vi", cards, triggerCounters, uncapLevels, character, memoryBonuses),
            ActionType.VoClass => CalculateClassGain(week, "vo", cards, triggerCounters, uncapLevels),
            ActionType.DaClass => CalculateClassGain(week, "da", cards, triggerCounters, uncapLevels),
            ActionType.ViClass => CalculateClassGain(week, "vi", cards, triggerCounters, uncapLevels),
            ActionType.Outing => CalculateOutingGain(week, cards, triggerCounters, uncapLevels),
            ActionType.Consultation => CalculateConsultationGain(week, cards, triggerCounters, uncapLevels),
            // 休む: ステータス獲得なし(体力回復はモデル外)だが「休む選択時」トリガーは発火する
            ActionType.Rest => FireTrigger("rest", cards, triggerCounters, uncapLevels),
            ActionType.ActivitySupply => CalculateSupplyGain(turnChoice, plan, cards, triggerCounters, uncapLevels),
            ActionType.SpecialTraining => CalculateSpecialTrainingGain(week, cards, triggerCounters, uncapLevels),
            _ => StatusValues.Zero
        };

        return gain;
    }

    /// <summary>
    /// サポカ/キャラ/持ち込みメモリーの para_bonus% を Vo/Da/Vi 別に合算して返す。
    /// </summary>
    private (double Vo, double Da, double Vi) SumParaBonusPercent(
        List<SupportCard> cards,
        Dictionary<string, int>? uncapLevels,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses)
    {
        double vo = 0, da = 0, vi = 0;
        foreach (var card in cards)
        {
            var uncap = GetUncapLevel(card, uncapLevels);
            foreach (var e in card.Effects.Where(e => e.Trigger == "equip" && e.ValueType == "para_bonus"))
            {
                var val = e.GetValue(uncap);
                switch (e.Stat)
                {
                    case "vo": vo += val; break;
                    case "da": da += val; break;
                    case "vi": vi += val; break;
                    case "all":
                        vo += val;
                        da += val;
                        vi += val;
                        break;
                }
            }
        }
        if (character != null)
        {
            vo += character.ParaBonus.Vo;
            da += character.ParaBonus.Da;
            vi += character.ParaBonus.Vi;
        }
        if (memoryBonuses != null)
        {
            var memPara = MemoryBonus.SumParaBonus(memoryBonuses);
            vo += memPara.Vo;
            da += memPara.Da;
            vi += memPara.Vi;
        }
        return (vo, da, vi);
    }

    /// <summary>
    /// 獲得パラメータ raw に para_bonus% を適用 (Math.Floor 切り捨て)。
    /// </summary>
    private StatusValues ApplyParaBonus(
        StatusValues raw,
        List<SupportCard> cards,
        Dictionary<string, int>? uncapLevels,
        Character? character,
        IReadOnlyList<MemoryBonus>? memoryBonuses)
    {
        var (pVo, pDa, pVi) = SumParaBonusPercent(cards, uncapLevels, character, memoryBonuses);
        return new StatusValues(
            (int)Math.Floor(raw.Vo * (1.0 + pVo / 100.0)),
            (int)Math.Floor(raw.Da * (1.0 + pDa / 100.0)),
            (int)Math.Floor(raw.Vi * (1.0 + pVi / 100.0)));
    }

    private StatusValues CalculateLessonGain(
        WeekSchedule week, string lessonType, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels,
        Character? character, IReadOnlyList<MemoryBonus>? memoryBonuses)
    {
        var lesson = week.GetLesson(lessonType);
        if (lesson == null)
            return StatusValues.Zero;

        // 各属性のパラボは該当属性のレッスン上昇値にのみ適用
        var result = ApplyParaBonus(lesson.SpBonus, cards, uncapLevels, character, memoryBonuses);

        // SP終了時トリガー (汎用)
        var spEndGain = FireTrigger("sp_end", cards, triggerCounters, uncapLevels);
        result = result.Add(spEndGain);

        // 属性別SP終了時トリガー (vo_sp_end, da_sp_end, vi_sp_end)
        var statSpEndGain = FireTrigger($"{lessonType}_sp_end", cards, triggerCounters, uncapLevels);
        result = result.Add(statSpEndGain);

        // レッスン終了時トリガー (汎用)
        var lessonEndGain = FireTrigger("lesson_end", cards, triggerCounters, uncapLevels);

        // 属性別レッスン終了時トリガー (vo_lesson_end, da_lesson_end, vi_lesson_end)
        var statLessonEndGain = FireTrigger($"{lessonType}_lesson_end", cards, triggerCounters, uncapLevels);
        lessonEndGain = lessonEndGain.Add(statLessonEndGain);
        result = result.Add(lessonEndGain);

        return result;
    }

    private StatusValues CalculateClassGain(
        WeekSchedule week, string classType, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        var classConfig = week.GetClass(classType);
        var baseGain = classConfig?.SpBonus.Clone() ?? week.ClassEffect?.Clone() ?? StatusValues.Zero;

        // 授業終了時トリガー
        var classEndGain = FireTrigger("class_end", cards, triggerCounters, uncapLevels);
        return baseGain.Add(classEndGain);
    }

    private StatusValues CalculateOutingGain(
        WeekSchedule week, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        var baseGain = week.OutingEffect?.Clone() ?? StatusValues.Zero;

        // お出かけ終了時トリガー
        var outingEndGain = FireTrigger("outing_end", cards, triggerCounters, uncapLevels);
        return baseGain.Add(outingEndGain);
    }

    private StatusValues CalculateConsultationGain(
        WeekSchedule week, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        var baseGain = week.ConsultationEffect?.Clone() ?? StatusValues.Zero;

        // 相談選択時トリガー
        var consultGain = FireTrigger("consultation", cards, triggerCounters, uncapLevels);
        return baseGain.Add(consultGain);
    }

    private StatusValues CalculateSupplyGain(
        TurnChoice turnChoice, TrainingPlan plan, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        // 活動支給自体はステータス加算なし（サポカトリガー発火のみ）
        var supplyGain = FireTrigger("activity_supply", cards, triggerCounters, uncapLevels);
        return supplyGain;
    }

    private StatusValues CalculateSpecialTrainingGain(
        WeekSchedule week, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        var baseGain = week.SpecialTrainingEffect?.Clone() ?? StatusValues.Zero;

        // 特別指導開始時トリガー
        var stGain = FireTrigger("special_training", cards, triggerCounters, uncapLevels);
        return baseGain.Add(stGain);
    }

    /// <summary>
    /// 追加イベント回数テンプレート + trigger_count_bonus のトリガーを発火する。
    /// 週次処理で既に消費された max_count を考慮する。
    /// </summary>
    private StatusValues FireAdditionalTriggersFromDict(
        List<SupportCard> cards,
        Dictionary<string, int> triggerCounters,
        Dictionary<string, int>? uncapLevels,
        Dictionary<string, int> additionalCounts)
    {
        var gain = StatusValues.Zero;
        foreach (var kvp in additionalCounts)
        {
            if (kvp.Value <= 0) continue;
            for (int i = 0; i < kvp.Value; i++)
            {
                gain = gain.Add(FireTrigger(kvp.Key, cards, triggerCounters, uncapLevels));
            }
        }
        return gain;
    }

    /// <summary>
    /// turnChoices + plan から「グローバルなトリガー発火回数」を導出する。
    /// scales_with の参照先 (例: "da_sp_end") を解決するために使う。
    /// </summary>
    private static Dictionary<string, int> ComputeBaseTriggerCounts(
        TrainingPlan plan,
        List<TurnChoice> turnChoices)
    {
        var counts = new Dictionary<string, int>();
        foreach (var tc in turnChoices)
        {
            var a = tc.ChosenAction;
            if (a == ActionType.VoLesson || a == ActionType.DaLesson || a == ActionType.ViLesson)
            {
                var stat = a switch
                {
                    ActionType.VoLesson => "vo",
                    ActionType.DaLesson => "da",
                    _ => "vi"
                };
                counts[$"{stat}_sp_end"] = counts.GetValueOrDefault($"{stat}_sp_end") + 1;
                counts[$"{stat}_lesson_end"] = counts.GetValueOrDefault($"{stat}_lesson_end") + 1;
                counts["sp_end"] = counts.GetValueOrDefault("sp_end") + 1;
                counts["lesson_end"] = counts.GetValueOrDefault("lesson_end") + 1;
            }
            else if (a == ActionType.VoClass || a == ActionType.DaClass || a == ActionType.ViClass)
            {
                counts["class_end"] = counts.GetValueOrDefault("class_end") + 1;
            }
            else if (a == ActionType.Outing)
            {
                counts["outing_end"] = counts.GetValueOrDefault("outing_end") + 1;
            }
            else if (a == ActionType.Consultation)
            {
                counts["consultation"] = counts.GetValueOrDefault("consultation") + 1;
            }
            else if (a == ActionType.SpecialTraining)
            {
                counts["special_training"] = counts.GetValueOrDefault("special_training") + 1;
            }
            else if (a == ActionType.ActivitySupply)
            {
                counts["activity_supply"] = counts.GetValueOrDefault("activity_supply") + 1;
            }
            else if (a == ActionType.Rest)
            {
                counts["rest"] = counts.GetValueOrDefault("rest") + 1;
            }
        }
        foreach (var week in plan.Schedule)
        {
            if (week.IsFixedEvent)
            {
                counts["exam_end"] = counts.GetValueOrDefault("exam_end") + 1;
            }
        }
        return counts;
    }

    /// <summary>
    /// 指定トリガーの全カード効果を発火し、合計ステータスを返す。
    /// max_count を超えた効果はスキップする。
    /// </summary>
    private StatusValues FireTrigger(
        string trigger, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters,
        Dictionary<string, int>? uncapLevels)
    {
        var gain = StatusValues.Zero;

        foreach (var card in cards)
        {
            var uncap = GetUncapLevel(card, uncapLevels);
            foreach (var effect in card.GetEffectsByTrigger(trigger))
            {
                if (effect.ValueType != "flat") continue;

                // 発動回数チェック
                var counterKey = $"{card.Id}_{trigger}_{effect.Stat}";
                triggerCounters.TryGetValue(counterKey, out int count);

                if (effect.MaxCount.HasValue && count >= effect.MaxCount.Value)
                    continue;

                triggerCounters[counterKey] = count + 1;

                var value = (int)effect.GetValue(uncap);
                switch (effect.Stat)
                {
                    case "vo": gain = gain.Add(new StatusValues(value, 0, 0)); break;
                    case "da": gain = gain.Add(new StatusValues(0, value, 0)); break;
                    case "vi": gain = gain.Add(new StatusValues(0, 0, value)); break;
                    case "all": gain = gain.Add(new StatusValues(value, value, value)); break;
                }
            }
        }

        return gain;
    }

    private string GetActionName(WeekSchedule week, TurnChoice? turnChoice)
    {
        if (week.IsFixedEvent)
            return week.EventName ?? "固定イベント";

        if (turnChoice == null)
            return "未選択";

        return turnChoice.ChosenAction switch
        {
            ActionType.VoLesson => "Voレッスン (SP)",
            ActionType.DaLesson => "Daレッスン (SP)",
            ActionType.ViLesson => "Viレッスン (SP)",
            ActionType.VoClass => "Vo授業",
            ActionType.DaClass => "Da授業",
            ActionType.ViClass => "Vi授業",
            ActionType.Outing => "お出かけ",
            ActionType.Rest => "休む",
            ActionType.Consultation => "相談",
            ActionType.ActivitySupply => "活動支給",
            ActionType.SpecialTraining => "特別指導",
            _ => "不明"
        };
    }
}
