using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Sokoban.Model;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

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
        if (play.HasNextLevel)
        {
            items.Add(new MenuItem(T("Next level"), () =>
            {
                Scenes.Pop();
                play.NextLevel();
            }));
        }

        items.Add(new MenuItem(T("Replay"), () =>
        {
            play.Restart();
            Scenes.Pop();
        }));
        items.Add(new MenuItem(T("Select level"), () => Navigation.ToLevelSelect(Scenes, context, play.SelectTarget)));
        items.Add(new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
        _menu = new Menu(context.Sounds, [.. items]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var session = _play.Session;
        var title = _play.HasNextLevel ? T("Level complete!") : T("Last level done!");
        var lines = new List<PanelLine>
        {
            new(Format.Result(session.MoveCount, session.PushCount, _play.Time), Palette.Text),
        };

        // A first solve is its own best, so only later solves compare against the record.
        if (_play.LastImprovement != Improvement.FirstSolve)
        {
            if (Format.Improvement(_play.LastImprovement) is { } improved)
            {
                lines.Add(new PanelLine(improved, Palette.Accent));
            }
            else if (_context.Progress.Progress.Get(session.Level) is { } record)
            {
                lines.Add(new PanelLine(Format.Best(record), Palette.Muted));
            }
        }

        OverlayPanel.Draw(batch, viewport, _context.Fonts, title, lines, _menu);
    }
}
