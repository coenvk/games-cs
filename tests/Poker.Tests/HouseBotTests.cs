using PokerBots;

namespace Poker.Tests;

public class HouseBotTests
{
    private static readonly string[] Names = ["A", "B", "C", "D", "E", "F"];

    private static Decision DecisionFor(HoldemHand hand, int id = 1) =>
        BotView.Decision(hand, hand.CurrentSeat, id, handNumber: 1, level: 1, Names, [], TimeSpan.FromSeconds(1));

    [Fact]
    public void ACardCode_RoundTripsThroughBotView()
    {
        foreach (var card in Jentl.Cards.Deck.Standard52())
        {
            BotView.Card(BotView.Code(card)).Should().Be(card);
        }
    }

    [Fact]
    public void HouseBots_ListedByName()
    {
        HouseBots.All.Select(b => b.Name).Should().Equal("Always call", "Always raise", "Random", "Tight", "Balanced", "Loose");
        HouseBots.All.Select(b => b.Make(1).Name).Should().Equal("Always call", "Always raise", "Random", "Tight", "Balanced", "Loose");
    }

    [Fact]
    public void HouseBots_OnlyEverAnswerLegally()
    {
        foreach (var (name, make) in HouseBots.All)
        {
            // The equity bots deal fewer samples here, to keep the test quick; the rest is as in play.
            var bots = Enumerable.Range(0, 6)
                .Select(seat => name is "Tight" or "Balanced" or "Loose"
                    ? new EquityBot(Enum.Parse<EquityStyle>(name), seat, samples: 20)
                    : make(seat))
                .ToArray();
            var series = new HandSeries(6, seed: 3);
            var id = 0;
            for (var index = 0; index < 300; index++)
            {
                var hand = series.Deal(index);
                while (!hand.IsOver)
                {
                    var seat = hand.CurrentSeat;
                    var decision = BotView.Decision(hand, seat, ++id, index + 1, 1, Names, [], TimeSpan.FromSeconds(1));
                    var answer = bots[seat].Decide(decision);
                    var move = BotView.ToAction(answer, hand, seat, id);
                    move.Should().NotBeNull($"{name} answered {answer} in hand {index}");
                    hand.Apply(seat, move!);
                }
            }
        }
    }

    [Fact]
    public void TheCallingBot_Calls()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        new CallingBot().Decide(DecisionFor(hand)).Should().Be(BotAction.Call);
        hand.Apply(hand.CurrentSeat, CallBet.Instance);
        hand.Apply(hand.CurrentSeat, CallBet.Instance);
        new CallingBot().Decide(DecisionFor(hand)).Should().Be(BotAction.Check);
    }

    [Fact]
    public void TheRaisingBot_RaisesTheMinimum()
    {
        var hand = new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        var decision = DecisionFor(hand);
        new RaisingBot().Decide(decision).Should().Be(BotAction.Raise(decision.Legal.MinRaiseTo!.Value));
    }

    [Fact]
    public void TheRaisingBot_GoesAllIn_WhenTheMinimumIsAllItHas()
    {
        var hand = new HoldemHand([1000, 30, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: 1);
        hand.Apply(hand.CurrentSeat, CallBet.Instance);
        var decision = DecisionFor(hand);
        decision.Legal.MaxRaiseTo.Should().BeLessThan(decision.Legal.MinRaiseTo!.Value + 1);
        new RaisingBot().Decide(decision).Should().Be(BotAction.AllIn);
    }
}
