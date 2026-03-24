using System.IO;
using GakumasuCalc.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GakumasuCalc.Services;

public class InventoryService
{
    private readonly string _inventoryPath;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public InventoryService(string inventoryPath)
    {
        _inventoryPath = inventoryPath;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public List<CardInventoryEntry> Load()
    {
        if (!File.Exists(_inventoryPath))
            return new List<CardInventoryEntry>();

        var yaml = File.ReadAllText(_inventoryPath);
        var file = _deserializer.Deserialize<CardInventoryFile>(yaml);
        return file?.Inventory ?? new List<CardInventoryEntry>();
    }

    public void Save(List<CardInventoryEntry> entries)
    {
        var file = new CardInventoryFile { Inventory = entries };
        var yaml = _serializer.Serialize(file);
        var dir = Path.GetDirectoryName(_inventoryPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_inventoryPath, yaml);
    }

    /// <summary>
    /// 全カードリストからインベントリを初期化（未登録カードを未所持で追加）
    /// </summary>
    public List<CardInventoryEntry> InitializeFromCards(
        List<SupportCard> allCards, List<CardInventoryEntry> existing)
    {
        var existingIds = existing.ToDictionary(e => e.CardId);
        var result = new List<CardInventoryEntry>();

        foreach (var card in allCards)
        {
            if (existingIds.TryGetValue(card.Id, out var entry))
                result.Add(entry);
            else
                result.Add(new CardInventoryEntry
                {
                    CardId = card.Id,
                    Owned = false,
                    Uncap = 4
                });
        }

        return result;
    }
}
