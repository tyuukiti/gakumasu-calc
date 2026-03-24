using System.IO;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SupportCardEditor.Services;

public class YamlExportService
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public YamlExportService()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// カード1枚分のYAML文字列を生成
    /// </summary>
    public string SerializeCard(SupportCard card)
    {
        var file = new SupportCardFile { SupportCards = new List<SupportCard> { card } };
        return _serializer.Serialize(file);
    }

    /// <summary>
    /// 既存YAMLファイルにカードを追記。ファイルが無ければ新規作成。
    /// </summary>
    public void AppendToFile(string filePath, SupportCard card)
    {
        SupportCardFile file;

        if (File.Exists(filePath))
        {
            var yaml = File.ReadAllText(filePath);
            file = _deserializer.Deserialize<SupportCardFile>(yaml) ?? new SupportCardFile();
        }
        else
        {
            file = new SupportCardFile();
        }

        // 同一IDのカードがあれば上書き、なければ追加
        var existing = file.SupportCards.FindIndex(c => c.Id == card.Id);
        if (existing >= 0)
            file.SupportCards[existing] = card;
        else
            file.SupportCards.Add(card);

        var output = _serializer.Serialize(file);
        File.WriteAllText(filePath, output);
    }
}
