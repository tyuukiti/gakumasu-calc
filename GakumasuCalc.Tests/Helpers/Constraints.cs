using GakumasuCalc.Models;
using static GakumasuCalc.Services.CardScoringService;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>編成制約の述語群 (TS 版 constraints.ts と対応)。</summary>
public static class Constraints
{
    private static readonly HashSet<string> WildcardTypes = new() { "all", "as" };

    /// <summary>カードが指定属性の SP 率を持つか (enforceSpCounts の coversStat と同義)。</summary>
    public static bool CoversSpStat(SupportCard card, string stat) =>
        card.Effects.Any(e =>
            e.Trigger == "equip" && e.ValueType == "sp_rate" &&
            (e.Stat == stat || e.Stat == "all"));

    public static int CountSp(IEnumerable<SupportCard> cards, string stat) =>
        cards.Count(c => CoversSpStat(c, stat));

    public static int CountTypeSlotFillable(IEnumerable<SupportCard> cards, string stat) =>
        cards.Count(c => c.Type == stat || WildcardTypes.Contains(c.Type));

    public static bool HasNoDuplicates(List<SupportCard> cards) =>
        cards.Select(c => c.Id).Distinct().Count() == cards.Count;

    public static List<SupportCard> DeckCards(DeckResult deck) =>
        deck.SelectedCards.Select(cs => cs.Card).ToList();

    public static CardScore? RentalCard(DeckResult deck) =>
        deck.SelectedCards.FirstOrDefault(cs => cs.IsRental);
}
