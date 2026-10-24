using Jentl.Cards;
using Jentl.Multiplayer;

namespace Poker.Tests;

public class HoldemTests
{
    [Fact]
    public void Blinds_ArePosted_AndPlayStartsAfterTheBigBlind()
    {
        var hand = new HoldemHand([1000, 1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.SmallBlindSeat.Should().Be(1);
        hand.BigBlindSeat.Should().Be(2);
        hand.Bet(1).Should().Be(10);
        hand.Bet(2).Should().Be(20);
        hand.CurrentSeat.Should().Be(3);
        hand.Pot.Should().Be(30);
        Enumerable.Range(0, 4).Should().OnlyContain(s => hand.Hole(s).Count == 2);
    }

    [Fact]
    public void HeadsUp_TheButtonPostsTheSmallBlind_AndActsFirstBeforeTheFlop()
    {
        var hand = new HoldemHand([1000, 1000], button: 1, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.SmallBlindSeat.Should().Be(1);
        hand.CurrentSeat.Should().Be(1);
        hand.Apply(1, CallBet.Instance);
        hand.CurrentSeat.Should().Be(0, "the big blind has the option");
        hand.LegalMoves(0).Should().Contain(Check.Instance).And.NotContain(Fold.Instance);
        hand.Apply(0, Check.Instance);

        hand.Street.Should().Be(Street.Flop);
        hand.Board.Should().HaveCount(3);
        hand.CurrentSeat.Should().Be(0, "after the flop the player after the button starts");
    }

    [Fact]
    public void WhenEveryoneElseFolds_TheLastPlayerTakesThePot()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.Apply(0, new RaiseTo(60));
        hand.Apply(1, Fold.Instance);
        hand.Apply(2, Fold.Instance);

        hand.IsOver.Should().BeTrue();
        hand.Winnings.Should().Equal(new Dictionary<int, int> { [0] = 90 });
        Enumerable.Range(0, 3).Select(hand.Stack).Should().Equal(1030, 990, 980);
        hand.TakeEvents().OfType<PotWon>().Single().Value.Should().BeNull("nobody showed");
    }

    [Fact]
    public void Raises_MustBeAtLeastTheLastRaise()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        hand.MinRaiseTo.Should().Be(40);
        var tooSmall = () => hand.Apply(0, new RaiseTo(30));
        tooSmall.Should().Throw<InvalidOperationException>();

        hand.Apply(0, new RaiseTo(100));
        hand.MinRaiseTo.Should().Be(180, "the last raise was 80");
        hand.Apply(1, new RaiseTo(180));
        hand.CurrentSeat.Should().Be(2);
        hand.ToCall(2).Should().Be(160);
    }

    [Fact]
    public void AShowdown_PaysTheBestHand_AndSplitsTies_OddChipLeftOfTheButton()
    {
        // Seats 1 and 2 both make the same nine-high straight; seat 0 only pairs the board.
        var hand = Rigged([1000, 1000, 1000], button: 0,
            holes: ["2C 3D", "9S KH", "9D KC"],
            board: "5H 6S 7D 8C 2H");
        PlayToShowdown(hand, callsOnly: true);

        hand.Winnings.Should().Equal(new Dictionary<int, int> { [1] = 30, [2] = 30 });

        var odd = Rigged([1000, 1000, 1000], button: 0, holes: ["2C 3D", "9S KH", "9D KC"], board: "5H 6S 7D 8C 2H");
        odd.Apply(0, new RaiseTo(45));
        odd.Apply(1, CallBet.Instance);
        odd.Apply(2, CallBet.Instance);
        PlayToShowdown(odd, callsOnly: true);
        odd.Winnings.Should().Equal(new Dictionary<int, int> { [1] = 68, [2] = 67 }, "135 splits 68/67, the odd chip to the first winner after the button");
    }

    [Fact]
    public void AllIns_ForDifferentAmounts_MakeSidePots()
    {
        // Seat 0 is all in for 100 with the best hand; 1 and 2 play a side pot that 2 wins.
        var hand = Rigged([100, 500, 500], button: 2,
            holes: ["AS AH", "QS QH", "KS KH"],
            board: "2C 7D 9S 3H 4C");

        // Button 2 acts first, then the small blind (0) and the big blind (1).
        hand.Apply(2, CallBet.Instance);
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, new RaiseTo(300));
        hand.Apply(2, CallBet.Instance);
        PlayToShowdown(hand, callsOnly: true);

        hand.Winnings.Should().Equal(new Dictionary<int, int> { [0] = 300, [2] = 400 });
        Enumerable.Range(0, 3).Select(hand.Stack).Should().Equal(300, 200, 600);
        hand.TakeEvents().OfType<HandShown>().Select(s => s.Value.Describe()).Should().Contain("Pair of aces");
    }

