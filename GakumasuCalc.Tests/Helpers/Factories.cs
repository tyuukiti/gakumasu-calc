using GakumasuCalc.Models;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>合成フィクスチャ生成 (TS 版 factories.ts と対応)。</summary>
public static class Factories
{
    private static int _seq;
    public static void ResetIds() => _seq = 0;
    public static string NextId(string prefix = "C") => $"{prefix}{(++_seq):D4}";

    public sealed class CardSpec
    {
        public string? Id;
        public string Type = "vo";
        public string Rarity = "ssr";
        public string Plan = "";
        public string? Tag;
        public int Vo, Da, Vi;            // equip/flat
        public int ParaVo, ParaDa, ParaVi, ParaAll; // equip/para_bonus %
        public string[]? Sp;              // equip/sp_rate を付ける属性
    }

    private static CardEffect Flat(string stat, int v) => new()
    {
        Trigger = "equip", Stat = stat, ValueType = "flat",
        Values = new List<double> { v, v, v, v, v },
    };

    private static CardEffect Para(string stat, int v) => new()
    {
        Trigger = "equip", Stat = stat, ValueType = "para_bonus",
        Values = new List<double> { v, v, v, v, v },
    };

    public static SupportCard MakeCard(CardSpec s)
    {
        var effects = new List<CardEffect>();
        if (s.Vo != 0) effects.Add(Flat("vo", s.Vo));
        if (s.Da != 0) effects.Add(Flat("da", s.Da));
        if (s.Vi != 0) effects.Add(Flat("vi", s.Vi));
        if (s.ParaAll != 0) effects.Add(Para("all", s.ParaAll));
        if (s.ParaVo != 0) effects.Add(Para("vo", s.ParaVo));
        if (s.ParaDa != 0) effects.Add(Para("da", s.ParaDa));
        if (s.ParaVi != 0) effects.Add(Para("vi", s.ParaVi));
        if (s.Sp != null)
            foreach (var stat in s.Sp)
                effects.Add(new CardEffect
                {
                    Trigger = "equip", Stat = stat, ValueType = "sp_rate",
                    Values = new List<double> { 10, 10, 10, 10, 10 },
                });

        return new SupportCard
        {
            Id = s.Id ?? NextId(),
            Name = s.Id ?? "card",
            Rarity = s.Rarity,
            Type = s.Type,
            Plan = s.Plan,
            Tag = s.Tag ?? string.Empty,
            Effects = effects,
        };
    }

    public sealed class PlanSpec
    {
        public string Id = "synthetic";
        public int StatusLimit = 9999;
        public int BaseVo, BaseDa, BaseVi;
        public int LessonVo, LessonDa, LessonVi; // 各属性のレッスン週数
        public int LessonGain = 100;
    }

    public static TrainingPlan MakePlan(PlanSpec s)
    {
        var schedule = new List<WeekSchedule>();
        int week = 1;
        void AddLessons(string stat, int n)
        {
            for (int i = 0; i < n; i++)
            {
                schedule.Add(new WeekSchedule
                {
                    Week = week++,
                    Type = "normal",
                    AvailableActions = new List<string> { $"{stat}_lesson", "vo_lesson", "da_lesson", "vi_lesson" },
                    Lessons = new List<LessonConfig>
                    {
                        new() { Type = stat, SpBonus = MakeStat(stat, s.LessonGain) },
                    },
                    Classes = new List<LessonConfig>(),
                });
            }
        }
        AddLessons("vo", s.LessonVo);
        AddLessons("da", s.LessonDa);
        AddLessons("vi", s.LessonVi);

        return new TrainingPlan
        {
            Id = s.Id,
            Name = "Synthetic Plan",
            Description = "test fixture",
            TotalWeeks = schedule.Count,
            StatusLimit = s.StatusLimit,
            BaseStatus = new StatusValues(s.BaseVo, s.BaseDa, s.BaseVi),
            Schedule = schedule,
        };
    }

    private static StatusValues MakeStat(string stat, int v) => stat switch
    {
        "vo" => new StatusValues(v, 0, 0),
        "da" => new StatusValues(0, v, 0),
        "vi" => new StatusValues(0, 0, v),
        _ => StatusValues.Zero,
    };
}
