using Jentl.Core.Audio;
using Jentl.Core.Content;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Jentl.Core.UI;
using Games.Input;
using Microsoft.Xna.Framework.Graphics;

namespace Games;

internal static class Sound
{
    public const string Navigate = "navigate";
    public const string Completed = "completed";
    public const string Card = "card";
    public const string Error = "error";
    public const string Hit = "hit";
    public const string Pickup = "pickup";
    public const string LevelUp = "levelup";
    public const string Fanfare = "fanfare";
    public const string Chips = "chips";
    public const string Click = "click";
    public const string Lose = "lose";
}

/// <summary>What the platform supports; a browser tab cannot quit or switch to fullscreen by itself.</summary>
/// <param name="HintTime">
/// Search budget for a hint. WebAssembly has a single thread, so there the search holds up the frame
/// and must stay short.
/// </param>
internal sealed record PlatformFeatures(bool CanQuit, bool CanFullscreen, TimeSpan HintTime)
{
    public static PlatformFeatures Desktop { get; } = new(CanQuit: true, CanFullscreen: true, TimeSpan.FromSeconds(3));

    public static PlatformFeatures Browser { get; } = new(CanQuit: false, CanFullscreen: false, TimeSpan.FromSeconds(1));
}

/// <summary>
/// One of the games: what the shared shell (window, options, audio, languages, the test harness) needs to know
/// to run it. Each app folder has one, made by its <c>Entry.Create()</c>.
/// </summary>
internal interface IGameApp
{
    /// <summary>The name players see: the window title and the title screen.</summary>
    string Title { get; }

    /// <summary>Where its saves live: a folder in the user's data folder on the desktop, a key prefix in the browser.</summary>
    string SaveName { get; }

    /// <summary>Loads what the game's scenes need (levels, art, save files) into <paramref name="context"/>.</summary>
    void Load(GameContext context, GraphicsDevice device);

    /// <summary>The title screen, where Back and "Main menu" lead.</summary>
    Scene MainMenu(GameContext context);

    /// <summary>The scenes to start with, bottom first: the title screen, perhaps with a game on top (<c>--level</c>).</summary>
    IReadOnlyList<Scene> Start(GameContext context) => [MainMenu(context)];

    /// <summary>The game's own rows in the Options panel, shown above Back.</summary>
    IEnumerable<FormRow> Options(GameContext context) => [];
}

/// <summary>
/// Shared services handed to every scene: fonts, input, audio, settings, assets, the platform. Things only one game
/// needs (Sokoban's levels, the card art) are added by its app with <see cref="Add{T}"/> and read through extension
/// properties of that app (<c>context.Tileset</c>).
/// </summary>
internal sealed class GameContext(
    IGameApp app,
    GameOptions options,
    Fonts fonts,
    ActionMap<GameAction> actions,
    SoundPlayer sounds,
    SavedFile<Settings> settings,
    Action<bool> setFullscreen,
    PlatformFeatures platform,
    AssetStore assets,
    ISaveStore? store) : IDisposable
{
    private readonly Dictionary<Type, object> _services = [];

    public IGameApp App { get; } = app;

    public GameOptions Options { get; } = options;

    public Fonts Fonts { get; } = fonts;

    public ActionMap<GameAction> Actions { get; } = actions;

    public SoundPlayer Sounds { get; } = sounds;

    public SavedFile<Settings> Settings { get; } = settings;

    public Action<bool> SetFullscreen { get; } = setFullscreen;

    public PlatformFeatures Platform { get; } = platform;

    public AssetStore Assets { get; } = assets;

    /// <summary>Where the game keeps its files; null keeps them in memory only (scripted runs).</summary>
    public ISaveStore? Store { get; } = store;

    /// <summary>A fixed deal or seed for reproducible runs (<c>--deal</c>).</summary>
    public int? Deal => Options.Deal;

    /// <summary>The game server for online play: chosen at start-up, or one on this machine.</summary>
    public Uri GameServer => Options.Server ?? DefaultServer;

    /// <summary>A server run locally with <c>dotnet run --project src/GameServer</c>.</summary>
    public static Uri DefaultServer { get; } = new("ws://localhost:5080/play");

    /// <summary>Adds a service for this game's scenes; disposable ones are disposed with the context.</summary>
    public T Add<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
        return service;
    }

    /// <exception cref="InvalidOperationException">The app didn't add one.</exception>
    public T Get<T>()
        where T : class =>
        _services.TryGetValue(typeof(T), out var service)
            ? (T)service
            : throw new InvalidOperationException($"{App.Title} has no {typeof(T).Name}.");

    public void Dispose()
    {
        foreach (var service in _services.Values.OfType<IDisposable>())
        {
            service.Dispose();
        }

        _services.Clear();
    }
}
