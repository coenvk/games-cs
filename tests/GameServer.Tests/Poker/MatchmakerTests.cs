using GameServer.Poker;

namespace GameServer.Tests.Poker;

/// <summary>Pairing of queued poker bots: owners, rating windows and the cooldown between the same two bots.</summary>
public sealed class MatchmakerTests
{
    private readonly Clock _clock = new();
    private readonly Dictionary<string, (string Owner, double Rating)> _bots = [];

    private Matchmaker Create(MatchmakerSettings? settings = null) =>
        new(bot => _bots[bot], _clock, settings ?? new MatchmakerSettings());

    private void Add(string bot, string owner, double rating) => _bots[bot] = (owner, rating);

    [Fact]
    public void BotsOfOneOwner_AreNeverPaired()
    {
        Add("A", "x", 1500);
        Add("B", "x", 1500);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");
        _clock.Advance(TimeSpan.FromDays(1));

        matchmaker.Pair().Should().BeEmpty();
    }

    [Fact]
    public void ABot_IsNeverPairedWithItself()
    {
        Add("A", "x", 1500);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("A");
        _clock.Advance(TimeSpan.FromDays(1));

        matchmaker.Pair().Should().BeEmpty();
    }

    [Fact]
    public void FarRatings_PairOnceTheWindowHasGrown()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1800);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");

        matchmaker.Pair().Should().BeEmpty();

        _clock.Advance(TimeSpan.FromMinutes(1));
        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "B"));
    }

    [Fact]
    public void TheLongestWaiting_IsPairedFirst_WithTheClosest()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1550);
        Add("C", "z", 1700);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        _clock.Advance(TimeSpan.FromSeconds(10));
        matchmaker.Enqueue("C");
        matchmaker.Enqueue("B");

        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "B"));
    }

    [Fact]
    public void APairedBot_LeavesTheQueue()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");

        matchmaker.Pair().Should().HaveCount(1);
        matchmaker.Pair().Should().BeEmpty();
    }

    [Fact]
    public void ARemovedBot_IsNotPaired()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        var matchmaker = Create();
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");
        matchmaker.Remove("B");

        matchmaker.Pair().Should().BeEmpty();
    }

    [Fact]
    public void ARecentOpponent_WaitsForTheCooldown()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        Add("C", "z", 1520);
        var matchmaker = Create();
        matchmaker.Played("A", "B");
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");
        matchmaker.Enqueue("C");

        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "C"));
    }

    [Fact]
    public void ARecentOpponent_IsPairedAfterTenMinutes_WhenNoOneElseFits()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        var matchmaker = Create();
        matchmaker.Played("A", "B");
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");

        _clock.Advance(TimeSpan.FromMinutes(9));
        matchmaker.Pair().Should().BeEmpty();

        _clock.Advance(TimeSpan.FromMinutes(1));
        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "B"));
    }

    [Fact]
    public void TheCooldown_CountsMatches()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        Add("C", "z", 1500);
        Add("D", "w", 1500);
        Add("E", "v", 1500);
        var matchmaker = Create(new MatchmakerSettings(Cooldown: 2));
        matchmaker.Played("A", "B");
        matchmaker.Played("A", "C");
        matchmaker.Played("A", "D");
        matchmaker.Played("B", "C");
        matchmaker.Played("B", "D");
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");

        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "B"));
    }
    [Fact]
    public void ABotTheLookupCannotFind_IsDropped_AndTheOthersStillPair()
    {
        Add("A", "x", 1500);
        Add("B", "y", 1500);
        var matchmaker = Create();
        matchmaker.Enqueue("Ghost");
        matchmaker.Enqueue("A");
        matchmaker.Enqueue("B");

        matchmaker.Pair().Should().ContainSingle().Which.Should().Be(("A", "B"));

        Add("Ghost", "z", 1500);
        Add("C", "w", 1500);
        matchmaker.Enqueue("C");
        matchmaker.Pair().Should().BeEmpty();
    }


    /// <summary>A clock that stands still until told to move.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
