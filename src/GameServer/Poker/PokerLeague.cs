using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Poker;
using Poker.Arena;

namespace GameServer.Poker;

/// <summary>
/// How ranked and practice matches are played: <paramref name="Hands"/> hands, and for each decision
/// <paramref name="TimeLimit"/> (1 s when null) plus <paramref name="Allowance"/> for the network (0.5 s when null). At
/// most <paramref name="ConcurrentMatches"/> matches run at once.
/// </summary>
public sealed record LeagueSettings(int Hands = 1000, TimeSpan? TimeLimit = null, TimeSpan? Allowance = null, int ConcurrentMatches = 8)
{
    /// <summary>What the table gives a bot for each decision: the time limit plus the network allowance.</summary>
    public TimeSpan DecisionLimit => (TimeLimit ?? TimeSpan.FromSeconds(1)) + (Allowance ?? TimeSpan.FromMilliseconds(500));
}

/// <summary>
/// Runs the online poker league: a bot whose runner connects goes into the matchmaker's queue; <see cref="TickAsync"/>
/// pairs waiting bots and starts their ranked matches, each on its own task, heads-up on fresh deals from a new secret
/// seed, fixed stacks of 100 big blinds, blinds 1/2. A finished ranked match is rated and stored; a practice match
/// against a house bot (<see cref="Practise"/>) is stored for its owner and not rated. A bot plays one match at a time,
/// and goes back into the queue afterwards while it is still connected and not banned.
/// </summary>
/// <remarks>
/// A runner that disconnects leaves the queue, but a match it is playing goes on: its seat folds while it is away and it
/// takes the seat back when it reconnects (see <see cref="AgentSeat"/>); the match counts. <see cref="Dispose"/> (the
/// server stopping) drops the matches that are running: they are neither stored nor rated. The league listens to the
/// relay host's <see cref="RelayHost.Connected"/> and <see cref="RelayHost.Disconnected"/> itself. A match that fails
/// (it cannot be played, stored or rated) is logged and raised as <see cref="Failed"/>, and the league carries on.
/// </remarks>
public sealed partial class PokerLeague : IDisposable
{
    private const int StackBigBlinds = 100;

    private readonly RelayHost _relays;
    private readonly Registry _registry;
    private readonly Ratings _ratings;
    private readonly Matchmaker _matchmaker;
    private readonly MatchStore _store;
    private readonly LeagueSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PokerLeague> _logger;
    private readonly CancellationTokenSource _stopping = new();

