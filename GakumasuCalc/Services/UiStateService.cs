using System.IO;
using System.Text;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

/// <summary>
/// UI表示状態 (スケジュール個別調整パネルの高さ等) の永続化サービス。
/// 保存先は Data/UiState/ui_state.yaml (リポジトリ管理外)。
/// </summary>
public class UiStateService
{
    private readonly string _path;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;
    private readonly UiStateFile _state;

    public UiStateService(string path)
    {
        _path = path;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _state = Load();
    }

    /// <summary>保存済みのパネル高さを返す。未保存・不正値なら defaultHeight。</summary>
    public double GetPanelHeight(string key, double defaultHeight)
        => _state.PanelHeights.TryGetValue(key, out var h) && h > 0 ? h : defaultHeight;

    /// <summary>パネル高さを記憶してファイルへ保存する。</summary>
    public void SavePanelHeight(string key, double height)
    {
        _state.PanelHeights[key] = Math.Round(height);
        Save();
    }

    private UiStateFile Load()
    {
        if (!File.Exists(_path))
            return new UiStateFile();

        try
        {
            var yaml = File.ReadAllText(_path, Encoding.UTF8);
            return _deserializer.Deserialize<UiStateFile>(yaml) ?? new UiStateFile();
        }
        catch
        {
            return new UiStateFile();
        }
    }

    private void Save()
    {
        var yaml = _serializer.Serialize(_state);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, yaml, Encoding.UTF8);
    }
}
