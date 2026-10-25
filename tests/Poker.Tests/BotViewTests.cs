using PokerBots;

namespace Poker.Tests;

public class BotViewTests
{
    private static readonly string[] Names = ["A", "B", "C", "D", "E", "F"];

    private static readonly Jentl.Cards.Card[] OddChipDeck =
        [.. new[] { "2C", "2D", "3C", "3D", "3H", "4C", "AS", "KS", "QS", "JS", "10S" }.Select(Jentl.Cards.Card.Parse)];

    private static Decision DecisionFor(HoldemHand hand, int seat, int id = 1) =>
        BotView.Decision(hand, seat, id, handNumber: 1, level: 1, Names, [], TimeSpan.FromSeconds(1));

    // Three players, blinds 10/20: seat 0 raises to 60, so seat 1 (small blind) faces a raise.
    private static HoldemHand FacingARaise()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, new RaiseTo(60));
        return hand;
    }

    [Fact]
    public void Code_UsesRankAndSuitLetters()
    {
        BotView.Code(new Jentl.Cards.Card(Jentl.Cards.Card.Ace, Jentl.Cards.Suit.Spades)).Should().Be("As");
        BotView.Code(new Jentl.Cards.Card(10, Jentl.Cards.Suit.Diamonds)).Should().Be("Td");
        BotView.Code(new Jentl.Cards.Card(2, Jentl.Cards.Suit.Clubs)).Should().Be("2c");
        BotView.Code(new Jentl.Cards.Card(Jentl.Cards.Card.King, Jentl.Cards.Suit.Hearts)).Should().Be("Kh");
    }

    [Fact]
    public void ADecision_ShowsOnlyTheSeatsOwnCards_OnEveryStreet()
    {
        var hand = new HoldemHand([1000, 1000, 1000, 1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 7);
        var streets = new HashSet<string>();
        while (!hand.IsOver)
        {
            var current = hand.CurrentSeat;
            foreach (var seat in new[] { current, (current + 1) % 6 })
            {
                var decision = DecisionFor(hand, seat);
                var json = Protocol.Write(decision);
                streets.Add(decision.Street);
                decision.Hole.Should().Equal(hand.Hole(seat).Select(BotView.Code));
                foreach (var other in Enumerable.Range(0, 6).Where(s => s != seat))
                {
                    foreach (var card in hand.Hole(other).Select(BotView.Code))
                    {
                        json.Should().NotContain($"\"{card}\"", $"seat {other}'s hole card must not reach seat {seat} on the {decision.Street}");
                    }
                }
            }

            hand.Apply(current, hand.ToCall(current) > 0 ? CallBet.Instance : Check.Instance);
        }

        streets.Should().BeEquivalentTo("preflop", "flop", "turn", "river");
    }

    [Fact]
    public void ADecision_GivesTheLegalAmounts()
    {
        var hand = FacingARaise();

        var decision = DecisionFor(hand, 1, id: 42);

        decision.Id.Should().Be(42);
        decision.ToCall.Should().Be(50);
        decision.Legal.Check.Should().BeFalse();
        decision.Legal.Call.Should().Be(50);
        decision.Legal.MinRaiseTo.Should().Be(100);
        decision.Legal.MaxRaiseTo.Should().Be(hand.Bet(1) + hand.Stack(1));
        decision.Legal.AllIn.Should().BeTrue();
        decision.Street.Should().Be("preflop");
        decision.Seats[1].Should().Be(new TableSeat(1, "B", 990, 10, "active"));
        decision.Pots.Should().ContainSingle().Which.Amount.Should().Be(90);
    }

    [Fact]
    public void ADecision_ShowsSeatStates()
    {
        var hand = new HoldemHand([1000, 0, 100, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 2);
        hand.Apply(hand.CurrentSeat, Fold.Instance);
        hand.Apply(hand.CurrentSeat, AllIn.Instance);

        var states = DecisionFor(hand, hand.CurrentSeat).Seats.Select(s => s.State).ToArray();

        states.Should().Contain(["out", "folded", "all-in"]);
    }

    [Fact]
    public void ADecision_WhenOnlyACallIsPossible_OffersNoRaise()
    {
        var hand = OnlyACallIsPossible();

        var legal = DecisionFor(hand, 2).Legal;

        legal.Call.Should().Be(980);
        legal.MinRaiseTo.Should().BeNull();
        legal.MaxRaiseTo.Should().BeNull();
    }

    private static HoldemHand OnlyACallIsPossible()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, CallBet.Instance);
        return hand;
    }

    private static HoldemHand ShortStackFacingARaise()
    {
        // Seat 1 has 110: facing a raise to 100 the minimum raise is to 140, so all in (110) is a short raise.
        var hand = new HoldemHand([1000, 110, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, new RaiseTo(100));
        return hand;
    }

    [Theory]
    [InlineData("check-facing-a-bet")]
    [InlineData("call-with-nothing-to-call")]
    [InlineData("raise-below-the-minimum")]
    [InlineData("raise-above-the-maximum")]
    [InlineData("raise-when-only-a-call-is-possible")]
    [InlineData("raise-without-an-amount")]
    [InlineData("an-unknown-action")]
    [InlineData("an-answer-to-another-decision")]
    public void IllegalAnswers_AreNull(string scenario)
    {
        var (hand, seat, answer) = scenario switch
        {
            "check-facing-a-bet" => (FacingARaise(), 1, BotAction.Check),
            "call-with-nothing-to-call" => (BigBlindWithTheOption(), 2, BotAction.Call),
            "raise-below-the-minimum" => (FacingARaise(), 1, BotAction.Raise(99)),
            "raise-above-the-maximum" => (FacingARaise(), 1, BotAction.Raise(1001)),
            "raise-when-only-a-call-is-possible" => (OnlyACallIsPossible(), 2, BotAction.Raise(1000)),
            "raise-without-an-amount" => (FacingARaise(), 1, new BotAction("raise")),
            "an-unknown-action" => (FacingARaise(), 1, new BotAction("bluff")),
            _ => (FacingARaise(), 1, BotAction.Call with { Id = 6 }),
        };

        BotView.ToAction(answer, hand, seat, decisionId: 5).Should().BeNull();
    }

    private static HoldemHand BigBlindWithTheOption()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, CallBet.Instance);
        hand.Apply(1, CallBet.Instance);
        return hand;
    }

    [Fact]
    public void LegalAnswers_BecomeEngineActions()
    {
        var hand = FacingARaise();

        BotView.ToAction(BotAction.Fold, hand, 1, 5).Should().Be(Fold.Instance);
        BotView.ToAction(BotAction.Call, hand, 1, 5).Should().Be(CallBet.Instance);
        BotView.ToAction(BotAction.Raise(100), hand, 1, 5).Should().Be(new RaiseTo(100));
        BotView.ToAction(BotAction.AllIn, hand, 1, 5).Should().Be(AllIn.Instance);
        BotView.ToAction(BotAction.Raise(1000), hand, 1, 5).Should().Be(AllIn.Instance);
        BotView.ToAction(BotAction.Call with { Id = 5 }, hand, 1, 5).Should().Be(CallBet.Instance);
        BotView.ToAction(BotAction.Check, BigBlindWithTheOption(), 2, 5).Should().Be(Check.Instance);
    }

    [Fact]
    public void AShortAllIn_IsLegal()
    {
        var hand = ShortStackFacingARaise();

        var legal = DecisionFor(hand, 1).Legal;

        legal.MinRaiseTo.Should().Be(110, "the minimum is capped at what the seat can put in");
        BotView.ToAction(BotAction.Raise(110), hand, 1, 5).Should().Be(AllIn.Instance);
        BotView.ToAction(BotAction.Raise(105), hand, 1, 5).Should().BeNull("only the all-in may be below the minimum raise");
    }

    [Fact]
    public void Fallback_ChecksWhenFree_FoldsOtherwise()
    {
        BotView.Fallback(BigBlindWithTheOption(), 2).Should().Be(Check.Instance);
        BotView.Fallback(FacingARaise(), 1).Should().Be(Fold.Instance);
    }

    [Fact]
    public void HandStart_ShowsStacksBeforeTheBlinds()
    {
        var hand = new HoldemHand([1000, 0, 500], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);

        var start = BotView.HandStart(hand, 3, Names);

        start.Hand.Should().Be(3);
        start.Seats.Select(s => (s.Stack, s.State)).Should().Equal((1000, "active"), (0, "out"), (500, "active"));
    }

    [Fact]
    public void HandEnd_ShowsOnlyShownCards()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, Fold.Instance);
        hand.Apply(1, Fold.Instance);

        var end = BotView.HandEnd(hand, 1, []);
        var json = Protocol.Write(end);

        end.Shown.Should().BeEmpty();
        foreach (var card in Enumerable.Range(0, 3).SelectMany(s => hand.Hole(s)).Select(BotView.Code))
        {
            json.Should().NotContain($"\"{card}\"");
        }

        end.Won.Should().Equal(new Win(2, 30));
        end.Pots.Should().ContainSingle().Which.Winners.Should().Equal(new Win(2, 30));
        end.Stacks.Should().Equal(1000, 990, 1010);
    }

    [Fact]
    public void HandEnd_AtShowdown_ShowsEveryHandStillIn_AndPaysEachPot()
    {
        var hand = new HoldemHand([100, 300, 600], button: 0, smallBlind: 10, bigBlind: 20, seed: 3);
        hand.Apply(0, AllIn.Instance);
        hand.Apply(1, AllIn.Instance);
        hand.Apply(2, CallBet.Instance);

        var end = BotView.HandEnd(hand, 1, []);

        hand.IsOver.Should().BeTrue();
        end.Shown.Select(s => s.Seat).Should().Equal(0, 1, 2);
        end.Shown.Should().OnlyContain(s => s.Cards.Count == 2 && s.Rank.Length > 0);
        end.Board.Should().HaveCount(5);
        end.Pots.Select(p => p.Amount).Should().Equal(hand.Pots.Select(p => p.Amount));
        end.Pots.Should().OnlyContain(p => p.Winners.Sum(w => w.Amount) == p.Amount);
        end.Pots.SelectMany(p => p.Winners).GroupBy(w => w.Seat).ToDictionary(g => g.Key, g => g.Sum(w => w.Amount))
            .Should().BeEquivalentTo(hand.Winnings);
        end.Won.ToDictionary(w => w.Seat, w => w.Amount).Should().BeEquivalentTo(hand.Winnings);
    }

    [Fact]
    public void WhenEveryOpponentIsAllIn_ThereIsNothingToRaise()
    {
        var hand = new HoldemHand([1000, 100, 100], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(0, new RaiseTo(60));
        hand.Apply(1, AllIn.Instance);
        hand.Apply(2, AllIn.Instance);

        var legal = DecisionFor(hand, 0).Legal;

        legal.Call.Should().Be(40);
        legal.MinRaiseTo.Should().BeNull();
        legal.MaxRaiseTo.Should().BeNull();
        BotView.ToAction(BotAction.Raise(500), hand, 0, 5).Should().BeNull();
        BotView.ToAction(BotAction.Raise(1000), hand, 0, 5).Should().BeNull();
        BotView.ToAction(BotAction.Call, hand, 0, 5).Should().Be(CallBet.Instance);
    }

    [Fact]
    public void HandEnd_MidHand_Throws()
    {
        var hand = FacingARaise();

        var act = () => BotView.HandEnd(hand, 1, []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NamesShorterThanTheSeats_Throw()
    {
        var hand = FacingARaise();

        var decision = () => BotView.Decision(hand, 1, 1, 1, 1, ["A", "B"], [], TimeSpan.FromSeconds(1));
        var start = () => BotView.HandStart(hand, 1, ["A", "B"]);

        decision.Should().Throw<ArgumentException>();
        start.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HandEnd_ReportsTheEnginesPayout_IncludingTheOddChip()
    {
        // Seats 0 and 2 both play the royal flush on the board and split 25 chips; the odd chip goes left of the button.
        var deck = OddChipDeck;
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 5, bigBlind: 10, deck);
        hand.Apply(0, CallBet.Instance);
        hand.Apply(1, Fold.Instance);
        hand.Apply(2, Check.Instance);
        while (!hand.IsOver)
        {
            hand.Apply(hand.CurrentSeat, Check.Instance);
        }

        var end = BotView.HandEnd(hand, 1, []);

        end.Pots.Should().ContainSingle().Which.Winners.Should().Equal(new Win(0, 12), new Win(2, 13));
        end.Pots.Select(p => (p.Amount, p.Seats, p.Winners)).Should().BeEquivalentTo(
            hand.PotResults.Select(p => (p.Amount, p.Seats, (IReadOnlyList<Win>)[.. p.Winners.Select(w => new Win(w.Seat, w.Amount))])));
    }

    [Fact]
    public void Actions_AreShownByName()
    {
        var hand = FacingARaise();
        ActionRecord[] actions =
        [
            new(Street.Preflop, 0, new RaiseTo(60), 60, 60),
            new(Street.Preflop, 1, AllIn.Instance, 100, 110),
            new(Street.Flop, 2, Check.Instance, 0, 0),
        ];

        var decision = BotView.Decision(hand, 1, 1, 1, 1, Names, actions, TimeSpan.FromMilliseconds(1500));

        decision.Actions.Should().Equal(
            new PlayedAction("preflop", 0, "raise", 60, 60),
            new PlayedAction("preflop", 1, "all-in", 100, 110),
            new PlayedAction("flop", 2, "check", 0, 0));
        decision.TimeLimitMs.Should().Be(1500);
    }
}
