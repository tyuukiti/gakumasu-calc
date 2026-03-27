using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class StatusCalculationService
{
    public CalculationResult Calculate(
        TrainingPlan plan,
        List<SupportCard> selectedCards,
        List<TurnChoice> turnChoices,
        Dictionary<string, int>? uncapLevels = null)
    {
        // Step 1: 基礎ステータス
        var baseStatus = plan.BaseStatus.Clone();

        // Step 2: サポートカード装備ボーナス (初期値)
        var supportBonus = CalculateEquipBonus(selectedCards, uncapLevels);

        // Step 3: ターン逐次計算
        var accumulated = StatusValues.Zero;
        var weekDetails = new List<WeekBreakdown>();
        var triggerCounters = new Dictionary<string, int>(); // カードID_トリガー → 発動回数

        foreach (var week in plan.Schedule)
        {
            var turnChoice = turnChoices.FirstOrDefault(tc => tc.Week == week.Week);
            var weekGain = CalculateWeekGain(week, turnChoice, selectedCards, plan, triggerCounters, uncapLevels);

            accumulated = accumulated.Add(weekGain);

            var actionName = GetActionName(week, turnChoice);
            weekDetails.Add(new WeekBreakdown
            {
                Week = week.Week,
                ActionName = actionName,
                Gain = weekGain
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
        Dictionary<string, int>? uncapLevels)
    {
        // 固定イベント
        if (week.IsFixedEvent)
        {
            var fixedGain = week.StatusGain?.Clone() ?? StatusValues.Zero;
            // 試験・オーディション終了時トリガー
            var examTriggerGain = FireTrigger("exam_end", cards, triggerCounters, uncapLevels);
            return fixedGain.Add(examTriggerGain);
        }

        if (turnChoice == null)
            return StatusValues.Zero;

        var gain = turnChoice.ChosenAction switch
        {
            ActionType.VoLesson => CalculateLessonGain(week, "vo", cards, triggerCounters, uncapLevels),
            ActionType.DaLesson => CalculateLessonGain(week, "da", cards, triggerCounters, uncapLevels),
            ActionType.ViLesson => CalculateLessonGain(week, "vi", cards, triggerCounters, uncapLevels),
            ActionType.VoClass => CalculateClassGain(week, "vo", cards, triggerCounters, uncapLevels),
            ActionType.DaClass => CalculateClassGain(week, "da", cards, triggerCounters, uncapLevels),
            ActionType.ViClass => CalculateClassGain(week, "vi", cards, triggerCounters, uncapLevels),
            ActionType.Outing => CalculateOutingGain(week, cards, triggerCounters, uncapLevels),
            ActionType.Consultation => CalculateConsultationGain(week, cards, triggerCounters, uncapLevels),
            ActionType.Rest => StatusValues.Zero,
            ActionType.ActivitySupply => CalculateSupplyGain(turnChoice, plan, cards, triggerCounters, uncapLevels),
            ActionType.SpecialTraining => CalculateSpecialTrainingGain(week, cards, triggerCounters, uncapLevels),
            _ => StatusValues.Zero
        };

        return gain;
    }

    private StatusValues CalculateLessonGain(
        WeekSchedule week, string lessonType, List<SupportCard> cards,
        Dictionary<string, int> triggerCounters, Dictionary<string, int>? uncapLevels)
    {
        var lesson = week.GetLesson(lessonType);
        if (lesson == null)
            return StatusValues.Zero;

        var raw = lesson.SpBonus;

        // パラメータボーナス% を集計 (SP率は突破確率なので理論値計算では除外)
        double totalParaBonus = 0;
        foreach (var card in cards)
        {
            totalParaBonus += card.GetParaBonus(GetUncapLevel(card, uncapLevels));
        }

        // パラボのみ適用
        double multiplier = 1.0 + totalParaBonus / 100.0;

        int vo = (int)Math.Floor(raw.Vo * multiplier);
        int da = (int)Math.Floor(raw.Da * multiplier);
        int vi = (int)Math.Floor(raw.Vi * multiplier);

        var result = new StatusValues(vo, da, vi);

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
            ActionType.Rest => "休憩",
            ActionType.Consultation => "相談",
            ActionType.ActivitySupply => "活動支給",
            ActionType.SpecialTraining => "特別指導",
            _ => "不明"
        };
    }
}
