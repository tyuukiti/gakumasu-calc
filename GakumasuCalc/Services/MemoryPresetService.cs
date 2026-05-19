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
            // 旧バージョンが書き込んだ計算プロパティ (is_empty) を除去してデシリアライズ
            yaml = StripReadOnlyFields(yaml);
            var file = _deserializer.Deserialize<MemoryPresetFile>(yaml);
            return file?.Presets ?? new List<MemoryPreset>();
        }
        catch (Exception ex)
        {
            // 破損ファイルが残っていても起動を妨げない
            System.Diagnostics.Debug.WriteLine($"MemoryPreset 読込失敗: {ex}");
            return new List<MemoryPreset>();
        }
    }

    /// <summary>
    /// 旧バージョンで誤って書き出された計算プロパティの行を YAML から削除する。
    /// </summary>
    private static string StripReadOnlyFields(string yaml)
    {
        var lines = yaml.Split('\n');
        var keep = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            // "is_empty:" を含む行は破棄 (インデント問わず)
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*is_empty\s*:")) continue;
            keep.Add(line);
        }
        return string.Join('\n', keep);
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
