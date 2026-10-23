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
    private const int QuitItem = 2;

    private readonly GameContext _context;
    private readonly Menu _menu;

    public MenuScene(GameContext context)
    {
        _context = context;
        _menu = new Menu(
            new MenuItem("Play", () => Scenes.Push(new PlayScene(context, 0), Navigation.Fade())),
            new MenuItem("Select level", () => Scenes.Push(new LevelSelectScene(context, 0), Navigation.Fade())),
            new MenuItem("Quit", () => Scenes.Pop(Navigation.Fade())));
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

        batch.DrawText(
            fonts.Small,
            "Arrows to choose, Enter to select",
            new Vector2(centerX, viewport.Bottom - 48),
            Palette.Muted,
            Align.Center,
            shadow: false);
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
