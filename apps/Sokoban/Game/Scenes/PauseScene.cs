using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

internal sealed class PauseScene : Scene
{
    private readonly GameContext _context;
    private readonly PlayScene _play;
    private readonly Menu _menu;

    public PauseScene(GameContext context, PlayScene play)
    {
        _context = context;
        _play = play;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Restart level"), () =>
            {
                play.Restart();
                Scenes.Pop();
            }),
            new MenuItem(T("Select level"), () => Navigation.ToLevelSelect(Scenes, context, play.SelectTarget)),
            new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
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

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Paused"), [new PanelLine(_play.Title, Palette.Muted)], _menu);
}
