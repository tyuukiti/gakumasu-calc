using System.IO;
using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public class CharacterLoaderService
{
    private readonly YamlDataService _yamlService;
    private readonly string _filePath;

    public CharacterLoaderService(YamlDataService yamlService, string charactersDirectory)
    {
        _yamlService = yamlService;
        _filePath = Path.Combine(charactersDirectory, "characters.yaml");
    }

    public List<Character> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<Character>();

        try
        {
            var file = _yamlService.LoadFromFile<CharacterFile>(_filePath);
            return file?.Characters ?? new List<Character>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"キャラデータ読み込みエラー: {ex.Message}");
            return new List<Character>();
        }
    }
}
