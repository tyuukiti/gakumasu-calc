namespace GakumasuCalc.Models;

public class TurnChoice
{
    public int Week { get; set; }
    public ActionType ChosenAction { get; set; } = ActionType.VoLesson;
    public string? SupplyOptionId { get; set; }
}
