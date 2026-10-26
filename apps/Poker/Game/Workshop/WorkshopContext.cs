using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Jentl.Net;
using Poker;
using Poker.Arena;
using PokerBots;

namespace Games.Workshop;

/// <summary>A match to watch: a bot for each seat in order (null is the player's own seat) and the rules.</summary>
internal sealed record MatchRequest(IReadOnlyList<BotEntry?> Seats, ArenaSettings Settings);

/// <summary>A batch of matches to run between these bots.</summary>
internal sealed record BatchRequest(IReadOnlyList<BotEntry> Bots, BatchSettings Settings);

/// <summary>
/// What the workshop's scenes share: where the bots and the matches live, how to find the bots, and how to seat one.
/// The scenes the setup, the menu and a finished match lead to are made through <see cref="WatchMatch"/>,
/// <see cref="RunBatch"/>, <see cref="PastMatches"/> and <see cref="MatchResults"/>, so another
/// screen can be put in by setting one of them.
/// </summary>
internal sealed class WorkshopContext
{
    private readonly RunningBots _running;

    public WorkshopContext(GameContext game)
    {
        Game = game;

        // One list for the whole app, whichever workshop seated the bot: the context disposes it when the app quits.
        _running = game.Has<RunningBots>() ? game.Get<RunningBots>() : game.Add(new RunningBots());
        WatchMatch = request => WatchScene.Open(this, request);
        RunBatch = request => new BatchScene(this, request);
        PastMatches = () => new MatchesScene(this);
        MatchResults = (match, path) => new ResultsScene(this, match, path);
    }

    public GameContext Game { get; }

    /// <summary>The scene that plays a match the player watches.</summary>
    public Func<MatchRequest, Scene> WatchMatch { get; set; }

    /// <summary>The scene that runs a batch and shows its table.</summary>
    public Func<BatchRequest, Scene> RunBatch { get; set; }

    /// <summary>The scene that lists the matches played before.</summary>
    public Func<Scene> PastMatches { get; set; }

    /// <summary>The scene that shows a finished match, with the file it was saved to (null when saving failed).</summary>
    public Func<MatchResult, string?, Scene> MatchResults { get; set; }

    /// <summary>The player's bots: <c>*.bot.json</c> descriptions and C# DLLs.</summary>
    public string BotsFolder => Path.Combine(SaveFolder, "bots");

    /// <summary>The finished matches, one file each.</summary>
    public string MatchesFolder => Path.Combine(SaveFolder, "workshop", "matches");

    /// <summary>The hand histories written out as text.</summary>
    public string HistoriesFolder => Path.Combine(SaveFolder, "workshop", "histories");

    /// <summary>
    /// How C# bots are run: the bot host that was copied next to the app, through its own program (which finds the .NET
    /// runtime where it is installed, also when the app was started from the Finder without dotnet on its PATH), or
    /// with <c>dotnet</c> when it has none.
    /// </summary>
    public AgentCommand Host { get; } = AgentCommand.DotNet(Path.Combine(AppContext.BaseDirectory, "PokerBotHost.dll"));

    // The save folder of the desktop app; scripted runs without one keep their workshop in a temporary folder.
    private string SaveFolder => Game.Store is SaveStore store ? store.Directory : Path.Combine(Path.GetTempPath(), Game.App.SaveName);

    /// <summary><paramref name="path"/> as it is shown to the player: from the save folder on, as it would be too long whole.</summary>
    public string Shown(string path) => Path.GetRelativePath(SaveFolder, path);

    /// <summary>Makes the folders if they are not there yet.</summary>
    public void EnsureFolders()
    {
        Directory.CreateDirectory(BotsFolder);
        Directory.CreateDirectory(MatchesFolder);
        Directory.CreateDirectory(HistoriesFolder);
    }

    /// <summary>The house bots, then the ones in the bots folder (some of which may be unreadable: no command).</summary>
    public IReadOnlyList<BotEntry> Bots() =>
    [
        .. HouseBots.All.Select(b => new BotEntry(b.Name, BotKind.House, null, "")),
        .. BotCatalog.Scan(BotsFolder, Host),
    ];

    /// <summary>
    /// Seats <paramref name="bot"/>, with <paramref name="seed"/> for a house bot's own randomness, under <paramref name="name"/>
    /// (the bot's own when null; see <see cref="SeatNames"/> for a bot seated more than once).
    /// </summary>
    /// <exception cref="InvalidOperationException">The bot cannot be run: its description is unreadable.</exception>
    public IArenaSeat Seat(BotEntry bot, int seed, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bot);
        if (bot.Kind == BotKind.House)
        {
            return new HouseSeat(HouseBots.All.First(b => b.Name == bot.Name).Make(seed), name);
        }

        var command = bot.Command ?? throw new InvalidOperationException($"{bot.Name} cannot be run: {bot.Source}");
        return _running.Track(ProcessSeat.Start(command, name ?? bot.Name));
    }
}

/// <summary>
/// The bots running in processes of their own that are seated now. A seat leaves the list when it is disposed; when
/// the app quits, the game context disposes this list, which stops every bot still in it, so that a bot stuck in a
/// loop does not outlive the app even while a match or a batch is being played.
/// </summary>
internal sealed class RunningBots : IDisposable
{
    private readonly HashSet<IArenaSeat> _seats = [];
    private readonly Lock _lock = new();
    private bool _stopped;

    /// <summary>
    /// Keeps <paramref name="seat"/> in the list until the seat returned is disposed. Once the app is quitting no bot is
    /// started any more: a batch still running would otherwise seat the next run's bots after the others were stopped.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The app is quitting; <paramref name="seat"/> has been disposed.</exception>
    public IArenaSeat Track(IArenaSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        lock (_lock)
        {
            if (!_stopped)
            {
                _seats.Add(seat);
                return new Tracked(this, seat);
            }
        }

        seat.Dispose();
        throw new ObjectDisposedException(nameof(RunningBots), "The app is quitting.");
    }

    /// <summary>Stops every bot in the list, and every one seated from now on.</summary>
    public void Dispose()
    {
        IArenaSeat[] seats;
        lock (_lock)
        {
            _stopped = true;
            seats = [.. _seats];
            _seats.Clear();
        }

        foreach (var seat in seats)
        {
            try
            {
                seat.Dispose();
            }
#pragma warning disable CA1031 // The app is quitting: one bot failing to stop must not keep the others running.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    private void Remove(IArenaSeat seat)
    {
        lock (_lock)
        {
            _seats.Remove(seat);
        }
    }

    /// <summary>The seat as the table uses it; disposing it takes it off the list.</summary>
    private sealed class Tracked(RunningBots list, IArenaSeat seat) : IArenaSeat
    {
        public string Name => seat.Name;

        public string? Problem => seat.Problem;

        public bool IsGone => seat.IsGone;

        public bool HasTimeLimit => seat.HasTimeLimit;

        public Task StartAsync(MatchStart start, TimeSpan limit) => seat.StartAsync(start, limit);

        public void Notify(Message message) => seat.Notify(message);

        public Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel) => seat.AskAsync(decision, cancel);

        public void Dispose()
        {
            list.Remove(seat);
            seat.Dispose();
        }
    }
}
