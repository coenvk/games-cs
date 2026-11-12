using Games.Input;
using Games.Scenes;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>
/// An app holding several games of a kind (the word games, the puzzles): a title screen to choose one from, each
/// game then running as it would on its own, with "All games" on its title screen to come back. Each game keeps its
/// saves apart, its files' names starting with its own <see cref="IGameApp.SaveName"/>; the settings, controls and
/// music are the suite's, shared by its games.
/// </summary>
internal sealed class SuiteApp(string title, string saveName, IReadOnlyList<IGameApp> games) : IGameApp
{
    public IReadOnlyList<IGameApp> Games { get; } = games is { Count: > 0 } ? games : throw new ArgumentException("A suite needs a game.", nameof(games));

    /// <summary>The game being played, or null on the suite's own title screen.</summary>
    public IGameApp? Current { get; private set; }

    /// <summary>The current game's name, or the suite's (in the player's language) on its own title screen.</summary>
    public string Title => Current?.Title ?? T(title);

    public string SaveName { get; } = saveName;

    /// <summary>Every game's controls, each once, for the Controls screen.</summary>
    public IReadOnlyList<GameAction> ControlActions => [.. Games.SelectMany(g => g.ControlActions).Distinct()];

    /// <summary>Loads every game, each seeing only its own saves.</summary>
    public void Load(GameContext context, GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(context);
        var store = context.Store;
        try
        {
            foreach (var game in Games)
            {
                context.Store = store is null ? null : new PrefixedStore(store, game.SaveName + "-");
                game.Load(context, device);
            }
        }
        finally
        {
            context.Store = store;
        }
    }

    /// <summary>The current game's title screen, or the suite's own while none is chosen.</summary>
    public Scene MainMenu(GameContext context) => Current is { } game ? game.MainMenu(context) : Choice(context);

    public IEnumerable<Jentl.Core.UI.FormRow> Options(GameContext context) => Current?.Options(context) ?? [];

    /// <summary>Starts a game: its title screen in place of the suite's.</summary>
    public void Enter(IGameApp game, SceneManager scenes, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        Current = Games.Contains(game) ? game : throw new ArgumentException("The game isn't in this suite.", nameof(game));
        Navigation.ToMainMenu(scenes, context);
    }

    /// <summary>Back to the suite's title screen, from a game's.</summary>
    public void Leave(SceneManager scenes, GameContext context)
    {
        Current = null;
        Navigation.ToMainMenu(scenes, context);
    }

    private TitleScene Choice(GameContext context) => new(
        context,
        title => [.. Games.Select(g => new Jentl.Core.UI.MenuItem(g.Title, () => Enter(g, title.Manager, context)))],
        () => T("Choose a game"));
}
