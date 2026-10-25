using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Jentl.Net;
using Poker;
using Poker.Arena;

namespace Games.Workshop;

/// <summary>A match to watch: a bot for each seat in order (null is the player's own seat) and the rules.</summary>
internal sealed record MatchRequest(IReadOnlyList<BotEntry?> Seats, ArenaSettings Settings);

/// <summary>A batch of matches to run between these bots.</summary>
internal sealed record BatchRequest(IReadOnlyList<BotEntry> Bots, BatchSettings Settings);

/// <summary>
/// What the workshop's scenes share: where the bots and the matches live, how to find the bots, and how to seat one.
/// The three scenes the setup and the menu lead to are made through <see cref="WatchMatch"/>, <see cref="RunBatch"/>
/// and <see cref="PastMatches"/>, so a screen replaces its stand-in by setting one of them.
/// </summary>
internal sealed class WorkshopContext(GameContext game)
{
    public GameContext Game { get; } = game;

    /// <summary>The scene that plays a match the player watches.</summary>
    public Func<MatchRequest, Scene> WatchMatch { get; set; } = _ => new ComingSoonScene(game, "Watch a match");

    /// <summary>The scene that runs a batch and shows its table.</summary>
    public Func<BatchRequest, Scene> RunBatch { get; set; } = _ => new ComingSoonScene(game, "Run a batch");

    /// <summary>The scene that lists the matches played before.</summary>
    public Func<Scene> PastMatches { get; set; } = () => new ComingSoonScene(game, "Past matches");

    /// <summary>The player's bots: <c>*.bot.json</c> descriptions and C# DLLs.</summary>
    public string BotsFolder => Path.Combine(SaveFolder, "bots");

    /// <summary>The finished matches, one file each.</summary>
    public string MatchesFolder => Path.Combine(SaveFolder, "workshop", "matches");

    /// <summary>The hand histories written out as text.</summary>
    public string HistoriesFolder => Path.Combine(SaveFolder, "workshop", "histories");

    /// <summary>How C# bots are run: the bot host that was copied next to the app, started with <c>dotnet</c>.</summary>
    public AgentCommand Host { get; } = new("dotnet", [Path.Combine(AppContext.BaseDirectory, "PokerBotHost.dll")]);

    // The save folder of the desktop app; scripted runs without one keep their workshop in a temporary folder.
    private string SaveFolder => Game.Store is SaveStore store ? store.Directory : Path.Combine(Path.GetTempPath(), Game.App.SaveName);

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

    /// <summary>Seats <paramref name="bot"/>, with <paramref name="seed"/> for a house bot's own randomness.</summary>
    /// <exception cref="InvalidOperationException">The bot cannot be run: its description is unreadable.</exception>
    // Kept an instance member, like the rest of the workshop's API, though it needs none of the context's state yet.
#pragma warning disable CA1822
    public IArenaSeat Seat(BotEntry bot, int seed)
    {
        ArgumentNullException.ThrowIfNull(bot);
        if (bot.Kind == BotKind.House)
        {
            return new HouseSeat(HouseBots.All.First(b => b.Name == bot.Name).Make(seed));
        }

        return new ProcessSeat(bot.Command ?? throw new InvalidOperationException($"{bot.Name} cannot be run: {bot.Source}"), bot.Name);
    }
#pragma warning restore CA1822
}
