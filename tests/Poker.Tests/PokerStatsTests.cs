using Poker.Arena;

namespace Poker.Tests;

public class PokerStatsTests
{
    private static readonly HandRecord[] Hands =
    [
        TestHands.Showdown(),
        TestHands.RaiseTakesIt(),
        TestHands.BigBlindFolds(),
        TestHands.BigBlindWins(),
    ];

    [Fact]
    public void Ann_ShowsTheNumbersWorkedOutByHand()
    {
        // Ann: hands 1 (calls 20), 2 (raises to 60), 3 (folds the big blind), 4 (checks the big blind).
        // VPIP 2 of 4; PFR 1 of 4. After the flop: hand 1 a call and a bet, hand 4 a bet: 2 bets, 1 call.
        // She sees the flop in hands 1 and 4 and shows down in both; she wins hand 4 only.
        var stats = PokerStats.For(Hands, "Ann");

        stats.Should().Be(new BotStats(4, Vpip: 0.5, Pfr: 0.25, AggressionFactor: 2.0, WentToShowdown: 1.0, WonAtShowdown: 0.5));
    }

    [Fact]
    public void Bob_ShowsTheNumbersWorkedOutByHand()
    {
        // Bob: raises in hand 1 and 3, calls in hand 4, folds in hand 2: VPIP 3 of 4, PFR 2 of 4.
        // After the flop: a bet and a call in hand 1: AF 1. Flops in hands 1 and 4; showdown only in hand 1, which he wins.
        var stats = PokerStats.For(Hands, "Bob");

        stats.Should().Be(new BotStats(4, Vpip: 0.75, Pfr: 0.5, AggressionFactor: 1.0, WentToShowdown: 0.5, WonAtShowdown: 1.0));
    }

    [Fact]
    public void ABotThatNeverPlayed_HasAllZeros() =>
        PokerStats.For(Hands, "Nobody").Should().Be(new BotStats(0, 0, 0, null, 0, 0));

    [Fact]
    public void ABotWithoutCalls_HasNoAggressionFactor() =>
        PokerStats.For([TestHands.BigBlindWins()], "Ann").AggressionFactor.Should().BeNull();

    [Fact]
    public void ABotWhoseCardsAreHidden_StillCountsItsHands()
    {
        // A downloaded match keeps an opponent's folded cards on the server; the hands it played still count.
        var hidden = Hands.Select(h => h with { Holes = [h.Holes[0], [], .. h.Holes.Skip(2)] }).ToArray();

        PokerStats.For(hidden, "Bob").Should().Be(PokerStats.For(Hands, "Bob"));
    }
}
