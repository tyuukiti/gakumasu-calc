using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// HIFスケジュール調整プリセットの永続化サービス。
/// 保存先は Data/HifSchedulePresets/hif_schedule_presets.yaml (リポジトリ管理外)。
/// </summary>
public class HifSchedulePresetService
{
    /// <summary>保存可能なプリセットの最大件数。</summary>
    public const int MaxPresets = 10;

    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public HifSchedulePresetService(string path)
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

    public List<HifSchedulePreset> Load()
    {
        if (!File.Exists(_path))
            return new List<HifSchedulePreset>();

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<HifSchedulePresetFile>(yaml);
            return file?.Presets ?? new List<HifSchedulePreset>();
        }
        catch
        {
            return new List<HifSchedulePreset>();
        }
    }

    public void Save(List<HifSchedulePreset> presets)
    {
        var file = new HifSchedulePresetFile { Presets = presets };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
