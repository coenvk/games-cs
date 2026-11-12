using System.Globalization;
using Jentl.Core.UI;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Every level, with its stars once won; levels open one after another.</summary>
internal sealed class WordLevelsScene : PanelScene
{
    public WordLevelsScene(GameContext context)
        : base(context)
    {
        var words = context.Words;
        Menu = new Menu(
            context.Sounds,
            [
                .. Enumerable.Range(0, words.Levels.Count).Where(words.IsOpen).Select(level => new MenuItem(Label(words, level), () =>
                {
                    Scenes.Pop();
                    Scenes.Push(new WordPlayScene(context, level), Navigation.Fade());
                })),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    protected override string Title => T("Select level");

    private static string Label(WordStore words, int level) => words.Best(level) is { } best
        ? T("Level {0}  {1}  {2} moves", level + 1, new string('★', best.Stars) + new string('☆', 3 - best.Stars), best.Moves)
        : T("Level {0}", level + 1);
}

internal sealed class WordPauseScene : PanelScene
{
    private readonly WordPlayScene _game;

    public WordPauseScene(GameContext context, WordPlayScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Restart"), () =>
            {
                Scenes.Pop();
                _game.Restart();
            }),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => _game.Puzzle.Title;

    protected override IReadOnlyList<PanelLine> Lines =>
        [new(T("{0} of {1} matched, {2} moves", _game.Game.Solved.Count, _game.Game.Level.Categories.Count, _game.Game.Moves), Palette.Muted)];
}

internal sealed class WordWonScene : PanelScene
{
    private readonly WordPlayScene _game;
    private readonly bool _best;

    public WordWonScene(GameContext context, WordPlayScene game, bool best)
        : base(context)
    {
        (_game, _best) = (game, best);
        var next = game.Puzzle.Kind switch
        {
            PuzzleKind.Level when game.Number < context.Words.Levels.Count - 1 => T("Next level"),
            PuzzleKind.Random => T("Another puzzle"),
            _ => null,
        };
        Menu = new Menu(
            context.Sounds,
            [
                .. next is null ? Array.Empty<MenuItem>() : [new MenuItem(next, () =>
                {
                    Scenes.Pop();
                    _game.NextLevel();
                })],
                new MenuItem(T("Play again"), () =>
                {
                    Scenes.Pop();
                    _game.Restart();
                }),
                new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)),
            ]);
    }

    protected override string Title => _game.Puzzle.Kind == PuzzleKind.Level ? T("Level {0} done!", _game.Number + 1) : T("Solved!");

    protected override IReadOnlyList<PanelLine> Lines => _game.Puzzle.Kind switch
    {
        PuzzleKind.Level =>
        [
            new(new string('★', _game.Game.Stars) + new string('☆', 3 - _game.Game.Stars), Palette.Accent),
            new(T("{0} moves (par {1})", _game.Game.Moves, _game.Game.Par), Palette.Text),
            new(_best ? T("Your best for this level") : T("Every word in its place"), Palette.Muted),
        ],
        PuzzleKind.Daily =>
        [
            new(T("{0} in {1} moves", WordPlayScene.Clock(_game.Time), _game.Game.Moves), Palette.Accent),
            new(N(Context.Words.Streak(DateOnly.FromDateTime(DateTime.Now)), "{0} day in a row", "{0} days in a row"), Palette.Text),
            new(T("A new puzzle comes tomorrow"), Palette.Muted),
        ],
        _ =>
        [
            new(T("{0} in {1} moves", WordPlayScene.Clock(_game.Time), _game.Game.Moves), Palette.Accent),
            new(T("Every word in its place"), Palette.Muted),
        ],
    };

    protected override void OnBack() => Navigation.ToMainMenu(Scenes, Context);
}
