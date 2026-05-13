using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// 持ち込みメモリー プリセットの永続化サービス。
/// 保存先は Data/MemoryPresets/memory_presets.yaml（リポジトリ管理外、ユーザー固有）。
/// </summary>
public class MemoryPresetService
{
    /// <summary>保存可能なプリセットの最大件数。</summary>
    public const int MaxPresets = 5;

    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public MemoryPresetService(string path)
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

    public List<MemoryPreset> Load()
    {
        if (!File.Exists(_path))
            return new List<MemoryPreset>();

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<MemoryPresetFile>(yaml);
            return file?.Presets ?? new List<MemoryPreset>();
        }
        catch
        {
            // 破損ファイルが残っていても起動を妨げない
            return new List<MemoryPreset>();
        }
    }

    public void Save(List<MemoryPreset> presets)
    {
        var file = new MemoryPresetFile { Presets = presets };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
