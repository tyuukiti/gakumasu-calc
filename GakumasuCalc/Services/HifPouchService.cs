using System.Reflection;
using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

/// <summary>
/// HIF カスタムPアイテム (ポーチ) の発動回数集計 (Web版 services/hifPouch.ts と対)。
///
/// ポーチは 選抜①突破で入手 (段階0=未カスタマイズ) → ②突破で1段階目 (ぬいぐるみ) → ③突破で2段階目 (デコレーション)
/// と変化し、段階ごとに「レッスン終了時 / 相談選択時 / 差し入れ終了時 / おでかけ終了時」に効果を起こす。
/// 段階ごとの有効週 (StageWindows) の中で Trigger に合う日程を先頭から MaxCount 件まで採用し、
/// 1回あたりの Effects (AdditionalCounts のキー) を掛けて追加イベント回数にする。
/// 計算エンジンは変更せず、合算した AdditionalCounts を渡すだけ。
/// </summary>
public static class HifPouchService
{
    /// <summary>段階ごとの集計結果 (プレビュー表・診断で共用)</summary>
    public sealed class StageResult
    {
        /// <summary>0=未カスタマイズ, 1=ぬいぐるみ, 2=デコレーション</summary>
        public int Stage { get; init; }
        /// <summary>表示名 (例: "未カスタマイズ", "1段階目 もじゃ", "2段階目 花")</summary>
        public string Label { get; init; } = string.Empty;
        public string Trigger { get; init; } = string.Empty;
        public int MaxCount { get; init; }
        /// <summary>1回あたりの追加回数</summary>
        public Dictionary<string, int> Effects { get; init; } = new();
        public string Description { get; init; } = string.Empty;
        public int FromWeek { get; init; }
        public int ToWeek { get; init; }
        /// <summary>採用した発動日 (week 昇順、MaxCount 件まで)</summary>
        public List<int> FiredWeeks { get; init; } = new();
        public int Fires => FiredWeeks.Count;
    }

    public sealed class Result
    {
        /// <summary>追加イベント回数への加算分 (0 のキーは持たない)</summary>
        public Dictionary<string, int> Counts { get; } = new();
        /// <summary>index = 段階。未選択 (または定義なし) の段階は null</summary>
        public StageResult?[] Stages { get; } = new StageResult?[3];
    }

    /// <summary>合計行・診断で使う回数キーと表示名 (この順で表示)</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> CountLabels = new[]
    {
        ("p_drink_acquire", "Pドリンク獲得"),
        ("skill_ssr_acquire", "SSR獲得"),
        ("skill_change", "チェンジ"),
        ("skill_enhance", "強化"),
        ("skill_delete", "削除"),
    };

    public static readonly IReadOnlyDictionary<string, string> TriggerLabels = new Dictionary<string, string>
    {
        ["lesson_end"] = "レッスン終了時",
        ["consultation"] = "相談選択時",
        ["activity_supply"] = "差し入れ終了時",
        ["outing_end"] = "おでかけ終了時",
    };

    /// <summary>
    /// ポーチプリセットの初期値 (Wiki 掲載の 3 ルート)。プリセットの保存領域が未作成のときだけ投入し、
    /// 以後は他のプリセットと同じくユーザが上書き・削除できる (Web版 HIF_POUCH_DEFAULT_PRESETS と対)。
    /// </summary>
    public sealed record Preset(string Name, string Color, string Plush, string Deco);

    public static readonly IReadOnlyList<Preset> DefaultPresets = new[]
    {
        new Preset("花ロボ 黄", "yellow", "robo", "flower"),
        new Preset("花もじゃ 黄", "yellow", "moja", "flower"),
        new Preset("メダルもじゃ 黄", "yellow", "moja", "medal"),
    };

    /// <summary>日程の行動がポーチの発動契機に当たるか</summary>
    public static bool TriggerMatches(string trigger, ActionType action) => trigger switch
    {
        "lesson_end" => action is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson,
        "consultation" => action == ActionType.Consultation,
        "activity_supply" => action == ActionType.ActivitySupply,
        "outing_end" => action == ActionType.Outing,
        _ => false,
    };

    /// <summary>選択を定義と突き合わせて整合させる (定義に無い id は null、親が null なら子も null)</summary>
    public static (string? Color, string? Plush, string? Deco) Sanitize(
        HifPouchDefinition? def, string? color, string? plush, string? deco)
    {
        var c = def?.Colors.FirstOrDefault(x => x.Id == color);
        var p = c?.Plushes.FirstOrDefault(x => x.Id == plush);
        var d = p?.Decorations.FirstOrDefault(x => x.Id == deco);
        return (c?.Id, p?.Id, d?.Id);
    }

    /// <summary>選択の表示名 "黄 → もじゃ → 花" (未選択なら null)</summary>
    public static string? Describe(HifPouchDefinition? def, string? color, string? plush, string? deco)
    {
        var c = def?.Colors.FirstOrDefault(x => x.Id == color);
        if (c == null) return null;
        var parts = new List<string> { c.Name };
        var p = c.Plushes.FirstOrDefault(x => x.Id == plush);
        if (p != null)
        {
            parts.Add(p.Name);
            var d = p.Decorations.FirstOrDefault(x => x.Id == deco);
            if (d != null) parts.Add(d.Name);
        }
        return string.Join(" → ", parts);
    }