    [Fact]
    public void WhenEveryoneIsAllIn_TheBoardIsDealtOut()
    {
        var hand = new HoldemHand([300, 300], button: 0, smallBlind: 10, bigBlind: 20, seed: 3);

        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, AllIn.Instance);

        hand.IsOver.Should().BeTrue();
        hand.Board.Should().HaveCount(5);
        (hand.Stack(0) + hand.Stack(1)).Should().Be(600);
    }

    [Fact]
    public void ABlindThatPutsAPlayerAllIn_StillPlays()
    {
        var hand = new HoldemHand([15, 1000], button: 1, smallBlind: 10, bigBlind: 20, seed: 4);

        hand.IsAllIn(0).Should().BeTrue();
        hand.Apply(1, CallBet.Instance);

        hand.IsOver.Should().BeTrue();
        (hand.Stack(0) + hand.Stack(1)).Should().Be(1015);
    }

    [Fact]
    public void TheTable_MovesTheButton_RaisesBlinds_AndDropsBustedPlayers()
    {
        var table = new HoldemTable(3, seed: 1, startingStack: 100, smallBlind: 5, handsPerLevel: 2);

        var first = table.DealHand();
        first.Button.Should().Be(0);
        FoldAround(first);
        table.DealHand().Button.Should().Be(1);
        table.BigBlind.Should().Be(10);
        FoldAround(table.Hand!);
        table.DealHand();
        table.BigBlind.Should().Be(20, "the third hand starts level 2");
        table.Stacks.Sum().Should().Be(300);
    }

    [Fact]
    public void Equity_RanksHandsSensibly()
    {
        var random = new Random(1);
        var aces = PokerBot.Equity(HandEvaluatorTests.Cards("AS AH"), [], 1, 2000, random);
        var junk = PokerBot.Equity(HandEvaluatorTests.Cards("7C 2D"), [], 1, 2000, random);
        var made = PokerBot.Equity(HandEvaluatorTests.Cards("AS KS"), HandEvaluatorTests.Cards("QS JS 10S"), 3, 200, random);

        aces.Should().BeInRange(0.8, 0.9);
        junk.Should().BeInRange(0.28, 0.4);
        made.Should().Be(1, "a royal flush cannot lose");
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    public void BotTournaments_KeepEveryChip_AndEndWithOneWinner(int players, int seed)
    {
        var table = new HoldemTable(players, seed, startingStack: 500, smallBlind: 10, handsPerLevel: 5);
        var bots = Enumerable.Range(0, players).Select(i => new PokerBot(0.3 + 0.1 * i, 0.7 - 0.1 * i, samples: 40)).ToArray();
        var seats = Enumerable.Range(0, players).Select(s => new Player(s, $"Bot {s}", PlayerKind.Bot)).ToArray();

        for (var hands = 0; hands < 400 && table.Champion is null; hands++)
        {
            var hand = table.DealHand();
            var match = new Match<HoldemHand, PokerAction>(hand, seats, s => bots[s], seed + hands) { BotDelay = TimeSpan.Zero };
            for (var turn = 0; turn < 500 && !hand.IsOver; turn++)
            {
                match.Update(TimeSpan.Zero);
            }

            hand.IsOver.Should().BeTrue();
            Enumerable.Range(0, players).Sum(hand.Stack).Should().Be(players * 500);
            table.Settle();
        }

        table.Champion.Should().NotBeNull();
        table.Stacks.Sum().Should().Be(players * 500);
    }

    /// <summary>Folds round to the big blind.</summary>
    private static void FoldAround(HoldemHand hand)
    {
        while (!hand.IsOver)
        {
            hand.Apply(hand.CurrentSeat, Poker.Fold.Instance);
        }
    }

    /// <summary>Everyone checks or calls until the showdown.</summary>
    private static void PlayToShowdown(HoldemHand hand, bool callsOnly)
    {
        for (var turn = 0; turn < 50 && !hand.IsOver; turn++)
        {
            var seat = hand.CurrentSeat;
            hand.Apply(seat, hand.ToCall(seat) > 0 ? CallBet.Instance : Check.Instance);
        }

        callsOnly.Should().BeTrue();
        hand.IsOver.Should().BeTrue();
    }

    /// <summary>A hand dealing these hole cards (by seat) and this board.</summary>
    private static HoldemHand Rigged(int[] stacks, int button, string[] holes, string board)
    {
        var order = Enumerable.Range(1, stacks.Length).Select(i => (button + i) % stacks.Length).ToArray();
        var deck = new List<Card>();
        for (var round = 0; round < 2; round++)
        {
            deck.AddRange(order.Select(seat => HandEvaluatorTests.Cards(holes[seat])[round]));
        }

        deck.AddRange(HandEvaluatorTests.Cards(board));
        return new HoldemHand(stacks, button, 10, 20, deck);
    }
}
