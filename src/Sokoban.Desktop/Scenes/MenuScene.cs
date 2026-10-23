using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Sokoban.Model;

namespace Sokoban.Desktop.Scenes;

internal sealed class MenuScene : Scene
{
    private const int QuitItem = 7;

    private readonly GameContext _context;
    private Menu _menu;

    public MenuScene(GameContext context)
    {
        _context = context;
        _menu = BuildMenu();
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        // Esc moves to Quit rather than quitting outright, so a stray press is harmless.
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            if (_context.Platform.CanQuit)
            {
                _menu.Selected = QuitItem;
            }

            return;
        }

        _menu.Update(input, _context.Actions, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var centerX = viewport.Center.X;
        var footerTop = viewport.Bottom - (int)fonts.Small.LineHeight - fonts.Px(24);

        // Title, mascot, menu and solved count as one column, centred in the space above the footer,
        // so a longer menu or a shorter screen never runs into the footer.
        var gap = fonts.Px(24);
        var titleHeight = (int)fonts.Title.LineHeight;
        var mascotHeight = fonts.Px(64);
        var menuHeight = _menu.Height(fonts.Body);
        var countHeight = (int)fonts.Small.LineHeight;
        var column = titleHeight + mascotHeight + 2 * gap + menuHeight + gap / 2 + countHeight;
        var top = viewport.Y + Math.Max(0, (footerTop - viewport.Y - column) / 2);

        batch.DrawText(fonts.Title, "Sokoban", new Vector2(centerX, top), Palette.Accent, Align.Center);
        DrawMascot(batch, centerX, top + titleHeight);

        var menuTop = top + titleHeight + mascotHeight + 2 * gap;
        _menu.Draw(batch, fonts.Body, centerX, menuTop);

        var total = _context.AllLevels.Count();
        var solved = _context.Progress.Progress.SolvedCount(_context.AllLevels);
        if (solved > 0)
        {
            batch.DrawText(
                fonts.Small,
                string.Create(CultureInfo.InvariantCulture, $"{solved} of {total} levels solved"),
                new Vector2(centerX, menuTop + menuHeight + gap / 2),
                Palette.Muted,
                Align.Center,
                shadow: false);
        }

        batch.DrawText(
            fonts.Small,
            "Tap, or use the arrow keys and Enter",
            new Vector2(centerX, footerTop),
            Palette.Muted,
            Align.Center,
            shadow: false);
    }

    // Progress may have changed while another scene was on top, so the labels are rebuilt.
    protected override void OnUncovered() => _menu = BuildMenu(_menu.Selected);

    private Menu BuildMenu(int selected = 0)
    {
        var context = _context;
        var started = context.Progress.Progress.SolvedCount(context.AllLevels) > 0;
        List<MenuItem> items =
        [
            new(started ? "Continue" : "Play", () => Scenes.Push(new PlayScene(context, context.Continue), Navigation.Fade())),
            new("Select level", () => Scenes.Push(new LevelSelectScene(context, context.Continue), Navigation.Fade())),
            new("Random level", () => Scenes.Push(new GenerateScene(context), Navigation.Fade())),
            new("Card games", () => Scenes.Push(new CardGamesScene(context))),
            new("Adventure", () => Scenes.Push(context.Adventures.Exists ? new AdventureMenuScene(context) : AdventureScene.New(context), Navigation.Fade())),
            new("Code lock", () => Scenes.Push(new CodeLockLevelsScene(context))),
            new("Options", () => Scenes.Push(new OptionsScene(context))),
        ];
        if (context.Platform.CanQuit)
        {
            items.Add(new MenuItem("Quit", () => Scenes.Pop(Navigation.Fade())));
        }

        return new Menu(context.Sounds, [.. items])
        {
            Selected = selected,
        };
    }

    /// <summary>A player pushing a crate towards a goal, under the title.</summary>
    private void DrawMascot(SpriteBatch batch, int centerX, int top)
    {
        var size = _context.Fonts.Px(64);
        var theme = _context.Tileset.ThemeFor(1);
        var left = centerX - 3 * size / 2;
        batch.Draw(_context.Tileset.PlayerFrames(Direction.Right)[0], new Rectangle(left, top, size, size), Color.White);
        batch.Draw(theme.Crate, new Rectangle(left + size, top, size, size), Color.White);
        batch.Draw(theme.Goal, new Rectangle(left + 2 * size, top, size, size), Color.White);
    }
}
