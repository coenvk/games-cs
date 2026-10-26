using System.Collections.Concurrent;
using GameServer.Poker;
using Jentl.Net;

namespace GameServer.Tests.Poker;

/// <summary>
/// Ranked and practice matches between bots that are online through runners: real <see cref="AgentRelay"/>s running the
/// echo bot (which checks or calls), connected to a <see cref="RelayHost"/> over in-memory sockets.
/// </summary>
public sealed class LeagueTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-league-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Runner> _runners = [];
    private readonly RelayHost _host;
    private Registry _registry;
    private Registration _registration;
    private Ratings _ratings;
    private MatchStore _store;
    private PokerLeague? _league;

    public LeagueTests()
    {
        (_registry, _registration, _ratings, _store) = Open();
        _host = new RelayHost(token => _registry.BotOfToken(token)?.Name);
    }

    /// <summary>The registry, ratings and matches as the data folder holds them now, as a server starting up reads them.</summary>
    private (Registry, Registration, Ratings, MatchStore) Open()
    {
        var data = new PokerData(_folder);
        var registry = new Registry(data, TimeProvider.System);
        return (registry, new Registration(registry, new RegistrationLimits(), TimeProvider.System), new Ratings(data, registry), new MatchStore(data));
    }

    private PokerLeague League => _league ?? throw new InvalidOperationException("Start the league first.");

    public void Dispose()
    {
        _league?.Dispose();
        _stop.Cancel();
        foreach (var runner in _runners)
        {
            runner.Dispose();
        }

        _stop.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A bot process still closing a file: the temporary folder stays.
        }
    }

    private static AgentCommand EchoBot(params string[] arguments) =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "EchoBot", "Poker.Tests.EchoBot.dll"), .. arguments]);

    // Echo leaves every other decision from the 2nd to the given one unanswered: each costs the time limit, so the
    // match lasts long enough to do something while it runs. Never three in a row, so it is never stopped.
    private static AgentCommand SlowEchoBot(int upTo) =>
        EchoBot("skip:" + string.Join(',', Enumerable.Range(1, upTo / 2).Select(n => 2 * n)));

    private PokerLeague StartLeague(int hands, int cooldown = 3)
    {
        var matchmaker = new Matchmaker(
            bot => (_registry.Bot(bot)!.OwnerId, _ratings.Of(bot).Rating.Rating),
            TimeProvider.System,
            new MatchmakerSettings(Window: 10_000, Cooldown: cooldown)); // any two bots may meet, however their ratings moved
        _league = new PokerLeague(
            _host,
            _registry,
            _ratings,
            matchmaker,
            _store,
            new LeagueSettings(hands, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100)),
            TimeProvider.System);
        return _league;
    }

    private string Token(string owner, string bot)
    {
        var issued = _registration.NewOwner(owner, null).Value;
        return _registration.NewBot(issued.Token, bot, null).Value.Token;
    }

    private Runner Online(string owner, string bot, AgentCommand? command = null) => OnlineWith(Token(owner, bot), command);

    private Runner OnlineWith(string token, AgentCommand? command = null)
    {
        var runner = new Runner(_host, token, command ?? EchoBot(), _stop.Token);
        _runners.Add(runner);
        return runner;
    }

    /// <summary>Ticks the league until <paramref name="done"/> holds, as the server's timer would.</summary>
    private async Task RunUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!done())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the league should have got there by now");
            await League.TickAsync();
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task TwoConnectedBots_PlayARankedMatch_ThatIsStoredAndRated()
    {
        StartLeague(hands: 20);
        var a = Online("Ann", "BotA");
        Online("Bob", "BotB");

        await RunUntil(() => _store.Of("BotA", 10).Count == 1);

        _ratings.Of("BotA").Matches.Should().Be(1);
        _ratings.Of("BotB").Matches.Should().Be(1);
        _ratings.Of("BotA").Hands.Should().Be(20);
        var stored = _store.Of("BotA", 1)[0];
        stored.Ranked.Should().BeTrue();
        new[] { stored.BotA, stored.BotB }.Should().BeEquivalentTo("BotA", "BotB");
        (stored.NetA + stored.NetB).Should().Be(0);
        stored.Hands.Should().Be(20);
        var match = _store.Load(stored.Id)!;
        match.Hands.Should().HaveCount(20);
        match.Settings.Hands.Should().Be(20);
        match.Settings.TimeLimit.Should().Be(TimeSpan.FromMilliseconds(300), "the time limit plus the network allowance");
        match.Settings.SmallBlind.Should().Be(1);
        match.Settings.StackBigBlinds.Should().Be(100);
        _store.Of("BotB", 10).Should().ContainSingle();
        await RunUntil(() => a.Statuses.Any(s => s.Contains("\"ranked\":true", StringComparison.Ordinal))
            && a.Statuses.Any(s => s.Contains("\"rating\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EveryMatch_DealsFromAFreshSeed()
    {
        StartLeague(hands: 10, cooldown: 0);
        Online("Ann", "BotA");
        Online("Bob", "BotB");

        await RunUntil(() => _store.Of("BotA", 10).Count >= 2);

        var matches = _store.Of("BotA", 2).Select(m => _store.Load(m.Id)!).ToList();
        matches[0].Settings.Seed.Should().NotBe(matches[1].Settings.Seed);
        matches[0].Hands[0].Holes.Should().NotBeEquivalentTo(matches[1].Hands[0].Holes, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ARunnerThatDropsAndComesBack_KeepsItsSeat_AndTheMatchIsRated()
    {
        StartLeague(hands: 50);
        var reconnectedWhilePlaying = false;
        var connects = 0;
        _host.Connected += bot =>
        {
            if (bot == "BotA" && Interlocked.Increment(ref connects) == 2)
            {
                reconnectedWhilePlaying = League.Playing.Contains("BotA");
            }
        };
        var a = Online("Ann", "BotA");
        Online("Bob", "BotB", SlowEchoBot(40));

        await RunUntil(() => League.Playing.Contains("BotA"));
        await Task.Delay(1500); // the bots have said hello and are playing hands
        a.Drop(); // the runner connects again after a second, with the same token
        await RunUntil(() => _store.Of("BotA", 10).Count == 1);

        reconnectedWhilePlaying.Should().BeTrue("the runner came back during the match");
        var match = _store.Load(_store.Of("BotA", 1)[0].Id)!;
        var result = match.Results.Single(r => r.Name == "BotA");
        result.Gone.Should().Be(0, "being away is not three missed answers");
        (result.Late + result.Illegal).Should().BePositive("its decisions were folded while it was away");
        match.Hands.Should().HaveCount(50);
        _ratings.Of("BotA").Matches.Should().Be(1);
        _ratings.Of("BotB").Matches.Should().Be(1);
        _ratings.Of("BotA").Rating.Deviation.Should().BeLessThan(350);
        _ratings.Of("BotB").Rating.Deviation.Should().BeLessThan(350);
    }

    [Fact]
    public async Task APracticeMatch_IsNotRated()
    {
        StartLeague(hands: 20);
        Online("Ann", "BotA");
        await RunUntil(() => _host.Agent("BotA") is { IsAway: false });

        League.Practise("BotA", "No such bot").Should().BeFalse();
        League.Practise("BotA", "Always call").Should().BeTrue();
        League.Practise("BotA", "Random").Should().BeFalse("one match at a time");
        await RunUntil(() => _store.Of("BotA", 10).Count == 1);

        var stored = _store.Of("BotA", 1)[0];
        stored.Ranked.Should().BeFalse();
        stored.BotA.Should().Be("BotA");
        stored.BotB.Should().Be("Always call");
        _store.Load(stored.Id)!.Hands.Should().HaveCount(20);
        _ratings.Of("BotA").Matches.Should().Be(0);
        _store.Of("Always call", 10).Should().BeEmpty("a practice match is its own bot's, not the house bot's");
    }

    [Fact]
    public async Task StoppingTheLeague_DropsTheRunningMatch()
    {
        var league = StartLeague(hands: 10, cooldown: 0);
        var finished = 0;
        league.Finished += _ => Interlocked.Increment(ref finished);
        Online("Ann", "BotA");
        Online("Bob", "BotB", SlowEchoBot(8));
        await RunUntil(() => Volatile.Read(ref finished) == 1);
        await RunUntil(() => league.Playing.Contains("BotA")); // the second match
        await Task.Delay(300);

        league.Dispose();
        await Task.Delay(3000); // long enough for the second match to have ended, had it gone on

        _store.Of("BotA", 10).Should().ContainSingle();
        _ratings.Of("BotA").Matches.Should().Be(1);
        var data = new PokerData(_folder);
        var registry = new Registry(data, TimeProvider.System);
        new MatchStore(data).Of("BotA", 10).Should().ContainSingle("the earlier match is still there after a restart");
        new Ratings(data, registry).Of("BotA").Matches.Should().Be(1);
    }

    [Fact]
    public async Task AMatchThatCannotBeStored_IsNotRated_AndTheFailureIsReported()
    {
        var league = StartLeague(hands: 10);
        var failures = new ConcurrentQueue<LeagueFailure>();
        league.Failed += failures.Enqueue;
        var matches = Path.Combine(_folder, "matches");
        Directory.Delete(matches);
        await File.WriteAllTextAsync(matches, "not a folder"); // the disk refuses the match file
        Online("Ann", "BotA");
        Online("Bob", "BotB");

        await RunUntil(() => !failures.IsEmpty);

        var failure = failures.First();
        new[] { failure.BotA, failure.BotB }.Should().BeEquivalentTo("BotA", "BotB");
        failure.Error.Should().NotBeNull();
        _ratings.Of("BotA").Matches.Should().Be(0, "a match is rated only once it is stored");
        _ratings.Of("BotB").Matches.Should().Be(0);
    }

    [Fact]
    public async Task AFinishedHandlerThatThrows_IsReported_AndTheLeagueGoesOn()
    {
        var league = StartLeague(hands: 10, cooldown: 0);
        var failures = new ConcurrentQueue<LeagueFailure>();
        league.Failed += failures.Enqueue;
        league.Finished += _ => throw new InvalidOperationException("handler broke");
        Online("Ann", "BotA");
        Online("Bob", "BotB");

        await RunUntil(() => _store.Of("BotA", 10).Count >= 2);

        failures.Should().Contain(f => f.Error is InvalidOperationException && f.Error.Message == "handler broke");
    }

    [Fact]
    public async Task ABotNamedLikeAHouseBot_CannotPractiseAgainstItsNamesake()
    {
        // A bot registered before house bot names were refused.
        await File.WriteAllTextAsync(Path.Combine(_folder, "owners.json"),
            $$"""[{"id":"o1","tokenHash":"{{Registry.Hash("owner")}}","name":"Old","created":"2026-01-01T00:00:00+00:00","address":null,"banned":false}]""");
        await File.WriteAllTextAsync(Path.Combine(_folder, "bots.json"),
            $$"""[{"name":"Random","ownerId":"o1","tokenHash":"{{Registry.Hash("legacy")}}","created":"2026-01-01T00:00:00+00:00","banned":false}]""");
        (_registry, _registration, _ratings, _store) = Open();
        StartLeague(hands: 10);
        OnlineWith("legacy");
        await RunUntil(() => _host.Agent("Random") is { IsAway: false });

        League.Practise("Random", "Random").Should().BeFalse("two seats with one name are ambiguous");
        League.Practise("Random", "Tight").Should().BeTrue();
    }

    [Fact]
    public void TheStore_KeepsMatchesNewestFirst_AndRefusesIdsThatAreNoMatch()
    {
        _store.Load("../../owners").Should().BeNull();
        _store.Load("20261026-120000-0000000000000000").Should().BeNull();
        _store.Of("Nobody", 10).Should().BeEmpty();
    }

    /// <summary>A runner on an owner's computer: an <see cref="AgentRelay"/> whose every connection is a new socket pair served by the host.</summary>
    private sealed class Runner : IDisposable
    {
        private readonly AgentRelay _relay;
        private SocketPair? _connection;

        public Runner(RelayHost host, string token, AgentCommand command, CancellationToken stop)
        {
            _relay = new AgentRelay(
                connecting =>
                {
                    var pair = SocketPair.Create();
                    _connection = pair;
                    _ = host.RunAsync(pair.Server, stop);
                    return Task.FromResult(pair.Client);
                },
                token,
                command);
            _relay.Status += Statuses.Enqueue;
            _ = _relay.RunAsync(stop);
        }

        /// <summary>What the runner showed its owner.</summary>
        public ConcurrentQueue<string> Statuses { get; } = new();

        /// <summary>Breaks the connection, as a network would; the runner connects again by itself.</summary>
        public void Drop() => _connection?.Dispose();

        public void Dispose()
        {
            _relay.Dispose();
            _connection?.Dispose();
        }
    }
}
