using System.IO;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using GakumasuCalc.ViewModels;

namespace GakumasuCalc.Tests;

/// <summary>
/// HIF カスタムPアイテム (ポーチ) の発動回数集計 (Web版 hifPouch.test.ts と対)。
///
/// - 定義ファイル Data/Hif/pouch.yaml の整合 (3色×4体、トリガー/効果キーが既知)
/// - HifPouchService.Compute: 既定日程で 花ロボ黄 = 2+4+4 = 10 本、花もじゃ黄 = 2+2+2 = 6 本、
///   1段階目の窓に相談が無い日程では もじゃ段階が 0 本、部分選択、定義なし
/// - Merge は元を変更しない
/// - HifViewModel: 従属選択のリセット、日程変更でのプレビュー更新、診断ブロック、プリセット往復
/// </summary>
public class HifPouchTests
{
    private static readonly HashSet<string> KnownKeys = new AdditionalCounts().ToDictionary().Keys.ToHashSet();
    private static readonly HashSet<string> Triggers = new() { "lesson_end", "consultation", "activity_supply", "outing_end" };

    private static HifPouchDefinition Def => RepoData.LoadHifPouch();
    private static TrainingPlan Plan => RepoData.LoadPlan("hif");

    /// <summary>既定日程 (授業 > 差し入れ > おでかけ > 相談 > 特別指導) の TurnChoice。Da主レッスン</summary>
    private static List<TurnChoice> DefaultTurnChoices() => CardScoringService.BuildTurnChoices(Plan, new List<string> { "da", "vi" });

    private static List<TurnChoice> WithAction(List<TurnChoice> tc, int week, ActionType action)
        => tc.Select(t => t.Week == week ? new TurnChoice { Week = t.Week, ChosenAction = action } : t).ToList();

    [Fact]
    public void 定義は3色4体でトリガーと効果キーが既知の範囲にある()
    {
        var def = Def;
        Assert.Equal(new[] { "red", "green", "yellow" }, def.Colors.Select(c => c.Id));
        Assert.Equal(new[] { (0, 8, 12), (1, 14, 19), (2, 21, 26) },
            def.StageWindows.Select(w => (w.Stage, w.FromWeek, w.ToWeek)));

        static void Check(HifPouchStageEffect e, string label)
        {
            Assert.True(Triggers.Contains(e.Trigger), $"{label} trigger={e.Trigger}");
            Assert.True(e.MaxCount >= 1, $"{label} max_count");
            foreach (var (k, v) in e.Effects)
            {
                Assert.True(KnownKeys.Contains(k), $"{label} effect key={k}");
                Assert.True(v > 0, $"{label} {k}");
            }
        }

        foreach (var c in def.Colors)
        {
            Check(c.Base, c.Id);
            Assert.Equal(4, c.Plushes.Count);
            foreach (var p in c.Plushes)
            {
                Check(p, $"{c.Id}/{p.Id}");
                // レッスン終了時系 (赤くま・緑インコ・黄ロボ) のデコは 花/リボン/羽 の 3 択、それ以外は 4 択
                Assert.Equal(p.Trigger == "lesson_end" ? 3 : 4, p.Decorations.Count);
                foreach (var d in p.Decorations)
                {
                    Check(d, $"{c.Id}/{p.Id}/{d.Id}");
                    Assert.Equal(p.Trigger, d.Trigger);
                }
            }
        }
        Assert.Equal(new[] { "花ロボ 黄", "花もじゃ 黄", "メダルもじゃ 黄" }, HifPouchService.DefaultPresets.Select(p => p.Name));
        foreach (var preset in HifPouchService.DefaultPresets)
            Assert.Equal((preset.Color, preset.Plush, preset.Deco),
                HifPouchService.Sanitize(def, preset.Color, preset.Plush, preset.Deco));
    }

    [Fact]
    public void 既定日程で花ロボ黄は10本_花もじゃ黄は6本()
    {
        var tc = DefaultTurnChoices();

        var robo = HifPouchService.Compute(Def, "yellow", "robo", "flower", tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 10 }, robo.Counts);
        Assert.Equal(new[] { 9, 11 }, robo.Stages[0]!.FiredWeeks);
        Assert.Equal(new[] { 15, 18 }, robo.Stages[1]!.FiredWeeks);
        Assert.Equal(new[] { 22, 25 }, robo.Stages[2]!.FiredWeeks);

