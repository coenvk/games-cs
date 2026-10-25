using Poker.Arena;

namespace Poker.Tests;

public class ArenaBatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private static Func<IArenaSeat> House(Func<PokerBots.IPokerBot> make) => () => new HouseSeat(make());

    [Fact]
    public async Task IdenticalDeterministicBots_ComeOutEven()
    {
        var progress = new Reports();
        var batch = new ArenaBatch(
            [House(() => new CallingBot()), House(() => new CallingBot())],
            new BatchSettings(BatchFormat.DuplicateHeadsUp, 200, Seed: 7, Limit));

        var result = await batch.RunAsync(progress, CancellationToken.None);

        result.Results.Should().HaveCount(2).And.OnlyContain(r => r.BigBlindsPer100 == 0 && r.Margin95 == 0 && r.Hands == 400);
        result.Hands.Should().HaveCount(400);
        result.Settings.Format.Should().Be(TableFormat.FixedStacks);
        result.Batch!.Hands.Should().Be(200);
        progress.Values.Should().HaveCount(400).And.BeInAscendingOrder();
        progress.Values[^1].Should().Be(1);
    }

    [Fact]
    public async Task DuplicateRing_SeatsEveryBotEverywhere()
    {
        var batch = new ArenaBatch(
            [House(() => new CallingBot()), House(() => new RandomBot(3)), House(() => new RaisingBot())],
            new BatchSettings(BatchFormat.DuplicateRing, 4, Seed: 11, Limit));

        var result = await batch.RunAsync(new Reports(), CancellationToken.None);

        result.Bots.Should().Equal("Always call", "Random", "Always raise");
        var firstHands = result.Hands.Where(h => h.Number == 1).ToList();
        firstHands.Should().HaveCount(3).And.OnlyContain(h => h.Seed == firstHands[0].Seed);
        for (var seat = 0; seat < 3; seat++)
        {
            firstHands.Select(h => h.Names[seat]).Should().BeEquivalentTo(result.Bots);
        }

        // The same deal in every run: each seat gets the same hole cards whoever sits there.
        firstHands.Select(h => h.Holes[0]).Should().AllBeEquivalentTo(firstHands[0].Holes[0]);
        result.Results.Should().OnlyContain(r => r.Hands == 12);
    }

    [Fact]
    public async Task MoreHands_NarrowTheMargin()
    {
        async Task<BotResult> Play(int hands)
        {
            var batch = new ArenaBatch(
                [House(() => new EquityBot(EquityStyle.Balanced, 5, samples: 20)), House(() => new RandomBot(9))],
                new BatchSettings(BatchFormat.DuplicateHeadsUp, hands, Seed: 3, Limit));
            return (await batch.RunAsync(new Reports(), CancellationToken.None)).Results[0];
        }

        var few = await Play(100);
        var many = await Play(800);

        few.Margin95.Should().BeGreaterThan(0);
        many.Margin95.Should().BeLessThan(few.Margin95);
        many.Hands.Should().Be(1600);
    }

    [Fact]
    public async Task ASeries_ReportsPlaces()
    {
        var batch = new ArenaBatch(
            [House(() => new CallingBot()), House(() => new RandomBot(1)), House(() => new RaisingBot())],
            new BatchSettings(BatchFormat.SitAndGoSeries, 5, Seed: 2, Limit));

        var result = await batch.RunAsync(new Reports(), CancellationToken.None);

        result.Settings.Format.Should().Be(TableFormat.SitAndGo);
        result.Results.Should().HaveCount(3).And.OnlyContain(r => r.Places.Count == 5 && r.BigBlindsPer100 == 0 && r.Hands > 0);
        for (var game = 0; game < 5; game++)
        {
            result.Results.Select(r => r.Places[game]).Should().BeEquivalentTo([1, 2, 3]);
        }

        // The seating turns from game to game, so no bot always has seat 0.
        result.Hands.Where(h => h.Number == 1).Select(h => h.Names[0]).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task Cancelling_StopsAndKeepsWhatWasPlayed()
    {
        using var cancel = new CancellationTokenSource();

        // Half way into the second run: the first 50 deals have been played in both runs.
        var progress = new Reports(p =>
        {
            if (p >= 250.0 / 400)
            {
                cancel.Cancel();
            }
        });
        var batch = new ArenaBatch(
            [House(() => new CallingBot()), House(() => new RandomBot(4))],
            new BatchSettings(BatchFormat.DuplicateHeadsUp, 200, Seed: 8, Limit));

        var result = await batch.RunAsync(progress, cancel.Token);

        result.Hands.Should().HaveCount(250);
        progress.Values[^1].Should().Be(250.0 / 400);
        result.Results.Should().OnlyContain(r => r.Hands == 100);
        result.Results[0].BigBlindsPer100.Should().Be(-result.Results[1].BigBlindsPer100);
    }

    [Fact]
    public async Task AParallelBatch_ComesToTheSameResult()
    {
        async Task<MatchResult> Play(bool parallel) => await new ArenaBatch(
            [House(() => new CallingBot()), House(() => new RandomBot(6)), House(() => new RaisingBot())],
            new BatchSettings(BatchFormat.DuplicateRing, 50, Seed: 4, Limit, parallel)).RunAsync(new Reports(), CancellationToken.None);

        var one = await Play(false);
        var all = await Play(true);

        all.Results.Should().BeEquivalentTo(one.Results);
        all.Hands.Select(h => h.Seed).Should().Equal(one.Hands.Select(h => h.Seed));
    }

    [Fact]
    public async Task AThrowingSeatFactory_DisposesTheSeatsAlreadyMade_AndSurfaces()
    {
        var made = new List<CountedSeat>();
        Func<IArenaSeat> Counted() => () =>
        {
            var seat = new CountedSeat(new CallingBot());
            made.Add(seat);
            return seat;
        };
        var calls = 0;
        Func<IArenaSeat> breaksOnSecondRun = () => ++calls == 2 ? throw new InvalidOperationException("no more bots") : Counted()();

        // Run 1 seats bots 1, 2, 0: bot 1's seat is made before bot 2's factory throws.
        var batch = new ArenaBatch([Counted(), Counted(), breaksOnSecondRun], new BatchSettings(BatchFormat.DuplicateRing, 3, Seed: 1, Limit));

        var act = () => batch.RunAsync(new Reports(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no more bots");
        made.Should().HaveCount(4).And.OnlyContain(s => s.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASeries_ReportsProgressAfterEveryGame(bool parallel)
    {
        var progress = new Reports();
        var batch = new ArenaBatch(
            [House(() => new CallingBot()), House(() => new RaisingBot())],
            new BatchSettings(BatchFormat.SitAndGoSeries, 4, Seed: 3, Limit, parallel));

        await batch.RunAsync(progress, CancellationToken.None);

        progress.Values.Order().Should().Equal(0.25, 0.5, 0.75, 1);
    }

    [Fact]
    public async Task TheNetOfEachHand_AddsUpToTheTablesNet()
    {
        using var table = new ArenaTable(
            [new HouseSeat(new RandomBot(1)), new HouseSeat(new RaisingBot()), new HouseSeat(new RandomBot(2))],
            new ArenaSettings(TableFormat.FixedStacks, 60, Seed: 5, Limit));
        await table.RunAsync(CancellationToken.None);

        for (var seat = 0; seat < 3; seat++)
        {
            table.Hands.Sum(h => ArenaBatch.HandNet(h, seat)).Should().Be(table.Results()[seat].Net);
        }

        table.Hands.Should().OnlyContain(h => Enumerable.Range(0, 3).Sum(s => ArenaBatch.HandNet(h, s)) == 0);
    }

    [Fact]
    public void TheWrongNumberOfBots_IsRefused()
    {
        var call = House(() => new CallingBot());
        var act = () => new ArenaBatch([call, call, call], new BatchSettings(BatchFormat.DuplicateHeadsUp, 10, 1, Limit));
        act.Should().Throw<ArgumentException>();
        act = () => new ArenaBatch([call], new BatchSettings(BatchFormat.DuplicateRing, 10, 1, Limit));
        act.Should().Throw<ArgumentException>();
        act = () => new ArenaBatch([.. Enumerable.Repeat(call, 10)], new BatchSettings(BatchFormat.SitAndGoSeries, 10, 1, Limit));
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A house seat that remembers being disposed.</summary>
    private sealed class CountedSeat(PokerBots.IPokerBot bot) : IArenaSeat
    {
        private readonly HouseSeat _seat = new(bot);

        public bool Disposed { get; private set; }

        public string Name => _seat.Name;

        public string? Problem => _seat.Problem;

        public bool IsGone => _seat.IsGone;

        public Task StartAsync(PokerBots.MatchStart start, TimeSpan limit) => _seat.StartAsync(start, limit);

        public void Notify(PokerBots.Message message) => _seat.Notify(message);

        public Task<PokerBots.BotAction?> AskAsync(PokerBots.Decision decision, CancellationToken cancel) => _seat.AskAsync(decision, cancel);

        public void Dispose() => Disposed = true;
    }

    /// <summary>Records progress as it is reported, on the reporting thread.</summary>
    private sealed class Reports(Action<double>? onReport = null) : IProgress<double>
    {
        private readonly List<double> _values = [];

        public IReadOnlyList<double> Values
        {
            get
            {
                lock (_values)
                {
                    return [.. _values];
                }
            }
        }

        public void Report(double value)
        {
            lock (_values)
            {
                _values.Add(value);
            }

            onReport?.Invoke(value);
        }
    }
}
