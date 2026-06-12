using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// 日程方式 (初レジェンド / NIA) のスケジュールプリセット永続化サービス。
/// 保存先はプランごとに別ファイル Data/SchedulePresets/{plan}.yaml (リポジトリ管理外)。
/// </summary>
public class SchedulePresetService
{
    /// <summary>保存可能なプリセットの最大件数。</summary>
    public const int MaxPresets = 10;

    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public SchedulePresetService(string path)
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

    public List<SchedulePreset> Load()
    {
        if (!File.Exists(_path))
            return new List<SchedulePreset>();

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<SchedulePresetFile>(yaml);
            return file?.Presets ?? new List<SchedulePreset>();
        }
        catch
        {
            return new List<SchedulePreset>();
        }
    }

    public void Save(List<SchedulePreset> presets)
    {
        var file = new SchedulePresetFile { Presets = presets };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
