using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

public class YamlDataService
{
    private readonly IDeserializer _deserializer;

    public YamlDataService()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public T LoadFromFile<T>(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return _deserializer.Deserialize<T>(yaml);
    }

    public List<T> LoadAllFromDirectory<T>(string directoryPath)
    {
        var results = new List<T>();
        if (!Directory.Exists(directoryPath))
            return results;

        foreach (var file in Directory.GetFiles(directoryPath, "*.yaml"))
        {
            try
            {
                var item = LoadFromFile<T>(file);
                results.Add(item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YAML読み込みエラー ({file}): {ex.Message}");
            }
        }
        return results;
    }
}
