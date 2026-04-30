using System.Collections.ObjectModel;
using GakumasuCalc.Models;

namespace GakumasuCalc.ViewModels;

public class TurnChoiceViewModel : ViewModelBase
{
    private ActionType _selectedAction;
    private SupplyOption? _selectedSupply;

    public int Week { get; }
    public string WeekLabel => $"Week {Week}";
    public WeekSchedule Schedule { get; }
    public bool IsFixedEvent => Schedule.IsFixedEvent;
    public string? EventName => Schedule.EventName;
    public List<ActionType> AvailableActions { get; }
    public bool IsSupplyAvailable { get; }
    public ObservableCollection<SupplyOption> SupplyOptions { get; }

    public ActionType SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
            {
                OnPropertyChanged(nameof(IsSupplySelected));
            }
        }
    }

    public SupplyOption? SelectedSupply
    {
        get => _selectedSupply;
        set => SetProperty(ref _selectedSupply, value);
    }

    public bool IsSupplySelected => SelectedAction == ActionType.ActivitySupply;

    public TurnChoiceViewModel(WeekSchedule schedule, ActivitySupplyConfig? supplyConfig)
    {
        Week = schedule.Week;
        Schedule = schedule;

        // アクション一覧を構築
        AvailableActions = new List<ActionType>();
        foreach (var action in schedule.AvailableActions)
        {
            if (TryParseAction(action, out var actionType))
                AvailableActions.Add(actionType);
        }

        // デフォルト選択: 最初のレッスンがあればそれ、なければ先頭
        _selectedAction = AvailableActions.FirstOrDefault(a =>
            a is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson);
        if (!AvailableActions.Contains(_selectedAction) && AvailableActions.Count > 0)
            _selectedAction = AvailableActions[0];

        // 活動支給オプション
        IsSupplyAvailable = AvailableActions.Contains(ActionType.ActivitySupply);
        SupplyOptions = new ObservableCollection<SupplyOption>();
        if (IsSupplyAvailable && supplyConfig != null)
        {
            foreach (var option in supplyConfig.Options)
                SupplyOptions.Add(option);
            if (SupplyOptions.Count > 0)
                _selectedSupply = SupplyOptions[0];
        }
    }

    public TurnChoice ToTurnChoice()
    {
        return new TurnChoice
        {
            Week = Week,
            ChosenAction = IsFixedEvent ? ActionType.VoLesson : SelectedAction,
            SupplyOptionId = SelectedAction == ActionType.ActivitySupply
                ? SelectedSupply?.Id
                : null
        };
    }

    internal static bool TryParseAction(string actionStr, out ActionType actionType)
    {
        actionType = actionStr switch
        {
            "vo_lesson" => ActionType.VoLesson,
            "da_lesson" => ActionType.DaLesson,
            "vi_lesson" => ActionType.ViLesson,
            "vo_class" => ActionType.VoClass,
            "da_class" => ActionType.DaClass,
            "vi_class" => ActionType.ViClass,
            "outing" => ActionType.Outing,
            "rest" => ActionType.Rest,
            "consultation" => ActionType.Consultation,
            "activity_supply" => ActionType.ActivitySupply,
            "special_training" => ActionType.SpecialTraining,
            _ => ActionType.VoLesson
        };
        return actionStr is "vo_lesson" or "da_lesson" or "vi_lesson"
            or "vo_class" or "da_class" or "vi_class"
            or "outing" or "rest" or "consultation"
            or "activity_supply" or "special_training";
    }

    public static string ActionToDisplayName(ActionType action)
    {
        return action switch
        {
            ActionType.VoLesson => "Voレッスン",
            ActionType.DaLesson => "Daレッスン",
            ActionType.ViLesson => "Viレッスン",
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
