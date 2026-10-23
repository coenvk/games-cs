using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

internal sealed class SolitairePauseScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public SolitairePauseScene(GameContext context, SolitaireScene game)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem("Resume", () => Scenes.Pop()),
            new MenuItem("Restart this deal", () =>
            {
                game.RestartDeal();
                Scenes.Pop();
            }),
            new MenuItem("New deal", () =>
            {
                Scenes.Pop();
                game.NewDeal();
            }),
            new MenuItem("Options", () => Scenes.Push(new OptionsScene(context))),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
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

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Paused", [new PanelLine("Solitaire", Palette.Muted)], _menu);
}

internal sealed class SolitaireWonScene : Scene
{
    private readonly GameContext _context;
    private readonly SolitaireScene _game;
    private readonly Menu _menu;

    public SolitaireWonScene(GameContext context, SolitaireScene game)
    {
        _context = context;
        _game = game;
        _menu = new Menu(
            context.Sounds,
            new MenuItem("New deal", () =>
            {
                Scenes.Pop();
                game.NewDeal();
            }),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
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

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(
            batch,
            viewport,
            _context.Fonts,
            "You won!",
            [new PanelLine($"{Format.Count(_game.MoveCount, "move")}, {Format.Duration(_game.Time)}", Palette.Text)],
            _menu);
}
