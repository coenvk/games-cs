using Poker.Arena;

namespace Poker.Tests;

public class HandReplayTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private static Func<IArenaSeat> House(Func<PokerBots.IPokerBot> make) => () => new HouseSeat(make());

    public static TheoryData<BatchFormat> Formats => [BatchFormat.DuplicateHeadsUp, BatchFormat.DuplicateRing, BatchFormat.SitAndGoSeries];

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task EverySavedHand_IsDealtAgainFromItsSeed_AndPlaysOutAsRecorded(BatchFormat format)
    {
        List<Func<IArenaSeat>> bots = format == BatchFormat.DuplicateHeadsUp
            ? [House(() => new EquityBot(EquityStyle.Balanced, 5, samples: 20)), House(() => new CallingBot())]
            : [House(() => new RandomBot(4)), House(() => new CallingBot()), House(() => new RaisingBot()), House(() => new RandomBot(8))];
        var batch = new ArenaBatch(bots, new BatchSettings(format, format == BatchFormat.SitAndGoSeries ? 2 : 30, Seed: DealSeed.FromNumber(17), Limit));
        var result = await batch.RunAsync(new Progress<double>(), CancellationToken.None);

        // Replays are made from saved matches: the hands read back from the file.
        var hands = MatchFile.Read(MatchFile.Write(result)).Hands;
        hands.Should().NotBeEmpty();
        foreach (var record in hands)
        {
            var dealt = HandReplay.Rebuild(record, 0);
            var seats = Enumerable.Range(0, record.Names.Count).ToList();
            seats.Select(s => dealt.Hole(s)).Should().BeEquivalentTo(record.Holes, o => o.WithStrictOrdering(), "hand {0} is dealt from its seed", record.Number);

            var played = HandReplay.Rebuild(record, record.Actions.Count);
            played.IsOver.Should().BeTrue();
            played.Board.Should().Equal(record.Board);
            played.Winnings.Should().BeEquivalentTo(record.Won);
        }
    }

    [Fact]
    public async Task ADownloadedMatch_WithoutSeeds_IsDealtFromItsRecordedCards_AndPlaysOutAsRecorded()
    {
        List<Func<IArenaSeat>> bots = [House(() => new RandomBot(4)), House(() => new CallingBot())];
        var batch = new ArenaBatch(bots, new BatchSettings(BatchFormat.DuplicateHeadsUp, 40, Seed: DealSeed.FromNumber(23), Limit));
        var full = await batch.RunAsync(new Progress<double>(), CancellationToken.None);
        var mine = full.Bots[0];

        // As the server sends it to the first bot's owner: no seeds, the opponent's folded cards removed.
        var downloaded = MatchFile.Read(MatchFile.Write(OwnerView.For(full, mine)));
        var hidden = 0;
        for (var i = 0; i < downloaded.Hands.Count; i++)
        {
            var record = downloaded.Hands[i];
            var original = full.Hands[i];
            record.Seed.Should().Be(default(DealSeed));
            var played = HandReplay.Rebuild(record, record.Actions.Count);
            played.IsOver.Should().BeTrue();
            played.Board.Should().Equal(original.Board, "hand {0} deals its recorded board", record.Number);
            played.Winnings.Should().BeEquivalentTo(original.Won);
            played.TakeEvents().OfType<Acted>().Select(a => (a.Seat, a.Action, a.Amount, a.Total))
                .Should().Equal(original.Actions.Select(a => (a.Seat, a.Action, a.Amount, a.Total)));

            var seats = Enumerable.Range(0, record.Names.Count).ToList();
            foreach (var seat in seats)
            {
                if (HandReplay.IsHidden(record, seat))
                {
                    hidden++;
                    record.Names[seat].Should().NotBe(mine);
                    played.Hole(seat).Should().HaveCount(2, "a hidden seat is dealt stand-in cards, shown face down")
                        .And.NotIntersectWith(record.Board).And.NotIntersectWith(seats.SelectMany(s => record.Holes[s]));
                }
                else
                {
                    played.Hole(seat).Should().Equal(original.Holes[seat]);
                }
            }
        }

        hidden.Should().BeGreaterThan(0, "the random bot folds now and then");
    }

    [Fact]
    public void ASeatIsHidden_OnlyWhenItWasDealtInAndItsCardsAreMissing()
    {
        var hand = TestHands.Showdown();
        var view = hand with { Holes = [hand.Holes[0], hand.Holes[1], []] };

        HandReplay.IsHidden(view, 2).Should().BeTrue();
        HandReplay.IsHidden(view, 0).Should().BeFalse();
        HandReplay.IsHidden(hand, 2).Should().BeFalse();
        HandReplay.IsHidden(view with { StartStacks = [1000, 1000, 0] }, 2).Should().BeFalse("a seat that was out was dealt nothing");
    }

    [Fact]
    public void Rebuild_StopsAfterTheGivenNumberOfActions()
    {
        var record = Record(new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(5)), seed: DealSeed.FromNumber(5),
            (0, new RaiseTo(60)), (1, CallBet.Instance), (2, Fold.Instance), (1, Check.Instance), (0, new RaiseTo(100)), (1, Fold.Instance));

        var preflop = HandReplay.Rebuild(record, 2);
        preflop.Street.Should().Be(Street.Preflop);
        preflop.CurrentSeat.Should().Be(2);
        preflop.Bet(1).Should().Be(60);

        var flop = HandReplay.Rebuild(record, 4);
        flop.Street.Should().Be(Street.Flop);
        flop.CurrentSeat.Should().Be(0);
        flop.Board.Should().HaveCount(3);

        HandReplay.Rebuild(record, 6).Winnings.Should().Equal(new Dictionary<int, int> { [0] = 240 });
    }

    [Fact]
    public void ARecordThatDoesNotPlayOut_IsRefused()
    {
        var record = Record(new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(5)), seed: DealSeed.FromNumber(5), (0, Fold.Instance));
        var wrongSeat = record with { Actions = [new ActionRecord(Street.Preflop, 1, Fold.Instance, 0, 10)] };

        var replay = () => HandReplay.Rebuild(wrongSeat, 1);

        replay.Should().Throw<InvalidDataException>().WithMessage("*seat 1*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ACountOutsideTheActions_IsRefused(int actions)
    {
        var record = Record(new HoldemHand([1000, 1000, 1000], button: 0, smallBlind: 10, bigBlind: 20, seed: DealSeed.FromNumber(5)), seed: DealSeed.FromNumber(5), (0, Fold.Instance));

        var replay = () => HandReplay.Rebuild(record, actions);

        replay.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A record of <paramref name="hand"/> after the moves are applied to it, as the arena would write it.</summary>
    private static HandRecord Record(HoldemHand hand, DealSeed seed, params (int Seat, PokerAction Move)[] moves)
    {
        var stacks = Enumerable.Range(0, hand.Seats).Select(s => hand.Stack(s) + hand.Committed(s)).ToList();
        var actions = new List<ActionRecord>();
        foreach (var (seat, move) in moves)
        {
            var street = hand.Street;
            hand.Apply(seat, move);
            var acted = hand.TakeEvents().OfType<Acted>().Single();
            actions.Add(new ActionRecord(street, seat, acted.Action, acted.Amount, acted.Total));
        }

        return new HandRecord(
            1,
            seed,
            hand.Button,
            hand.SmallBlind,
            hand.BigBlind,
            hand.SmallBlindSeat,
            hand.BigBlindSeat,
            hand.SmallBlind,
            hand.BigBlind,
            [.. Enumerable.Range(0, hand.Seats).Select(s => $"Bot {s}")],
            stacks,
            [.. Enumerable.Range(0, hand.Seats).Select(s => (IReadOnlyList<Jentl.Cards.Card>)[.. hand.Hole(s)])],
            actions,
            [.. hand.Board],
            hand.Pots,
            new Dictionary<int, int>(hand.Winnings),
            new HashSet<int>(),
            []);
    }
}
