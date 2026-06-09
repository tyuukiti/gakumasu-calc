using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.Tests.Helpers;
using static GakumasuCalc.Tests.Helpers.Factories;

namespace GakumasuCalc.Tests;

/// <summary>
/// L1/L3: レンタル枠割当の不変条件 (TS 版 cardScoring.rental.test.ts と対応)。
/// レンタル枠は「どの 1 枚を 4 凸借用するか」。4 凸所持カードを浪費せず、借用すべき強カードを選ぶ。
/// 参照: feedback_rental_slot_assignment
/// </summary>
public class RentalTests
{
    private static readonly CardScoringService Svc = new();
    private static Dictionary<string, int> Alloc() => new() { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };

    private static (List<SupportCard> owned, SupportCard u1, List<SupportCard> rentalPool, Dictionary<string, int> uncap) Setup()
    {
        ResetIds();
        var owned = new List<SupportCard>
        {
            MakeCard(new CardSpec { Id = "O1", Type = "vo", Vo = 200 }),
            MakeCard(new CardSpec { Id = "O2", Type = "vo", Vo = 180 }),
            MakeCard(new CardSpec { Id = "O3", Type = "da", Da = 170 }),
            MakeCard(new CardSpec { Id = "O4", Type = "da", Da = 160 }),
            MakeCard(new CardSpec { Id = "O5", Type = "vi", Vi = 150 }),
            MakeCard(new CardSpec { Id = "O6", Type = "vi", Vi = 100 }),
        };
        var u1 = MakeCard(new CardSpec { Id = "U1", Type = "vo", Vo = 400 }); // 未所持・最強
        var rentalPool = owned.Concat(new[] { u1 }).ToList();
        var uncap = new Dictionary<string, int>();
        foreach (var c in rentalPool) uncap[c.Id] = 4;
        return (owned, u1, rentalPool, uncap);
    }

    [Fact]
    public void レンタルモードでデッキは6枚_レンタル枠1_重複なし()
    {
        var (owned, _, rentalPool, uncap) = Setup();
        var plan = MakePlan(new PlanSpec { StatusLimit = 99999 });
        var deck = Svc.SelectOptimalDeck(
            plan, owned, Alloc(), new(), new() { "vo", "da" },
            null, null, null, uncap, rentalPool, 5);

        var cards = Constraints.DeckCards(deck);
        Assert.Equal(6, cards.Count);
        Assert.True(Constraints.HasNoDuplicates(cards));
        Assert.Equal(1, deck.SelectedCards.Count(c => c.IsRental));
    }

    [Fact]
    public void 未所持の強カードを借用し4凸所持カードを浪費しない()
    {
        var (owned, _, rentalPool, uncap) = Setup();
        var plan = MakePlan(new PlanSpec { StatusLimit = 99999 });
        var deck = Svc.SelectOptimalDeck(
            plan, owned, Alloc(), new(), new() { "vo", "da" },
            null, null, null, uncap, rentalPool, 5);

        var rental = Constraints.RentalCard(deck);
        Assert.NotNull(rental);
        Assert.Equal("U1", rental!.Card.Id);
        var ownedSlotIds = deck.SelectedCards.Where(c => !c.IsRental).Select(c => c.Card.Id).ToList();
        Assert.DoesNotContain("U1", ownedSlotIds);
    }
}
