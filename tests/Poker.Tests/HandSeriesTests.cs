namespace Poker.Tests;

public class HandSeriesTests
{
    [Fact]
    public void TheSameSeries_DealsTheSameCards()
    {
        var first = PlayOut(new HandSeries(6, seed: 9).Deal(4));
        var second = PlayOut(new HandSeries(6, seed: 9).Deal(4));

        first.Hole.Should().BeEquivalentTo(second.Hole, options => options.WithStrictOrdering());
        first.Board.Should().Equal(second.Board);
        first.Board.Should().HaveCount(5);
    }

    [Fact]
    public void EveryHand_StartsWith100BigBlinds_AndTheButtonMoves()
    {
        var series = new HandSeries(6, seed: 9);

        series.BigBlind.Should().Be(20);
        foreach (var index in new[] { 0, 1, 2 })
        {
            var hand = series.Deal(index);
            hand.Button.Should().Be(index);
            Enumerable.Range(0, 6).Should().OnlyContain(s => hand.Stack(s) + hand.Committed(s) == 2000);
            hand.BigBlind.Should().Be(20);
        }
    }

    [Fact]
    public void DifferentHands_AreDealtDifferently()
    {
        var series = new HandSeries(6, seed: 9);

        series.Deal(1).Hole(0).Should().NotEqual(series.Deal(2).Hole(0));
    }

    private static (IReadOnlyList<IReadOnlyList<Jentl.Cards.Card>> Hole, IReadOnlyList<Jentl.Cards.Card> Board) PlayOut(HoldemHand hand)
    {
        var hole = Enumerable.Range(0, hand.Seats).Select(hand.Hole).ToArray();
        while (!hand.IsOver)
        {
            hand.Apply(hand.CurrentSeat, hand.LegalMoves(hand.CurrentSeat).Contains(Check.Instance) ? Check.Instance : CallBet.Instance);
        }

        return (hole, [.. hand.Board]);
    }
}
