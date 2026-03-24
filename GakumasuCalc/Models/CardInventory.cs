using YamlDotNet.Serialization;

namespace GakumasuCalc.Models;

public class CardInventoryFile
{
    [YamlMember(Alias = "inventory")]
    public List<CardInventoryEntry> Inventory { get; set; } = new();
}

public class CardInventoryEntry
{
    [YamlMember(Alias = "card_id")]
    public string CardId { get; set; } = string.Empty;

    [YamlMember(Alias = "owned")]
    public bool Owned { get; set; }

    /// <summary>凸数 (0〜4)</summary>
    [YamlMember(Alias = "uncap")]
    public int Uncap { get; set; }
}
