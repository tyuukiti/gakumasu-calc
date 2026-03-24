namespace GakumasuCalc.Models;

public class CalculationResult
{
    public StatusValues FinalStatus { get; }
    public StatusValues BaseStatus { get; }
    public StatusValues SupportCardBonus { get; }
    public StatusValues AccumulatedGain { get; }
    public List<WeekBreakdown> WeekDetails { get; }

    public CalculationResult(
        StatusValues finalStatus,
        StatusValues baseStatus,
        StatusValues supportCardBonus,
        StatusValues accumulatedGain,
        List<WeekBreakdown> weekDetails)
    {
        FinalStatus = finalStatus;
        BaseStatus = baseStatus;
        SupportCardBonus = supportCardBonus;
        AccumulatedGain = accumulatedGain;
        WeekDetails = weekDetails;
    }
}

public class WeekBreakdown
{
    public int Week { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public StatusValues Gain { get; set; } = StatusValues.Zero;
}
