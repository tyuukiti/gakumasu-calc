using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class PlanLoaderService
{
    private readonly YamlDataService _yamlService;
    private readonly string _plansDirectory;

    public PlanLoaderService(YamlDataService yamlService, string plansDirectory)
    {
        _yamlService = yamlService;
        _plansDirectory = plansDirectory;
    }

    public List<TrainingPlan> LoadAllPlans()
    {
        var files = _yamlService.LoadAllFromDirectory<TrainingPlanFile>(_plansDirectory);
        return files.Select(f => f.Plan).ToList();
    }
}
