using Poker.Arena;

namespace Poker.Tests;

public class HandHistoryTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 14, 3, 9, TimeSpan.FromHours(2));

    private static string[] Lines(HandRecord hand) =>
        HandHistoryText.Write(hand, "Workshop", At).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void AShowdownHand_IsWrittenInPokerStarsStyle()
    {
        Lines(TestHands.Showdown()).Should().Equal(
            "PokerStars Hand #71: Hold'em No Limit (10/20) - 2026/10/25 12:03:09 UTC",
            "Table 'Workshop' 9-max Seat #1 is the button",
            "Seat 1: Ann (1000 in chips)",
            "Seat 2: Bob (1000 in chips)",
            "Seat 3: Cy (1000 in chips)",
            "Bob: posts small blind 10",
            "Cy: posts big blind 20",
            "*** HOLE CARDS ***",
            "Dealt to Ann [Ac Jc]",
            "Dealt to Bob [Ks Kh]",
            "Dealt to Cy [5s 4s]",
            "Ann: calls 20",
            "Bob: raises 40 to 60",
            "Cy: folds",
            "Ann: calls 40",
            "*** FLOP *** [Ah Kd 2c]",
            "Bob: bets 100",
            "Ann: calls 100",
            "*** TURN *** [Ah Kd 2c] [7s]",
            "Bob: checks",
            "Ann: bets 200",
            "Bob: calls 200",
            "*** RIVER *** [Ah Kd 2c 7s] [9d]",
            "Bob: checks",
            "Ann: checks",
            "*** SHOW DOWN ***",
            "Ann: shows [Ac Jc] (Pair of aces)",
            "Bob: shows [Ks Kh] (Three kings)",
            "Bob collected 740 from pot",
            "*** SUMMARY ***",
            "Total pot 740 | Rake 0",
            "Board [Ah Kd 2c 7s 9d]",
            "Seat 1: Ann (button) showed [Ac Jc] and lost",
            "Seat 2: Bob (small blind) showed [Ks Kh] and won (740)",
            "Seat 3: Cy (big blind) folded");
    }

    [Fact]
    public void AnAllInHeadsUp_HasTheButtonPostTheSmallBlind_AndReturnsTheUncalledChips()
    {
        var lines = Lines(TestHands.AllInCall());

        lines.Should().ContainInOrder(
            "Table 'Workshop' 9-max Seat #1 is the button",
            "Dee: posts small blind 10",
            "Eli: posts big blind 20",
            "*** HOLE CARDS ***",
            "Dealt to Dee [Ah Ad]",
            "Dealt to Eli [Kc Kd]",
            "Dee: raises 80 to 100 and is all-in",
            "Eli: calls 30 and is all-in",
            "Uncalled bet (50) returned to Dee",
            "*** FLOP *** [Ks 2h 7c]",
            "*** TURN *** [Ks 2h 7c] [9d]",
            "*** RIVER *** [Ks 2h 7c 9d] [3s]",
            "*** SHOW DOWN ***",
            "Dee: shows [Ah Ad] (Pair of aces)",
            "Eli: shows [Kc Kd] (Three kings)",
            "Eli collected 100 from pot",
            "*** SUMMARY ***",
            "Total pot 100 | Rake 0");
    }

    [Fact]
    public void AHandWonBeforeTheFlop_HasNoBoardAndNoShowdown()
    {
        var lines = Lines(TestHands.RaiseTakesIt());

        lines.Should().Contain("Ann: raises 40 to 60").And.Contain("Uncalled bet (40) returned to Ann")
            .And.Contain("Ann collected 50 from pot").And.Contain("Total pot 50 | Rake 0");
        lines.Should().NotContain(l => l.StartsWith("*** FLOP") || l.StartsWith("*** SHOW DOWN") || l.StartsWith("Board"));
    }

    [Fact]
    public void ABlindThatIsShortOfChips_IsWrittenAsAllIn()
    {
        var hand = TestHands.Hand(
            5,
            button: 0,
            [100, 100, 15],
            ["Ah Ad", "7c 2d", "9s 4s"],
            [
                Act(Street.Preflop, 0, Fold.Instance),
                Act(Street.Preflop, 1, Fold.Instance),
            ],
            string.Empty,
            [],
            new() { [2] = 25 },
            bigBlindPosted: 15);

        Lines(hand).Should().Contain("Cy: posts big blind 15 and is all-in");
    }

    [Fact]
    public void TheHandNumberInTheHeader_IsNeverNegative()
    {
        var hand = TestHands.Hand(3, button: 0, [1000, 1000, 1000], ["Ah Ad", "7c 2d", "9s 4s"], [], string.Empty, [], new() { [0] = 30 }, seed: -5);

        Lines(hand)[0].Should().Be("PokerStars Hand #42949672913: Hold'em No Limit (10/20) - 2026/10/25 12:03:09 UTC");
    }

    [Fact]
    public void ACallOfNothing_IsNeverWrittenAsCallsZero()
    {
        var hand = TestHands.Hand(
            3,
            button: 0,
            [1000, 1000, 1000],
            ["Ah Ad", "7c 2d", "9s 4s"],
            [TestHands.Act(Street.Preflop, 0, CallBet.Instance, 0, 20)],
            string.Empty,
            [],
            new() { [0] = 30 });

        Lines(hand).Should().NotContain(l => l.Contains("calls 0", StringComparison.Ordinal)).And.Contain("Ann: checks");
    }

    private static ActionRecord Act(Street street, int seat, PokerAction action) => TestHands.Act(street, seat, action, 0, 0);
}
