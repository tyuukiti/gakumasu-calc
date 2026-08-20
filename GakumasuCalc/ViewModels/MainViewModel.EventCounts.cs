using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    // 追加カウントプロパティ
    public int PDrinkAcquire { get => _pDrinkAcquire; set => SetProperty(ref _pDrinkAcquire, value); }
    public int PItemAcquire { get => _pItemAcquire; set => SetProperty(ref _pItemAcquire, value); }
    public int SkillAcquire { get => _skillAcquire; set => SetProperty(ref _skillAcquire, value); }
    public int SkillSsrAcquire { get => _skillSsrAcquire; set => SetProperty(ref _skillSsrAcquire, value); }
    public int SkillEnhance { get => _skillEnhance; set => SetProperty(ref _skillEnhance, value); }
    public int SkillDelete { get => _skillDelete; set => SetProperty(ref _skillDelete, value); }
    public int SkillCustom { get => _skillCustom; set => SetProperty(ref _skillCustom, value); }
    public int SkillChange { get => _skillChange; set => SetProperty(ref _skillChange, value); }
    public int ActiveEnhance { get => _activeEnhance; set => SetProperty(ref _activeEnhance, value); }
    public int ActiveDelete { get => _activeDelete; set => SetProperty(ref _activeDelete, value); }
    public int MentalAcquire { get => _mentalAcquire; set => SetProperty(ref _mentalAcquire, value); }
    public int MentalEnhance { get => _mentalEnhance; set => SetProperty(ref _mentalEnhance, value); }
    public int MentalDelete { get => _mentalDelete; set => SetProperty(ref _mentalDelete, value); }
    public int ActiveAcquire { get => _activeAcquire; set => SetProperty(ref _activeAcquire, value); }
    public int GenkiAcquire { get => _genkiAcquire; set => SetProperty(ref _genkiAcquire, value); }
    public int GoodConditionAcquire { get => _goodConditionAcquire; set => SetProperty(ref _goodConditionAcquire, value); }
    public int GoodImpressionAcquire { get => _goodImpressionAcquire; set => SetProperty(ref _goodImpressionAcquire, value); }
    public int ConserveAcquire { get => _conserveAcquire; set => SetProperty(ref _conserveAcquire, value); }
    public int ConcentrateAcquire { get => _concentrateAcquire; set => SetProperty(ref _concentrateAcquire, value); }
    public int MotivationAcquire { get => _motivationAcquire; set => SetProperty(ref _motivationAcquire, value); }
    public int FullpowerAcquire { get => _fullpowerAcquire; set => SetProperty(ref _fullpowerAcquire, value); }
    public int AggressiveAcquire { get => _aggressiveAcquire; set => SetProperty(ref _aggressiveAcquire, value); }
    public int ConsultationDrink { get => _consultationDrink; set => SetProperty(ref _consultationDrink, value); }

    // イベント回数テンプレート
    private List<EventCountTemplate> _allEventCountTemplates = new();
    public ObservableCollection<EventCountTemplate> EventCountTemplates { get; } = new();
    /// <summary>HIFタブ用にplan_id="hif"でフィルタしたテンプレート (planType は共有)</summary>
    public ObservableCollection<EventCountTemplate> HifEventCountTemplates { get; } = new();

    private EventCountTemplate? _selectedHifEventTemplate;
    public EventCountTemplate? SelectedHifEventTemplate
    {
        get => _selectedHifEventTemplate;
        set
        {
            if (SetProperty(ref _selectedHifEventTemplate, value) && value != null)
            {
                ApplyEventTemplate(value);
            }
        }
    }

    private EventCountTemplate? _selectedEventTemplate;
    public EventCountTemplate? SelectedEventTemplate
    {
        get => _selectedEventTemplate;
        set
        {
            if (SetProperty(ref _selectedEventTemplate, value) && value != null)
            {
                ApplyEventTemplate(value);
                // 既に計算済みならターン選択を道中テンプレートで再適用
                // (Result != null 必須: タブ切替後は結果をクリア済みのため、
                //  別プランの古い _deckResults で再適用しない)
                if (Result != null && _deckResults.Count > 0 && SelectedPattern != null)
                    ApplySelectedPattern(SelectedPattern.Index);
            }
        }
    }

    private AdditionalCounts BuildAdditionalCounts()
    {
        return new AdditionalCounts
        {
            PDrinkAcquire = PDrinkAcquire,
            PItemAcquire = PItemAcquire,
            SkillAcquire = SkillAcquire,
            SkillSsrAcquire = SkillSsrAcquire,
            SkillEnhance = SkillEnhance,
            SkillDelete = SkillDelete,
            SkillCustom = SkillCustom,
            SkillChange = SkillChange,
            ActiveEnhance = ActiveEnhance,
            ActiveDelete = ActiveDelete,
            MentalAcquire = MentalAcquire,
            MentalEnhance = MentalEnhance,
            MentalDelete = MentalDelete,
            ActiveAcquire = ActiveAcquire,
            GenkiAcquire = GenkiAcquire,
            GoodConditionAcquire = GoodConditionAcquire,
            GoodImpressionAcquire = GoodImpressionAcquire,
            ConserveAcquire = ConserveAcquire,
            ConcentrateAcquire = ConcentrateAcquire,
            MotivationAcquire = MotivationAcquire,
            FullpowerAcquire = FullpowerAcquire,
            AggressiveAcquire = AggressiveAcquire,
            ConsultationDrink = ConsultationDrink,
        };
    }

    private void ApplyEventTemplate(EventCountTemplate template)
    {
        ApplyCounts(template.Counts);
        ApplyTemplateWeekActionsToSchedule(template);
    }

    /// <summary>
    /// 日程方式: テンプレートの week_actions をスケジュールへ反映する（活動支給軸/相談削除軸の切替）。
    /// 直後の再適用(SelectedEventTemplate セッター)が新日程を使えるようスナップショットも更新する。
    /// </summary>
    private void ApplyTemplateWeekActionsToSchedule(EventCountTemplate template)
    {
        if (!IsExplicitSchedulePlan || _selectedPlan == null) return;
        if (template.PlanId != _selectedPlan.Id) return;
        if (template.WeekActions == null || template.WeekActions.Count == 0) return;

        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent) continue;
            if (template.WeekActions.TryGetValue(tc.Week, out var actionStr)
                && TurnChoiceViewModel.TryParseAction(actionStr, out var action)
                && tc.AvailableActions.Contains(action))
            {
                tc.SelectedAction = action;
            }
        }
        CacheScheduleSelections();
        if (_isScheduleMode)
            _scheduleTurnChoices = TurnChoices.Where(t => !t.IsFixedEvent).Select(t => t.ToTurnChoice()).ToList();
    }

    /// <summary>AdditionalCounts の各値を入力欄プロパティへ反映する（テンプレート/プリセット共通）。</summary>
    private void ApplyCounts(AdditionalCounts c)
    {
        PDrinkAcquire = c.PDrinkAcquire;
        PItemAcquire = c.PItemAcquire;
        SkillAcquire = c.SkillAcquire;
        SkillSsrAcquire = c.SkillSsrAcquire;
        SkillEnhance = c.SkillEnhance;
        SkillDelete = c.SkillDelete;
        SkillCustom = c.SkillCustom;
        SkillChange = c.SkillChange;
        ActiveEnhance = c.ActiveEnhance;
        ActiveDelete = c.ActiveDelete;
        MentalAcquire = c.MentalAcquire;
        MentalEnhance = c.MentalEnhance;
        MentalDelete = c.MentalDelete;
        ActiveAcquire = c.ActiveAcquire;
        GenkiAcquire = c.GenkiAcquire;
        GoodConditionAcquire = c.GoodConditionAcquire;
        GoodImpressionAcquire = c.GoodImpressionAcquire;
        ConserveAcquire = c.ConserveAcquire;
        ConcentrateAcquire = c.ConcentrateAcquire;
        MotivationAcquire = c.MotivationAcquire;
        FullpowerAcquire = c.FullpowerAcquire;
        AggressiveAcquire = c.AggressiveAcquire;
        ConsultationDrink = c.ConsultationDrink;
    }

    private void LoadEventCountTemplates(YamlDataService yamlService, string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var file = yamlService.LoadFromFile<EventCountTemplateFile>(filePath);
                _allEventCountTemplates = file.Templates;
                FilterEventCountTemplates();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"テンプレート読み込みエラー: {ex.Message}");
        }
    }

    private void FilterEventCountTemplates()
    {
        var planId = _selectedPlan?.Id ?? string.Empty;
        var planTypeKeyword = _selectedPlanType switch
        {
            "sense" => "センス",
            "logic" => "ロジック",
            "anomaly" => "アノマリー",
            _ => null
        };

        EventCountTemplates.Clear();
        HifEventCountTemplates.Clear();
        foreach (var t in _allEventCountTemplates)
        {
            if (planTypeKeyword != null && !t.Name.Contains(planTypeKeyword)) continue;

            // 通常モード: 選択中プランで絞り込み (HIF プランのテンプレートは除外)
            if (!string.IsNullOrEmpty(t.PlanId) && t.PlanId == planId && t.PlanId != "hif")
                EventCountTemplates.Add(t);
            else if (string.IsNullOrEmpty(t.PlanId))
                EventCountTemplates.Add(t);

            // HIFタブ: plan_id="hif" のテンプレートのみ
            if (t.PlanId == "hif")
                HifEventCountTemplates.Add(t);
        }
        SelectedEventTemplate = null;
        SelectedHifEventTemplate = null;
    }
}