    /// <summary>
    /// 日程 (turnChoices) からポーチの発動回数を集計する。
    /// def / color が無ければ回数ゼロ (ポーチ未使用と同じ)。
    /// </summary>
    public static Result Compute(
        HifPouchDefinition? def, string? color, string? plush, string? deco, IReadOnlyList<TurnChoice> turnChoices)
    {
        var result = new Result();
        if (def == null || string.IsNullOrEmpty(color)) return result;

        var c = def.Colors.FirstOrDefault(x => x.Id == color);
        if (c == null) return result;
        var p = !string.IsNullOrEmpty(plush) ? c.Plushes.FirstOrDefault(x => x.Id == plush) : null;
        var d = p != null && !string.IsNullOrEmpty(deco) ? p.Decorations.FirstOrDefault(x => x.Id == deco) : null;

        var stageEffects = new (HifPouchStageEffect Effect, string Label)?[]
        {
            (c.Base, "未カスタマイズ"),
            p != null ? (p, $"1段階目 {p.Name}") : null,
            d != null ? (d, $"2段階目 {d.Name}") : null,
        };

        var sorted = turnChoices.OrderBy(tc => tc.Week).ToList();
        for (int s = 0; s < stageEffects.Length; s++)
        {
            var se = stageEffects[s];
            if (se == null) continue;
            var win = def.StageWindows.FirstOrDefault(w => w.Stage == s);
            if (win == null) continue;
            var effect = se.Value.Effect;
            var fired = new List<int>();
            foreach (var tc in sorted)
            {
                if (fired.Count >= effect.MaxCount) break;
                if (tc.Week < win.FromWeek || tc.Week > win.ToWeek) continue;
                if (!TriggerMatches(effect.Trigger, tc.ChosenAction)) continue;
                fired.Add(tc.Week);
            }
            if (fired.Count > 0)
            {
                foreach (var (key, value) in effect.Effects)
                {
                    if (value <= 0) continue;
                    result.Counts[key] = result.Counts.GetValueOrDefault(key) + value * fired.Count;
                }
            }
            result.Stages[s] = new StageResult
            {
                Stage = s,
                Label = se.Value.Label,
                Trigger = effect.Trigger,
                MaxCount = effect.MaxCount,
                Effects = new Dictionary<string, int>(effect.Effects),
                Description = effect.Description ?? string.Empty,
                FromWeek = win.FromWeek,
                ToWeek = win.ToWeek,
                FiredWeeks = fired,
            };
        }
        return result;
    }

    /// <summary>手入力の AdditionalCounts にポーチ分を足した新しいインスタンスを返す (元は変更しない)</summary>
    public static AdditionalCounts Merge(AdditionalCounts baseCounts, IReadOnlyDictionary<string, int> extra)
    {
        var props = typeof(AdditionalCounts).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.CanRead && p.CanWrite)
            .ToList();
        var result = new AdditionalCounts();
        foreach (var p in props)
            p.SetValue(result, p.GetValue(baseCounts));
        foreach (var (key, value) in extra)
        {
            if (value <= 0) continue;
            var name = ToPascalCase(key);
            var prop = props.FirstOrDefault(p => p.Name == name);
            if (prop == null)
            {
                System.Diagnostics.Debug.WriteLine($"ポーチ効果の未知キーを無視: {key}");
                continue;
            }
            prop.SetValue(result, (int)prop.GetValue(result)! + value);
        }
        return result;
    }

    /// <summary>合計行の文言 "Pドリンク獲得 +6 ／ SSR獲得 +0 ／ ..."</summary>
    public static string FormatCountSummary(IReadOnlyDictionary<string, int> counts)
        => string.Join(" ／ ", CountLabels.Select(cl => $"{cl.Label} +{counts.GetValueOrDefault(cl.Key)}"));

    /// <summary>1回あたりの効果 "Pドリンク獲得+2・チェンジ+1" (無ければ "効果なし")</summary>
    public static string FormatEffects(IReadOnlyDictionary<string, int> effects)
    {
        var parts = CountLabels
            .Where(cl => effects.GetValueOrDefault(cl.Key) > 0)
            .Select(cl => $"{cl.Label}+{effects[cl.Key]}")
            .ToList();
        return parts.Count > 0 ? string.Join("・", parts) : "効果なし";
    }

    /// <summary>診断情報の段階行 (Web版 formatHifPouchStageLine と同一書式)</summary>
    public static string FormatStageLine(StageResult st)
    {
        var fired = st.Fires > 0
            ? $"{string.Join(", ", st.FiredWeeks.Select(w => $"W{w}"))} → {st.Fires}回"
            : "発動なし";
        var trigger = TriggerLabels.GetValueOrDefault(st.Trigger, st.Trigger);
        return $"{st.Label}: {trigger} (×{st.MaxCount}) {fired} / {FormatEffects(st.Effects)}";
    }

    /// <summary>"p_drink_acquire" → "PDrinkAcquire" (AdditionalCounts のプロパティ名)</summary>
    private static string ToPascalCase(string snake)
        => string.Concat(snake.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1)));
}
