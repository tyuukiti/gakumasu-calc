using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class SupportCardLoaderService
{
    private readonly YamlDataService _yamlService;
    private readonly string _cardsDirectory;

    public SupportCardLoaderService(YamlDataService yamlService, string cardsDirectory)
    {
        _yamlService = yamlService;
        _cardsDirectory = cardsDirectory;
    }

    public List<SupportCard> LoadAllCards()
    {
        var files = _yamlService.LoadAllFromDirectory<SupportCardFile>(_cardsDirectory);
        return files.SelectMany(f => f.SupportCards).ToList();
    }
}
