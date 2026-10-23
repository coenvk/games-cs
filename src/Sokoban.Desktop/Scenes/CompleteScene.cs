using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Shown over a solved level. Esc closes it to look at the board; Enter there reopens it.</summary>
internal sealed class CompleteScene : Scene
{
    private readonly GameContext _context;
    private readonly PlayScene _play;
    private readonly Menu _menu;

    public CompleteScene(GameContext context, PlayScene play)
    {
        _context = context;
        _play = play;

        var items = new List<MenuItem>();
        if (!play.IsLastLevel)
        {
            items.Add(new MenuItem("Next level", () =>
            {
                Scenes.Pop();
                play.GoToLevel(play.LevelIndex + 1);
            }));
        }

        items.Add(new MenuItem("Replay", () =>
        {
            play.Restart();
            Scenes.Pop();
        }));
        items.Add(new MenuItem("Select level", () => Navigation.ToLevelSelect(Scenes, context, play.LevelIndex)));
        items.Add(new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
        _menu = new Menu([.. items]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, _context.Actions, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var session = _play.Session;
        var title = _play.IsLastLevel ? "All levels done!" : "Level complete!";
        var stats = string.Create(CultureInfo.InvariantCulture, $"{session.MoveCount} moves, {session.PushCount} pushes");
        OverlayPanel.Draw(batch, viewport, _context.Fonts, title, stats, _menu);
    }
}
