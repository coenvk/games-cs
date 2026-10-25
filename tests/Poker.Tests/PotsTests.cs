namespace Poker.Tests;

public class PotsTests
{
    [Fact]
    public void Pots_WithThreeAllInsOfDifferentSizes_AndAFoldedStake()
    {
        var hand = new HoldemHand([100, 300, 600, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 3);

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
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.Pots.Should().ContainSingle();
        hand.Pots[0].Amount.Should().Be(30);
        hand.Pots[0].Seats.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Pots_WhenOnePlayerIsAllInForLess_ShowASidePotWhileTheHandIsStillOn()
    {
        var hand = new HoldemHand([100, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, new RaiseTo(300));

        hand.Pots.Select(p => p.Amount).Should().Equal(220, 200);
        hand.Pots[0].Seats.Should().Equal(0, 1, 2);
        hand.Pots[1].Seats.Should().Equal(1, 2);
    }
}
