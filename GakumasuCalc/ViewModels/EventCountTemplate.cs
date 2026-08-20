using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public class EventCountTemplate
{
    public string Name { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public AdditionalCounts Counts { get; set; } = new();
    public Dictionary<int, string>? WeekActions { get; set; }

    public override string ToString() => Name;
}

public class EventCountTemplateFile
{
    public List<EventCountTemplate> Templates { get; set; } = new();
}
