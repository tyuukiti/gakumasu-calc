using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// HIFボーナスレベル設定の永続化サービス。
/// 保存先は Data/HifBonusLevels/hif_bonus_levels.yaml (リポジトリ管理外)。
/// </summary>
public class HifBonusLevelsService
{
    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public HifBonusLevelsService(string path)
    {
        _path = path;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public HifBonusLevels Load()
    {
        if (!File.Exists(_path))
            return new HifBonusLevels(); // デフォルト MAX

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<HifBonusLevelsFile>(yaml);
            return file?.Levels ?? new HifBonusLevels();
        }
        catch
        {
            return new HifBonusLevels();
        }
    }

    public void Save(HifBonusLevels levels)
    {
        var file = new HifBonusLevelsFile { Levels = levels };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
