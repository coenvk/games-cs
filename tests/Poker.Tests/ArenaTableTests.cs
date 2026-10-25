using Jentl.Multiplayer;
using Poker.Arena;
using PokerBots;

namespace Poker.Tests;

public class ArenaTableTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

    private static BotAction CheckOrCall(Decision d) => d.Legal.Check ? BotAction.Check : BotAction.Call;

    private static ArenaSettings Fixed(int hands, TimeSpan? limit = null) =>
        new(TableFormat.FixedStacks, hands, Seed: 42, limit ?? Limit);

    [Fact]
    public async Task ALateSeat_IsFoldedOrChecked_AndCounted()
    {
        var late = new FakeSeat(CheckOrCall, _ => TimeSpan.FromMilliseconds(200));
        using var table = new ArenaTable([late, new HouseSeat(new CallingBot())], Fixed(3));

        await table.RunAsync(CancellationToken.None);

        table.IsOver.Should().BeTrue();
        table.Hands.Should().HaveCount(3);
        var moves = table.Hands.SelectMany(h => h.Actions).Where(a => a.Seat == 0).Select(a => a.Action).ToList();
        moves.Should().NotBeEmpty().And.OnlyContain(a => a is Fold || a is Check);
        moves.Should().HaveCount(late.Asked);
        table.Hands.SelectMany(h => h.Faults).Should().OnlyContain(f => f.Seat == 0 && f.Fault == Fault.Late).And.HaveCount(late.Asked);
        var result = table.Results()[0];
        result.Late.Should().Be(late.Asked);
        result.Illegal.Should().Be(0);
        result.Gone.Should().BeFalse();
        result.Hands.Should().Be(3);
        result.Place.Should().BeNull();
        table.Results().Sum(r => r.Net).Should().Be(0);
    }

    [Fact]
    public async Task EverySeat_IsToldAboutTheMatch_WithItsOwnSeed()
    {
        var first = new FakeSeat(CheckOrCall);
        var second = new FakeSeat(CheckOrCall);
        using var table = new ArenaTable([first, second], Fixed(2));

        await table.RunAsync(CancellationToken.None);

        var start = first.Messages.OfType<MatchStart>().Should().ContainSingle().Subject;
        start.Should().Be(new MatchStart(0, 2, "fixed-stacks", 2000, 10, 20, 50, ArenaTable.SeatSeed(42, 0)));
        second.Messages.OfType<MatchStart>().Single().Seed.Should().Be(ArenaTable.SeatSeed(42, 1)).And.NotBe(start.Seed);
        start.Seed.Should().NotBe(42);
        foreach (var seat in new[] { first, second })
        {
            seat.Messages.Select(m => m.GetType().Name).Should().Equal(
                "MatchStart", "HandStart", "HandEnd", "HandStart", "HandEnd", "MatchEnd");
        }

        var end = first.Messages.OfType<MatchEnd>().Single();
        end.Standings.Select(s => s.Place).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task ALateAnswer_DoesNotLeakIntoTheNextDecision()
    {
        // The first answer, all in, comes 100 ms after a 50 ms limit; every later answer comes at once.
        var asked = 0;
        var seat = new FakeSeat(
            d => ++asked == 1 ? BotAction.Raise(d.Legal.MaxRaiseTo!.Value) : CheckOrCall(d),
            n => n == 1 ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero);
        using var table = new ArenaTable([seat, new HouseSeat(new CallingBot())], Fixed(4));

        await table.RunAsync(CancellationToken.None);
        await Task.Delay(150);

        var mine = table.Hands.SelectMany(h => h.Actions).Where(a => a.Seat == 0).ToList();
        mine.Should().HaveCount(seat.Asked);
        mine[0].Action.Should().Be(Fold.Instance);
        mine.Skip(1).Should().OnlyContain(a => a.Action is Check || a.Action is CallBet);
        table.Hands.SelectMany(h => h.Faults).Should().Equal((0, Fault.Late));
        table.Results()[0].Late.Should().Be(1);
    }

    [Fact]
    public async Task AnIllegalRaise_IsCountedAndReplaced()
    {
        var seat = new FakeSeat(_ => BotAction.Raise(1));
        using var table = new ArenaTable([seat, new HouseSeat(new CallingBot())], Fixed(3));

        await table.RunAsync(CancellationToken.None);

        table.Hands.SelectMany(h => h.Actions).Where(a => a.Seat == 0)
            .Should().HaveCount(seat.Asked).And.OnlyContain(a => a.Action is Fold || a.Action is Check);
        var result = table.Results()[0];
        result.Illegal.Should().Be(seat.Asked);
        result.Late.Should().Be(0);
        table.Hands.SelectMany(h => h.Faults).Should().OnlyContain(f => f.Seat == 0 && f.Fault == Fault.Illegal);
    }

    [Fact]
    public async Task AGoneSeat_IsNeverAskedAgain()
    {
        var seat = new FakeSeat(CheckOrCall, fail: true);
        using var table = new ArenaTable([seat, new HouseSeat(new CallingBot()), new HouseSeat(new CallingBot())], Fixed(6));

        await table.RunAsync(CancellationToken.None);

        seat.Asked.Should().Be(1);
        var faults = table.Hands.SelectMany(h => h.Faults).ToList();
        faults.Should().HaveCountGreaterThan(1).And.OnlyContain(f => f.Seat == 0 && f.Fault == Fault.Gone);
        var result = table.Results()[0];
        result.Gone.Should().BeTrue();
        result.Problem.Should().Be("crashed");
        result.Late.Should().Be(0);
        result.Illegal.Should().Be(0);
        result.Hands.Should().Be(6);
    }

    [Fact]
    public async Task TheSameSeedAndHouseBots_PlayTheSameMatch()
    {
        var first = await Play();
        var second = await Play();

        first.Hands.Should().HaveCount(50);
        first.Hands.Should().BeEquivalentTo(second.Hands, o => o.WithStrictOrdering());
        first.Results.Should().Equal(second.Results);
        first.Results.Sum(r => r.Net).Should().Be(0);

        static async Task<(IReadOnlyList<HandRecord> Hands, IReadOnlyList<SeatResult> Results)> Play()
        {
            IArenaSeat[] seats =
            [
                new HouseSeat(new EquityBot(EquityStyle.Balanced, 1, samples: 40)),
                new HouseSeat(new EquityBot(EquityStyle.Loose, 2, samples: 40)),
                new HouseSeat(new EquityBot(EquityStyle.Tight, 3, samples: 40)),
            ];
            using var table = new ArenaTable(seats, Fixed(50, TimeSpan.FromSeconds(5)));
            await table.RunAsync(CancellationToken.None);
            return (table.Hands, table.Results());
        }
    }

    [Fact]
    public async Task ASitAndGo_EndsWithPlaces()
    {
        IArenaSeat[] seats =
        [
            new HouseSeat(new CallingBot()),
            new HouseSeat(new CallingBot()),
            new HouseSeat(new RaisingBot()),
            new HouseSeat(new CallingBot()),
            new HouseSeat(new CallingBot()),
        ];
        using var table = new ArenaTable(seats, new ArenaSettings(TableFormat.SitAndGo, 1000, 7, TimeSpan.FromSeconds(5)));

        await table.RunAsync(CancellationToken.None);

        table.IsOver.Should().BeTrue();
        var results = table.Results();
        results.Select(r => r.Place).Should().BeEquivalentTo(new int?[] { 1, 2, 3, 4, 5 });
        results.Sum(r => r.Net).Should().Be(0);
        results.Single(r => r.Place == 1).Net.Should().Be(4000);
        var lastHand = table.Hands[^1];
        lastHand.Number.Should().Be(table.Hands.Count);
    }

    [Fact]
    public async Task ASitAndGo_CutShortByTheHandLimit_PlacesByStack()
    {
        IArenaSeat[] seats = [new HouseSeat(new CallingBot()), new HouseSeat(new RaisingBot()), new HouseSeat(new CallingBot())];
        using var table = new ArenaTable(seats, new ArenaSettings(TableFormat.SitAndGo, 2, 7, TimeSpan.FromSeconds(5)));

        await table.RunAsync(CancellationToken.None);

        table.Hands.Should().HaveCount(2);
        var results = table.Results();
        results.Select(r => r.Place).Should().BeEquivalentTo(new int?[] { 1, 2, 3 });
        results.OrderBy(r => r.Place).Select(r => r.Net).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task StepAsync_DealsThenDecidesOneAtATime()
    {
        using var table = new ArenaTable([new HouseSeat(new CallingBot()), new HouseSeat(new CallingBot())], Fixed(1));
        await table.StartAsync();

        var dealt = await table.StepAsync(CancellationToken.None);
        dealt.Should().OnlyContain(e => e is BlindPosted).And.HaveCount(2);
        table.Hand.Should().NotBeNull();

        var acted = await table.StepAsync(CancellationToken.None);
        acted.OfType<Acted>().Should().ContainSingle();

        while (!table.IsOver)
        {
            await table.StepAsync(CancellationToken.None);
        }

        table.Hands.Should().ContainSingle();
        (await table.StepAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task AHandRecord_KeepsEverythingForAReplay()
    {
        using var table = new ArenaTable([new HouseSeat(new CallingBot()), new HouseSeat(new CallingBot())], Fixed(1));

        await table.RunAsync(CancellationToken.None);

        var record = table.Hands.Should().ContainSingle().Subject;
        var deal = new HandSeries(2, 42).Deal(0);
        record.Number.Should().Be(1);
        record.Seed.Should().Be(new HandSeries(2, 42).SeedOf(0));
        record.Button.Should().Be(0);
        record.SmallBlind.Should().Be(10);
        record.BigBlind.Should().Be(20);
        record.StartStacks.Should().Equal(2000, 2000);
        record.Holes[0].Should().Equal(deal.Hole(0));
        record.Holes[1].Should().Equal(deal.Hole(1));
        record.Board.Should().HaveCount(5);
        record.Shown.Should().BeEquivalentTo([0, 1]);
        record.Won.Values.Sum().Should().Be(40);
        record.Pots.Sum(p => p.Amount).Should().Be(40);
        record.Actions[0].Should().Be(new ActionRecord(Street.Preflop, 0, CallBet.Instance, 10, 20));
        record.Faults.Should().BeEmpty();
    }
}

/// <summary>A seat for tests: answers from a function, after a delay that ignores cancellation (like a slow bot), or crashes.</summary>
internal sealed class FakeSeat(Func<Decision, BotAction?> answer, Func<int, TimeSpan>? delay = null, bool fail = false) : IArenaSeat
{
    private readonly List<Message> _messages = [];
    private volatile string? _problem;

    public string Name => "Fake";

    public string? Problem => _problem;

    public bool IsGone => _problem is not null;

    /// <summary>How many times the seat was asked.</summary>
    public int Asked { get; private set; }

    public IReadOnlyList<Message> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public Task StartAsync(MatchStart start, TimeSpan limit)
    {
        Notify(start);
        return Task.CompletedTask;
    }

    public void Notify(Message message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }
    }

    public async Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel)
    {
        if (_problem is not null)
        {
            throw new IOException(_problem);
        }

        Asked++;
        if (fail)
        {
            _problem = "crashed";
            throw new IOException(_problem);
        }

        var reply = answer(decision);
        var wait = delay?.Invoke(Asked) ?? TimeSpan.Zero;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, CancellationToken.None);
        }

        return reply;
    }

    public void Dispose()
    {
    }
}
