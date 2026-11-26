using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// HIF カスタムPアイテム (ポーチ) プリセットの永続化サービス。
/// 保存先は Data/HifPouchPresets/hif_pouch_presets.yaml (リポジトリ管理外)。
/// ファイルが無い (初回) ときは Wiki の 3 ルートを初期プリセットとして使い、以後はユーザが上書き・削除できる。
/// </summary>
public class HifPouchPresetService
{
    /// <summary>保存可能なプリセットの最大件数。</summary>
    public const int MaxPresets = 10;

    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public HifPouchPresetService(string path)
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

    /// <summary>初期プリセット (Wiki の 3 ルート)。Web版 HIF_POUCH_DEFAULT_PRESETS と対。</summary>
    public static List<HifPouchPreset> DefaultPresets() =>
        HifPouchService.DefaultPresets
            .Select(p => new HifPouchPreset { Name = p.Name, Color = p.Color, Plush = p.Plush, Decoration = p.Deco })
            .ToList();

    /// <summary>
    /// 保存済みプリセットを読み込む。ファイルが無ければ null (初期プリセットを使う合図)。
    /// 全削除して空で保存した状態は空リストとしてそのまま尊重する。
    /// </summary>
    public List<HifPouchPreset>? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            var file = _deserializer.Deserialize<HifPouchPresetFile>(yaml);
            return file?.Presets ?? new List<HifPouchPreset>();
        }
        catch
        {
            return new List<HifPouchPreset>();
        }
    }

    public void Save(List<HifPouchPreset> presets)
    {
        var file = new HifPouchPresetFile { Presets = presets };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
