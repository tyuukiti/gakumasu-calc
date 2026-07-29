using System.IO;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using GakumasuCalc.ViewModels;

namespace GakumasuCalc.Tests;

/// <summary>
/// HIF条件プリセット (入力条件一式の保存・読込) のテスト。
/// Service の YAML ラウンドトリップと、HifViewModel の CaptureScheduleState/ApplyScheduleState を検証する。
/// Web版 tests/hifConditionPreset.test.ts と対応。
/// </summary>
public class HifConditionPresetTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"hif_condition_preset_test_{Guid.NewGuid():N}", "hif_condition_presets.yaml");

    private static void CleanUp(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static HifConditionPreset BuildFullPreset() => new()
    {
        Name = "Vo全踏み",
        Hif = new HifConditionHifFields
        {
            Choices = new List<HifScheduleChoiceEntry>
            {
                new() { Week = 1, Action = "vo_lesson", SubStat = "da" },
                new() { Week = 2, Action = "outing", SubStat = null },
            },
            ExamAllocations = new List<HifExamAllocationEntry>
            {
                new() { Week = 5, Vo = 80, Da = 0, Vi = 0 },
            },
            ExamRatioVo = 100,
            ExamRatioDa = 0,
            ExamRatioVi = 0,
            BulkMainStat = "vo",
            BulkSubStat = "vi",
            BulkClassStat = "da",
        },
        Calc = new HifConditionCalcFields
        {
            SelectedPlanType = "logic",
            VoSpCount = 2,
            DaSpCount = 0,
            ViSpCount = 1,
            Counts = new AdditionalCounts { SkillAcquire = 5, PDrinkAcquire = 3 },
            SelectedTemplateName = "ロジック(好印象)",
            OwnedOnly = true,
            ContestMode = true,
            RequiredCardIds = new List<string> { "0001", "0002" },
            ExcludedCardIds = new List<string> { "0003" },
            MemoryBonuses = new List<MemoryBonus>
            {
                new()
                {
                    Vo = new MemoryAttributeBonus(100, MemoryBonusType.Flat),
                    Da = new MemoryAttributeBonus(3.5, MemoryBonusType.ParaBonus),
                    Vi = new MemoryAttributeBonus(0, MemoryBonusType.Flat),
                },
            },
        },
    };

    [Fact]
    public void Service_RoundTrip_PreservesAllFields()
    {
        var path = TempPath();
        try
        {
            var svc = new HifConditionPresetService(path);
            svc.Save(new List<HifConditionPreset> { BuildFullPreset() });
            var loaded = svc.Load();

            var p = Assert.Single(loaded);
            Assert.Equal("Vo全踏み", p.Name);

            // hif側
            Assert.Equal(2, p.Hif.Choices.Count);
            Assert.Equal("vo_lesson", p.Hif.Choices[0].Action);
            Assert.Equal("da", p.Hif.Choices[0].SubStat);
            Assert.Equal("outing", p.Hif.Choices[1].Action);
            Assert.Null(p.Hif.Choices[1].SubStat);
            var alloc = Assert.Single(p.Hif.ExamAllocations);
            Assert.Equal(5, alloc.Week);
            Assert.Equal(80, alloc.Vo);
            Assert.Equal(100, p.Hif.ExamRatioVo);
            Assert.Equal(0, p.Hif.ExamRatioDa);
            Assert.Equal(0, p.Hif.ExamRatioVi);
            Assert.Equal("vo", p.Hif.BulkMainStat);
            Assert.Equal("vi", p.Hif.BulkSubStat);
            Assert.Equal("da", p.Hif.BulkClassStat);

            // calc側
            Assert.Equal("logic", p.Calc.SelectedPlanType);
            Assert.Equal(2, p.Calc.VoSpCount);
            Assert.Equal(0, p.Calc.DaSpCount);
            Assert.Equal(1, p.Calc.ViSpCount);
            Assert.Equal(5, p.Calc.Counts.SkillAcquire);
            Assert.Equal(3, p.Calc.Counts.PDrinkAcquire);
            Assert.Equal(0, p.Calc.Counts.SkillEnhance);
            Assert.Equal("ロジック(好印象)", p.Calc.SelectedTemplateName);
            Assert.True(p.Calc.OwnedOnly);
            Assert.True(p.Calc.ContestMode);
            Assert.Equal(new List<string> { "0001", "0002" }, p.Calc.RequiredCardIds);
            Assert.Equal(new List<string> { "0003" }, p.Calc.ExcludedCardIds);
            var m = Assert.Single(p.Calc.MemoryBonuses);
            Assert.Equal(100, m.Vo.Value);
            Assert.Equal(MemoryBonusType.Flat, m.Vo.Type);
            Assert.Equal(3.5, m.Da.Value);
            Assert.Equal(MemoryBonusType.ParaBonus, m.Da.Type);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Service_MissingFile_ReturnsEmpty()
    {
        var svc = new HifConditionPresetService(TempPath());
        Assert.Empty(svc.Load());
    }

    [Fact]
    public void Service_BrokenYaml_ReturnsEmpty()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "presets: [ {name: 壊れた, hif: {");
            var svc = new HifConditionPresetService(path);
            Assert.Empty(svc.Load());
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Service_OldSchema_MissingFields_UseDefaults()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 旧バージョン相当: name と hif.choices のみ。未知フィールドは無視される
            File.WriteAllText(path, string.Join('\n', new[]
            {
                "presets:",
                "- name: 旧形式",
                "  hif:",
                "    choices:",
                "    - week: 1",
                "      action: vo_lesson",
                "      sub_stat: da",
                "  unknown_field: 123",
            }));
            var svc = new HifConditionPresetService(path);
            var p = Assert.Single(svc.Load());
            Assert.Equal("旧形式", p.Name);
            var choice = Assert.Single(p.Hif.Choices);
            Assert.Equal("vo_lesson", choice.Action);
            // 欠落セクション・欠落フィールドは C# 既定値で補完される
            Assert.Empty(p.Hif.ExamAllocations);
            Assert.Equal(34, p.Hif.ExamRatioVo);
            Assert.Equal("vo", p.Hif.BulkMainStat);
            Assert.NotNull(p.Calc);
            Assert.Equal("sense", p.Calc.SelectedPlanType);
            Assert.Empty(p.Calc.RequiredCardIds);
            Assert.Empty(p.Calc.MemoryBonuses);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void HifVm_CaptureApply_RoundTrip()
    {
        var source = new HifViewModel { HifPlan = RepoData.LoadPlan("hif") };
        // 一括設定でデフォルトから変更 (Da→Vi 全踏み、授業 Vi、試験 Da 全振り)
        source.BulkMainStat = "da";
        source.BulkSubStat = "vi";
        source.ApplyBulkLessonChoiceCommand.Execute(null);
        source.BulkClassStat = "vi";
        source.ApplyBulkClassChoiceCommand.Execute(null);
        source.ApplyExamRatio(0, 100, 0);

        var (choices, allocs) = source.CaptureScheduleState();
        Assert.NotEmpty(choices);
        Assert.NotEmpty(allocs);

        var target = new HifViewModel { HifPlan = RepoData.LoadPlan("hif") };
        target.ApplyScheduleState(choices, allocs);

        foreach (var (src, dst) in source.ScheduleItems.Zip(target.ScheduleItems))
        {
            Assert.Equal(src.Week, dst.Week);
            if (src.IsPublicLesson)
            {
                Assert.Equal(src.MainStat, dst.MainStat);
                Assert.Equal(src.SubStat, dst.SubStat);
            }
            else if (!src.IsFixed)
            {
                Assert.Equal(src.SelectedAction, dst.SelectedAction);
            }
            if (src.IsExam)
            {
                Assert.Equal(src.ExamVoAlloc, dst.ExamVoAlloc);
                Assert.Equal(src.ExamDaAlloc, dst.ExamDaAlloc);
                Assert.Equal(src.ExamViAlloc, dst.ExamViAlloc);
            }
        }

        // 適用した配分から代表比率が逆算されてバー表示に反映される
        Assert.Equal(0, target.ExamRatioVo);
        Assert.Equal(100, target.ExamRatioDa);
        Assert.Equal(0, target.ExamRatioVi);
    }
}