    private readonly object _lock = new();
    private readonly HashSet<string> _online = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string Bot, string House)> _practice = [];
    private readonly HashSet<Task> _running = [];
    private bool _disposed;

    // Taken to rate and store a finished match, and to stop the league: no match is kept once Dispose has begun.
    private readonly object _finishing = new();

    /// <param name="logger">Where failed matches are logged; nowhere when null.</param>
    public PokerLeague(RelayHost relays, Registry registry, Ratings ratings, Matchmaker matchmaker, MatchStore store, LeagueSettings settings, TimeProvider clock, ILogger<PokerLeague>? logger = null)
    {
        _relays = relays ?? throw new ArgumentNullException(nameof(relays));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ratings = ratings ?? throw new ArgumentNullException(nameof(ratings));
        _matchmaker = matchmaker ?? throw new ArgumentNullException(nameof(matchmaker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<PokerLeague>.Instance;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Hands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ConcurrentMatches);
        _relays.Connected += Connected;
        _relays.Disconnected += Disconnected;
    }

    /// <summary>Raised when a match was stored, ranked or practice, on the match's own task.</summary>
    public event Action<StoredMatch>? Finished;

    /// <summary>
    /// Raised when something went wrong with a match: it could not be played, stored or rated (it is then not rated), or a
    /// <see cref="Finished"/> handler threw. Raised on the match's own task; the failure is logged too.
    /// </summary>
    public event Action<LeagueFailure>? Failed;

    /// <summary>The bots in a match now, or about to start a practice match.</summary>
    public IReadOnlyCollection<string> Playing
    {
        get
        {
            lock (_lock)
            {
                return [.. _busy];
            }
        }
    }

    /// <summary>A runner connected for <paramref name="bot"/>: it waits for a ranked match, unless it is in one or banned.</summary>
    public void Connected(string bot)
    {
        ArgumentNullException.ThrowIfNull(bot);
        lock (_lock)
        {
            _online.Add(bot);
        }

        Requeue(bot);
    }

    /// <summary>The runner of <paramref name="bot"/> is gone: it leaves the queue; a match it is playing goes on.</summary>
    public void Disconnected(string bot)
    {
        ArgumentNullException.ThrowIfNull(bot);
        lock (_lock)
        {
            _online.Remove(bot);
        }

        _matchmaker.Remove(bot);
    }

    /// <summary>
    /// Has <paramref name="bot"/> play an unranked match against the house bot <paramref name="houseBot"/> (one of
    /// <see cref="HouseBots.All"/>) instead of waiting for a ranked one; it starts on the next tick that has room. False
    /// when there is no such house bot, or it has the bot's own name (two seats of one name), or the bot is unknown,
    /// banned, not connected or already playing.
    /// </summary>
    public bool Practise(string bot, string houseBot)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(houseBot);
        if (!HouseBots.All.Any(h => h.Name == houseBot) || string.Equals(bot, houseBot, StringComparison.OrdinalIgnoreCase) || !CanPlay(bot))
        {
            return false;
        }

        lock (_lock)
        {
            if (_disposed || !_busy.Add(bot))
            {
                return false;
            }

            _practice.Enqueue((bot, houseBot));
        }

        _matchmaker.Remove(bot);
        return true;
    }

    /// <summary>
    /// Starts what there is room for (at most <see cref="LeagueSettings.ConcurrentMatches"/> running): the practice matches
    /// asked for, then ranked matches between the bots the matchmaker pairs. Each match runs on its own task. Call it
    /// regularly: the matchmaker widens its windows as bots wait.
    /// </summary>
    public Task TickAsync()
    {
        int room;
        lock (_lock)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            while (_running.Count < _settings.ConcurrentMatches && _practice.TryDequeue(out var practice))
            {
                Start(() => PlayAsync(practice.Bot, null, practice.House));
            }

            room = _settings.ConcurrentMatches - _running.Count;
        }

        // A bot the matchmaker dropped (its lookup failed) is put back while it is connected and may play.
        foreach (var bot in Online())
        {
            Requeue(bot);
        }

        if (room <= 0)
        {
            return Task.CompletedTask;
        }

        foreach (var (a, b) in _matchmaker.Pair())
        {
            if (!Claim(a, b))
            {
                Requeue(a);
                Requeue(b);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops the league: the matches that are running are dropped, neither stored nor rated, and their bots stopped.</summary>
    public void Dispose()
    {
        Task[] running;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            running = [.. _running];
        }

        _relays.Connected -= Connected;
        _relays.Disconnected -= Disconnected;
        lock (_finishing)
        {
            _stopping.Cancel();
        }

        // A table looks at the cancellation between decisions, so each match ends within a hello or a decision.
        var ended = false;
        try
        {
            ended = Task.WaitAll(running, ArenaTable.StartLimit + _settings.DecisionLimit + TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            ended = true; // a Finished handler threw on a match's task: not the league's to report
        }

        if (ended)
        {
            _stopping.Dispose(); // else a match still running may yet look at it
        }
    }

    /// <summary>Starts a ranked match between <paramref name="a"/> and <paramref name="b"/> when both may play now and there is room.</summary>
    private bool Claim(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || !CanPlay(a) || !CanPlay(b))
        {
            return false;
        }

        lock (_lock)
        {
            if (_disposed || _running.Count >= _settings.ConcurrentMatches || _busy.Contains(a) || _busy.Contains(b))
            {
                return false;
            }

            _busy.Add(a);
            _busy.Add(b);
            Start(() => PlayAsync(a, b, null));
            return true;
        }
    }

    /// <summary>Runs a match on its own task, and forgets the task once it is done. Call under the lock.</summary>
    private void Start(Func<Task> play)
    {
        var task = Task.Run(play);
        _running.Add(task);
        _ = task.ContinueWith(
            done =>
            {
                lock (_lock)
                {
                    _running.Remove(done);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Plays one match: <paramref name="a"/> in seat 0 against the bot <paramref name="b"/> (ranked) or the house bot
    /// <paramref name="house"/> (practice), then stores it, rates it once it is stored (a ranked match), and tells the
    /// runners how it went. A failure is reported (see <see cref="Failed"/>). Its bots are free again afterwards, whatever
    /// happened.
    /// </summary>
    private async Task PlayAsync(string a, string? b, string? house)
    {
        var ranked = b is not null;
        var opponent = b ?? house!;
        StoredMatch? stored = null;
        try
        {
            var cancel = _stopping.Token;
            if (_relays.Agent(a) is not { IsAway: false } agentA || _registry.Bot(a) is not { } botA)
            {
                return;
            }

            var agentB = ranked ? _relays.Agent(b!) : null;
            var botB = ranked ? _registry.Bot(b!) : null;
            if (ranked && (agentB is not { IsAway: false } || botB is null))
            {
                return;
            }

            var settings = new ArenaSettings(
                TableFormat.FixedStacks, _settings.Hands, DealSeed.New(), _settings.DecisionLimit, SmallBlind: 1, StackBigBlinds: StackBigBlinds);

            // Every match is played by a fresh process on the owner's computer.
            agentA.Restart();
            agentB?.Restart();
            IArenaSeat seatB = agentB is not null
                ? new AgentSeat(agentB, botB!.Name)
                : new HouseSeat(HouseBots.All.First(h => h.Name == house).Make(RandomNumberGenerator.GetInt32(int.MaxValue)), house);
            using var table = new ArenaTable([new AgentSeat(agentA, botA.Name), seatB], settings);
            Tell(agentA, Starting(seatB.Name, ranked));
            if (agentB is not null)
            {
                Tell(agentB, Starting(botA.Name, ranked));
            }

            await table.RunAsync(cancel).ConfigureAwait(false);
            var match = MatchResult.FromTable(table, _clock.GetUtcNow());
            var results = table.Results();
            lock (_finishing)
            {
                if (cancel.IsCancellationRequested)
                {
                    return; // the league stopped as the match ended: it is dropped, as if it had not finished
                }

                if (ranked)
                {
                    _matchmaker.Played(botA.Name, botB!.Name);
                }

                // Stored first: a match is rated only once it is kept, so a rating never rests on a match that is gone.
                stored = _store.Add(match, ranked);
                if (ranked)
                {
                    var score = results[0].Net > results[1].Net ? 1 : results[0].Net < results[1].Net ? 0 : 0.5;
                    var perHand = match.Hands.Select(hand => ArenaBatch.HandNet(hand, 0)).ToList();
                    _ratings.Record(new RankedOutcome(botA.Name, botB!.Name, score, perHand.Count, perHand));
                }
            }

            Tell(agentA, Ended(seatB.Name, ranked, results[0], match.Hands.Count, ranked ? _ratings.Of(botA.Name) : null));
            if (agentB is not null)
            {
                Tell(agentB, Ended(botA.Name, ranked, results[1], match.Hands.Count, _ratings.Of(botB!.Name)));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The league stopped: the match is dropped.
        }
#pragma warning disable CA1031 // A match that fails (a full disk, say) must not take the league down; its bots play on.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Report(a, opponent, e);
        }
        finally
        {
            lock (_lock)
            {
                _busy.Remove(a);
                if (b is not null)
                {
                    _busy.Remove(b);
                }
            }

            Requeue(a);
            if (b is not null)
            {
                Requeue(b);
            }
        }

        if (stored is not null && Finished is { } finished)
        {
            // Each handler on its own, so one that throws neither stops the others nor goes unseen.
            foreach (var handler in finished.GetInvocationList().Cast<Action<StoredMatch>>())
            {
                try
                {
                    handler(stored);
                }
#pragma warning disable CA1031 // Whatever a handler throws is reported, not passed on to a task nobody awaits.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    Report(stored.BotA, stored.BotB, e);
                }
            }
        }
    }

    /// <summary>Logs a failure and raises <see cref="Failed"/>; a <see cref="Failed"/> handler that throws is only logged.</summary>
    private void Report(string a, string b, Exception error)
    {
        LogFailure(_logger, error, a, b);
        try
        {
            Failed?.Invoke(new LeagueFailure(a, b, error));
        }
#pragma warning disable CA1031 // A failing report must not fail the match's task.
        catch (Exception e)
#pragma warning restore CA1031
        {
            LogFailure(_logger, e, a, b);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The poker match between {BotA} and {BotB} failed")]
    private static partial void LogFailure(ILogger logger, Exception error, string botA, string botB);

    /// <summary>Whether <paramref name="bot"/> is registered, not banned, and its runner is connected.</summary>
    private bool CanPlay(string bot) => _registry.Bot(bot) is { Banned: false } && _relays.Agent(bot) is { IsAway: false };

    /// <summary>Puts <paramref name="bot"/> back in the queue when it may play and is not playing; never a banned bot.</summary>
    private void Requeue(string bot)
    {
        if (!CanPlay(bot))
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed || _busy.Contains(bot) || !_online.Contains(bot))
            {
                return;
            }

            // Under the lock, so a match claiming the bot cannot slip in between the check and the enqueue.
            _matchmaker.Enqueue(bot);
        }
    }

    private string[] Online()
    {
        lock (_lock)
        {
            return [.. _online];
        }
    }

    private static void Tell(RemoteAgent agent, LeagueStatus status) =>
        agent.Status(JsonSerializer.SerializeToElement(status, LeagueJson.Default.LeagueStatus));

    private LeagueStatus Starting(string opponent, bool ranked)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"playing {opponent}, {(ranked ? "ranked" : "practice")}, {_settings.Hands} hands");
        return new LeagueStatus("playing", text, opponent, ranked, _settings.Hands);
    }

    private static LeagueStatus Ended(string opponent, bool ranked, SeatResult result, int hands, BotRecord? record)
    {
        var outcome = result.Net > 0 ? "won" : result.Net < 0 ? "lost" : "drawn";
        var bbPer100 = hands == 0 ? 0 : result.Net / 2.0 / hands * 100;
        var text = string.Create(CultureInfo.InvariantCulture, $"{outcome} against {opponent}: {result.Net:+0;-0;0} chips, {bbPer100:+0.0;-0.0;0.0} bb/100");
        if (record is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"; rating {record.Rating.Rating:0} ± {2 * record.Rating.Deviation:0}");
        }

        return new LeagueStatus("finished", text, opponent, ranked, hands)
        {
            Outcome = outcome,
            Net = result.Net,
            BbPer100 = bbPer100,
            Rating = record?.Rating.Rating,
            Range = record is null ? null : 2 * record.Rating.Deviation,
        };
    }
}

/// <summary>What went wrong with the match between <paramref name="BotA"/> and <paramref name="BotB"/> (a house bot in practice).</summary>
public sealed record LeagueFailure(string BotA, string BotB, Exception Error);

/// <summary>
/// What the league tells a runner to show its owner: <paramref name="State"/> "playing" at the start of a match and
/// "finished" at its end (then with the outcome, chips, bb/100 and, for a ranked match, the new rating and its range),
/// and <paramref name="Text"/> to print as it is.
/// </summary>
public sealed record LeagueStatus(string State, string Text, string Opponent, bool Ranked, int Hands)
{
    public string? Outcome { get; init; }

    public int? Net { get; init; }

    public double? BbPer100 { get; init; }

    public double? Rating { get; init; }

    public double? Range { get; init; }
}

/// <summary>The JSON of the league's status messages.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LeagueStatus))]
internal sealed partial class LeagueJson : JsonSerializerContext;
