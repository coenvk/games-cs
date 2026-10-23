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
    private const int QuitItem = 4;

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
            _menu.Selected = QuitItem;
            return;
        }

        _menu.Update(input, _context.Actions, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var centerX = viewport.Center.X;
        var titleTop = viewport.Y + viewport.Height / 6;

        batch.DrawText(fonts.Title, "Sokoban", new Vector2(centerX, titleTop), Palette.Accent, Align.Center);
        DrawMascot(batch, centerX, titleTop + (int)fonts.Title.LineHeight + 8);

        var menuTop = viewport.Y + viewport.Height / 2 + 20;
        _menu.Draw(batch, fonts.Body, centerX, menuTop);

        var total = _context.AllLevels.Count();
        var solved = _context.Progress.Progress.SolvedCount(_context.AllLevels);
        if (solved > 0)
        {
            batch.DrawText(
                fonts.Small,
                string.Create(CultureInfo.InvariantCulture, $"{solved} of {total} levels solved"),
                new Vector2(centerX, menuTop + _menu.Height(fonts.Body) + 16),
                Palette.Muted,
                Align.Center,
                shadow: false);
        }

        batch.DrawText(
            fonts.Small,
            "Arrows to choose, Enter to select",
            new Vector2(centerX, viewport.Bottom - 48),
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
        return new Menu(
            context.Sounds,
            new MenuItem(started ? "Continue" : "Play", () => Scenes.Push(new PlayScene(context, context.Continue), Navigation.Fade())),
            new MenuItem("Select level", () => Scenes.Push(new LevelSelectScene(context, context.Continue), Navigation.Fade())),
            new MenuItem("Random level", () => Scenes.Push(new GenerateScene(context), Navigation.Fade())),
            new MenuItem("Options", () => Scenes.Push(new OptionsScene(context))),
            new MenuItem("Quit", () => Scenes.Pop(Navigation.Fade())))
        {
            Selected = selected,
        };
    }

    /// <summary>A player pushing a crate towards a goal, under the title.</summary>
    private void DrawMascot(SpriteBatch batch, int centerX, int top)
    {
        const int size = 64;
        var theme = _context.Tileset.ThemeFor(1);
        var left = centerX - 3 * size / 2;
        batch.Draw(_context.Tileset.PlayerFrames(Direction.Right)[0], new Rectangle(left, top, size, size), Color.White);
        batch.Draw(theme.Crate, new Rectangle(left + size, top, size, size), Color.White);
        batch.Draw(theme.Goal, new Rectangle(left + 2 * size, top, size, size), Color.White);
    }
}