        var moja = HifPouchService.Compute(Def, "yellow", "moja", "flower", tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 6 }, moja.Counts);
        Assert.Equal(new[] { 9, 11 }, moja.Stages[0]!.FiredWeeks);
        Assert.Equal(new[] { 19 }, moja.Stages[1]!.FiredWeeks);
        Assert.Equal(new[] { 26 }, moja.Stages[2]!.FiredWeeks);
        Assert.Equal("1段階目 もじゃ", moja.Stages[1]!.Label);
        Assert.Equal("2段階目 花", moja.Stages[2]!.Label);
    }

    [Fact]
    public void 窓内に相談が無い日程ではもじゃ段階は0本()
    {
        var tc = WithAction(DefaultTurnChoices(), 19, ActionType.SpecialTraining);
        var r = HifPouchService.Compute(Def, "yellow", "moja", "flower", tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 4 }, r.Counts);
        Assert.Equal(0, r.Stages[1]!.Fires);
        Assert.Empty(r.Stages[1]!.FiredWeeks);
    }

    [Fact]
    public void おでかけ系は踏んだ段階だけ発動し強化と削除も加算される()
    {
        var r0 = HifPouchService.Compute(Def, "yellow", "usagi", "feather", DefaultTurnChoices());
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 2 }, r0.Counts);
        Assert.Equal(0, r0.Stages[1]!.Fires);
        Assert.Equal(0, r0.Stages[2]!.Fires);

        var tc = WithAction(WithAction(DefaultTurnChoices(), 16, ActionType.Outing), 23, ActionType.Outing);
        var r = HifPouchService.Compute(Def, "yellow", "usagi", "feather", tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 6, ["skill_enhance"] = 1, ["skill_delete"] = 1 }, r.Counts);
        Assert.Equal(new[] { 16 }, r.Stages[1]!.FiredWeeks);
        Assert.Equal(new[] { 23 }, r.Stages[2]!.FiredWeeks);
    }

    [Fact]
    public void 部分選択と定義なしと未知idの扱い()
    {
        var tc = DefaultTurnChoices();
        var colorOnly = HifPouchService.Compute(Def, "yellow", null, null, tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 2 }, colorOnly.Counts);
        Assert.Null(colorOnly.Stages[1]);
        Assert.Null(colorOnly.Stages[2]);

        var noDeco = HifPouchService.Compute(Def, "yellow", "moja", null, tc);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 4 }, noDeco.Counts);
        Assert.Null(noDeco.Stages[2]);

        Assert.Empty(HifPouchService.Compute(null, "yellow", "moja", "flower", tc).Counts);
        Assert.Empty(HifPouchService.Compute(Def, null, null, null, tc).Counts);
        Assert.Empty(HifPouchService.Compute(Def, "blue", null, null, tc).Counts);
        Assert.Empty(HifPouchService.Compute(Def, "yellow", "moja", "flower", new List<TurnChoice>()).Counts);

        Assert.Equal(("yellow", null, null), HifPouchService.Sanitize(Def, "yellow", "kuma", "flower"));
        Assert.Equal((null, null, null), HifPouchService.Sanitize(Def, "nope", "moja", "flower"));
        Assert.Equal("黄 → もじゃ → 花", HifPouchService.Describe(Def, "yellow", "moja", "flower"));
        Assert.Equal("緑", HifPouchService.Describe(Def, "green", null, null));
        Assert.Null(HifPouchService.Describe(Def, null, null, null));
    }

    [Fact]
    public void Mergeは元を変更せず加算し未知キーは無視する()
    {
        var baseCounts = new AdditionalCounts { PDrinkAcquire = 15, SkillDelete = 4, SkillChange = 3 };
        var merged = HifPouchService.Merge(baseCounts, new Dictionary<string, int>
        {
            ["p_drink_acquire"] = 6, ["skill_delete"] = 1, ["unknown_key"] = 9,
        });
        Assert.Equal(15, baseCounts.PDrinkAcquire);
        Assert.Equal(4, baseCounts.SkillDelete);
        Assert.Equal(21, merged.PDrinkAcquire);
        Assert.Equal(5, merged.SkillDelete);
        Assert.Equal(3, merged.SkillChange);
        Assert.NotSame(baseCounts, merged);
    }

    [Fact]
    public void 診断行と合計行の書式はWeb版と同一()
    {
        var r = HifPouchService.Compute(Def, "yellow", "moja", "flower", DefaultTurnChoices());
        Assert.Equal("未カスタマイズ: レッスン終了時 (×2) W9, W11 → 2回 / Pドリンク獲得+1", HifPouchService.FormatStageLine(r.Stages[0]!));
        Assert.Equal("1段階目 もじゃ: 相談選択時 (×1) W19 → 1回 / Pドリンク獲得+2", HifPouchService.FormatStageLine(r.Stages[1]!));
        Assert.Equal("Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0", HifPouchService.FormatCountSummary(r.Counts));
        var none = HifPouchService.Compute(Def, "yellow", "moja", "flower", WithAction(DefaultTurnChoices(), 19, ActionType.SpecialTraining));
        Assert.Equal("1段階目 もじゃ: 相談選択時 (×1) 発動なし / Pドリンク獲得+2", HifPouchService.FormatStageLine(none.Stages[1]!));
    }

    // ---------------------------------------------------------------------
    // HifViewModel
    // ---------------------------------------------------------------------

    private static HifViewModel NewVm() => new() { HifPlan = Plan, PouchDefinition = Def };

    [Fact]
    public void VM_従属選択_親を変えると子はリセットされ候補は親で絞られる()
    {
        var vm = NewVm();
        Assert.True(vm.IsPouchAvailable);
        Assert.Equal(new[] { "", "red", "green", "yellow" }, vm.PouchColorOptions.Select(o => o.Value));
        Assert.Single(vm.AvailablePouchPlushes); // 色未選択なら「未選択」だけ

        vm.PouchColor = "yellow";
        Assert.Equal(new[] { "", "robo", "moja", "inko", "usagi" }, vm.AvailablePouchPlushes.Select(o => o.Value));
        vm.PouchPlush = "moja";
        Assert.Equal(new[] { "", "flower", "ribbon", "medal", "feather" }, vm.AvailablePouchDecorations.Select(o => o.Value));
        vm.PouchDecoration = "flower";
        Assert.Equal(("yellow", "moja", "flower"), (vm.PouchColorId, vm.PouchPlushId, vm.PouchDecorationId));
        Assert.Equal(": 黄 → もじゃ → 花", vm.PouchHeaderText);

        vm.PouchPlush = "robo";
        Assert.Null(vm.PouchDecorationId);
        vm.PouchColor = "green";
        Assert.Null(vm.PouchPlushId);
        Assert.Null(vm.PouchDecorationId);
        vm.PouchPlush = "moja"; // 緑に もじゃ は無い
        Assert.Null(vm.PouchPlushId);

        vm.PouchColor = "";
        Assert.Null(vm.PouchColorId);
        Assert.Equal(string.Empty, vm.PouchHeaderText);
    }

    [Fact]
    public void VM_プリセットは初回に3ルートを持ち_読込_保存_同名上書き_削除できる()
    {
        var path = Path.Combine(Path.GetTempPath(), "gakumasu_pouch_preset_" + Guid.NewGuid().ToString("N"), "hif_pouch_presets.yaml");
        try
        {
            // ファイル未作成: 初期プリセット (Wiki の 3 ルート)
            var vm = new HifViewModel(pouchPresetService: new HifPouchPresetService(path)) { HifPlan = Plan, PouchDefinition = Def };
            Assert.Equal(new[] { "花ロボ 黄", "花もじゃ 黄", "メダルもじゃ 黄" }, vm.PouchPresets.Select(p => p.Name));
            Assert.Equal("3/10", vm.PouchPresetCountText);

            // 読込で選択に反映され、保存欄に名前が入る
            vm.SelectedPouchPreset = vm.PouchPresets.First(p => p.Name == "花もじゃ 黄");
            Assert.Equal(("yellow", "moja", "flower"), (vm.PouchColorId, vm.PouchPlushId, vm.PouchDecorationId));
            Assert.Equal("花もじゃ 黄", vm.NewPouchPresetName);

            // 新規保存 → 同名上書き (選択を変えてもプリセット側はスナップショット)
            vm.SetPouchSelection("green", "inko", "feather");
            vm.NewPouchPresetName = "緑インコ羽";
            Assert.True(vm.SavePouchPresetCommand.CanExecute(null));
            vm.SavePouchPresetCommand.Execute(null);
            Assert.Equal(4, vm.PouchPresets.Count);
            Assert.Equal(string.Empty, vm.NewPouchPresetName);
            vm.PouchDecoration = "ribbon";
            vm.NewPouchPresetName = "緑インコ羽";
            vm.SavePouchPresetCommand.Execute(null);
            Assert.Single(vm.PouchPresets, p => p.Name == "緑インコ羽");
            Assert.Equal("ribbon", vm.PouchPresets.Single(p => p.Name == "緑インコ羽").Decoration);
            vm.PouchColor = "";
            Assert.Equal("green", vm.PouchPresets.Single(p => p.Name == "緑インコ羽").Color);

            // 初期プリセットも削除でき、永続化される (再起動相当で空にならず残りが戻る)
            vm.SelectedPouchPreset = vm.PouchPresets.First(p => p.Name == "花ロボ 黄");
            Assert.True(vm.DeletePouchPresetCommand.CanExecute(null));
            vm.DeletePouchPresetCommand.Execute(null);
            Assert.Null(vm.SelectedPouchPreset);
            var reloaded = new HifViewModel(pouchPresetService: new HifPouchPresetService(path)) { HifPlan = Plan, PouchDefinition = Def };
            Assert.Equal(new[] { "花もじゃ 黄", "メダルもじゃ 黄", "緑インコ羽" }, reloaded.PouchPresets.Select(p => p.Name));
            Assert.Equal(("green", "inko", "ribbon"),
                reloaded.PouchPresets.Single(p => p.Name == "緑インコ羽") is { } r ? (r.Color, r.Plush, r.Decoration) : default);

            // 全削除して空で保存した状態は初期プリセットに戻らない
            foreach (var p in reloaded.PouchPresets.ToList())
            {
                reloaded.SelectedPouchPreset = p;
                reloaded.DeletePouchPresetCommand.Execute(null);
            }
            Assert.Empty(new HifViewModel(pouchPresetService: new HifPouchPresetService(path)).PouchPresets);

            // 上限: 新規追加は不可、同名上書きは可
            var full = new HifViewModel(pouchPresetService: new HifPouchPresetService(path)) { HifPlan = Plan, PouchDefinition = Def };
            for (int i = 0; i < HifPouchPresetService.MaxPresets; i++)
            {
                full.NewPouchPresetName = $"p{i}";
                full.SavePouchPresetCommand.Execute(null);
            }
            Assert.Equal(HifPouchPresetService.MaxPresets, full.PouchPresets.Count);
            full.NewPouchPresetName = "あふれる";
            Assert.False(full.SavePouchPresetCommand.CanExecute(null));
            full.NewPouchPresetName = "p0";
            Assert.True(full.SavePouchPresetCommand.CanExecute(null));
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VM_BuildTurnChoicesはBuildHifPlanAndChoicesと同じ規則で固定日と選択肢なしの日を除く()
    {
        var vm = NewVm();
        var tc = vm.BuildTurnChoices();
        // 固定日 (7/13/20/27/29) とインターバル (28) は含まれない
        Assert.DoesNotContain(tc, t => t.Week is 7 or 13 or 20 or 27 or 28 or 29);
        // 既定: 公開レッスンは Vo、授業は Vo、その他は 活動支給 > おでかけ > 相談
        Assert.Equal(ActionType.VoLesson, tc.Single(t => t.Week == 2).ChosenAction);
        Assert.Equal(ActionType.ActivitySupply, tc.Single(t => t.Week == 1).ChosenAction);
        Assert.Equal(ActionType.Outing, tc.Single(t => t.Week == 5).ChosenAction);
        Assert.Equal(ActionType.Consultation, tc.Single(t => t.Week == 19).ChosenAction);
        Assert.Equal(23, tc.Count);
    }

    [Fact]
    public void VM_プレビューは選択と日程変更で更新される()
    {
        var vm = NewVm();
        Assert.Equal(3, vm.PouchStageRows.Count);
        Assert.All(vm.PouchStageRows, r => Assert.Equal("—", r.FiredText));
        Assert.Equal(string.Empty, vm.PouchSummaryLine);

        vm.SetPouchSelection("yellow", "moja", "flower");
        Assert.Equal("Day 9, Day 11 → 2回", vm.PouchStageRows[0].FiredText);
        Assert.Equal("Day 19 → 1回", vm.PouchStageRows[1].FiredText);
        Assert.Equal("本戦6日目 → 1回", vm.PouchStageRows[2].FiredText);
        Assert.Equal("相談選択時 ×1", vm.PouchStageRows[1].TriggerLabel);
        Assert.Equal("Pドリンク獲得+2", vm.PouchStageRows[1].EffectText);
        Assert.Equal("イベント回数へ自動加算: Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0", vm.PouchSummaryText);
        Assert.Equal("ポーチ自動加算: Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0", vm.PouchSummaryLine);

        // 日程変更 (Day19 を特別指導) で 1段階目が不発になり、注意色になる
        vm.ScheduleItems.Single(i => i.Week == 19).SelectedAction = "special_training";
        Assert.Equal("発動なし", vm.PouchStageRows[1].FiredText);
        Assert.Equal("#DC2626", vm.PouchStageRows[1].Foreground);
        Assert.Contains("Pドリンク獲得 +4", vm.PouchSummaryText);
        Assert.Equal(new Dictionary<string, int> { ["p_drink_acquire"] = 4 }, vm.ComputePouchCounts(vm.BuildTurnChoices()).Counts);
    }

    [Fact]
    public void VM_診断ブロックはWeb版と同一文面で選択時だけ出る()
    {
        var vm = NewVm();
        var sb = new System.Text.StringBuilder();
        vm.AppendPouchDiagnostic(sb);
        Assert.Equal(string.Empty, sb.ToString());

        vm.SetPouchSelection("yellow", "moja", "flower");
        vm.AppendPouchDiagnostic(sb);
        // 先頭の空行 (AppendLine) と CRLF を除いて比較
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        Assert.Equal(new[]
        {
            "[カスタムPアイテム]",
            "選択: 黄 → もじゃ → 花",
            "未カスタマイズ: レッスン終了時 (×2) W9, W11 → 2回 / Pドリンク獲得+1",
            "1段階目 もじゃ: 相談選択時 (×1) W19 → 1回 / Pドリンク獲得+2",
            "2段階目 花: 相談選択時 (×1) W26 → 1回 / Pドリンク獲得+2",
            "加算: Pドリンク獲得 +6 ／ SSR獲得 +0 ／ チェンジ +0 ／ 強化 +0 ／ 削除 +0",
        }, lines);
    }

    // ---------------------------------------------------------------------
    // 条件プリセット (YAML 往復)
    // ---------------------------------------------------------------------

    [Fact]
    public void 条件プリセットはポーチ選択を往復し旧ファイルはnullになる()
    {
        var path = Path.Combine(Path.GetTempPath(), "gakumasu_pouch_test_" + Guid.NewGuid().ToString("N"), "hif_condition_presets.yaml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var svc = new HifConditionPresetService(path);
            svc.Save(new List<HifConditionPreset>
            {
                new()
                {
                    Name = "ポーチあり",
                    Hif = new HifConditionHifFields { PouchColor = "yellow", PouchPlush = "moja", PouchDecoration = "flower" },
                },
                new() { Name = "未選択" },
            });
            var loaded = svc.Load();
            Assert.Equal(("yellow", "moja", "flower"),
                (loaded[0].Hif.PouchColor, loaded[0].Hif.PouchPlush, loaded[0].Hif.PouchDecoration));
            Assert.Null(loaded[1].Hif.PouchColor);
            Assert.Null(loaded[1].Hif.PouchPlush);
            Assert.Null(loaded[1].Hif.PouchDecoration);

            // 旧スキーマ (pouch 無し)
            File.WriteAllText(path, string.Join('\n', new[]
            {
                "presets:",
                "- name: 旧形式",
                "  hif:",
                "    bulk_main_stat: da",
            }));
            var old = Assert.Single(new HifConditionPresetService(path).Load());
            Assert.Null(old.Hif.PouchColor);
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
