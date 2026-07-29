using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// HIF条件プリセットの永続化サービス。
/// 保存先は Data/HifConditionPresets/hif_condition_presets.yaml（リポジトリ管理外、ユーザー固有）。
/// </summary>
public class HifConditionPresetService
{
    /// <summary>保存可能なプリセットの最大件数。</summary>
    public const int MaxPresets = 10;

    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public HifConditionPresetService(string path)
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

    public List<HifConditionPreset> Load()
    {
        if (!File.Exists(_path))
            return new List<HifConditionPreset>();

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<HifConditionPresetFile>(yaml);
            return file?.Presets ?? new List<HifConditionPreset>();
        }
        catch (Exception ex)
        {
            // 破損ファイルが残っていても起動を妨げない
            System.Diagnostics.Debug.WriteLine($"HifConditionPreset 読込失敗: {ex}");
            return new List<HifConditionPreset>();
        }
    }

    public void Save(List<HifConditionPreset> presets)
    {
        var file = new HifConditionPresetFile { Presets = presets };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
