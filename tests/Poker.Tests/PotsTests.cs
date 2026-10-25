namespace Poker.Tests;

public class PotsTests
{
    [Fact]
    public void Pots_WithThreeAllInTiers_AndAFoldedStake_MatchThePayout()
    {
        var hand = new HoldemHand([100, 300, 600, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(3));

        hand.Apply(3, CallBet.Instance);
        hand.Apply(4, CallBet.Instance);
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, AllIn.Instance);
        hand.Apply(2, AllIn.Instance);
        hand.Apply(3, CallBet.Instance);
        hand.Apply(4, Fold.Instance);

        hand.IsOver.Should().BeTrue();
        hand.Pots.Select(p => p.Amount).Should().Equal(420, 600, 600);
        hand.Pots.Select(p => string.Join(",", p.Seats)).Should().Equal("0,1,2,3", "1,2,3", "2,3");
        hand.Winnings.Values.Sum().Should().Be(hand.Pots.Sum(p => p.Amount));
        hand.Winnings.Keys.Should().NotContain(4, "the folded player cannot win");
    }

    [Fact]
    public void Pots_WithAllInsOfDifferentSizes_AndAFold_SplitOffTheUncalledChips()
    {
        var hand = new HoldemHand([100, 300, 600, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(3));

        hand.Apply(3, new RaiseTo(400));
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, AllIn.Instance);
        hand.Apply(2, Fold.Instance);

        hand.IsOver.Should().BeTrue();
        hand.Pots.Select(p => p.Amount).Should().Equal(320, 400, 100);
        hand.Pots.Select(p => string.Join(",", p.Seats)).Should().Equal("0,1,3", "1,3", "3");
        hand.Winnings.Values.Sum().Should().Be(hand.Pots.Sum(p => p.Amount));
    }

    [Fact]
    public void Pots_BeforeAnyAllIn_IsOnePot()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(1));

        hand.Pots.Should().ContainSingle();
        hand.Pots[0].Amount.Should().Be(30);
        hand.Pots[0].Seats.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Pots_WhenOnePlayerIsAllInForLess_ShowASidePotWhileTheHandIsStillOn()
    {
        var hand = new HoldemHand([100, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(1));

        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, new RaiseTo(300));

        hand.Pots.Select(p => p.Amount).Should().Equal(220, 200);
        hand.Pots[0].Seats.Should().Equal(0, 1, 2);
        hand.Pots[1].Seats.Should().Equal(1, 2);
    }

    [Fact]
    public void ATie_WithFoldedStakes_SplitsTheMergedPotOnce_TheOddChipGoingLeftOfTheButton()
    {
        // Seats 0 and 3 are all in for 100 and tie on the board; seats 1 and 2 fold after posting 7 and 10.
        var deck = HandEvaluatorTests.Cards("4C 4D 2D 2C 5C 5D 3D 3C AS KS QS JS 10S");
        var hand = new HoldemHand([100, 1000, 1000, 100], button: 0, smallBlind: 7, bigBlind: 10, deck);

        hand.Apply(3, AllIn.Instance);
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, Fold.Instance);
        hand.Apply(2, Fold.Instance);

        hand.Pots.Should().ContainSingle().Which.Amount.Should().Be(217);
        hand.Winnings.Should().Equal(new Dictionary<int, int> { [3] = 109, [0] = 108 });
    }

    [Fact]
    public void GatheredPots_AreThePotsAsTheStreetBegan_WithoutTheBetsInFrontOfTheSeats()
    {
        var hand = new HoldemHand([100, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(1));
        hand.GatheredPots.Should().BeEmpty("before the flop the blinds are bets");

        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, CallBet.Instance);
        hand.Apply(2, CallBet.Instance);
        hand.Street.Should().Be(Street.Flop);
        hand.GatheredPots.Select(p => p.Amount).Should().Equal(300);

        hand.Apply(1, new RaiseTo(200));
        hand.GatheredPots.Select(p => p.Amount).Should().Equal(300);
        hand.Pots.Sum(p => p.Amount).Should().Be(500);
        hand.Copy().GatheredPots.Should().Equal(hand.GatheredPots);
    }
}
