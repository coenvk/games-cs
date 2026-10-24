using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

internal sealed class CardPauseScene : Scene
{
    private readonly GameContext _context;
    private readonly CardGameScene _game;
    private readonly Menu _menu;

    public CardPauseScene(GameContext context, CardGameScene game)
    {
        _context = context;
        _game = game;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Restart this deal"), () =>
            {
                game.RestartDeal();
                Scenes.Pop();
            }),
            new MenuItem(T("New deal"), () =>
            {
                Scenes.Pop();
                game.NewDeal();
            }),
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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Paused"), [new PanelLine($"{_game.Name} #{_game.Deal}", Palette.Muted)], _menu);
}

internal sealed class CardWonScene : Scene
{
    private readonly GameContext _context;
    private readonly CardGameScene _game;
    private readonly Menu _menu;

    public CardWonScene(GameContext context, CardGameScene game)
    {
        _context = context;
        _game = game;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("New deal"), () =>
            {
                Scenes.Pop();
                game.NewDeal();
            }),
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
        OverlayPanel.Draw(
            batch,
            viewport,
            _context.Fonts,
            T("You won!"),
            [new PanelLine($"{_game.Name} #{_game.Deal}: {Format.Moves(_game.MoveCount)}, {Format.Duration(_game.Time)}", Palette.Text)],
            _menu);
}

/// <summary>Picks how many suits a Spider deal uses: fewer suits, easier game.</summary>
internal sealed class SpiderSuitsScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public SpiderSuitsScene(GameContext context)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("1 suit (easy)"), () => Start(1)),
            new MenuItem(T("2 suits (medium)"), () => Start(2)),
            new MenuItem(T("4 suits (hard)"), () => Start(4)),
            new MenuItem(T("Back"), Back));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Back();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Spider", [new PanelLine(T("How many suits?"), Palette.Muted)], _menu);

    private void Start(int suits) => Scenes.Replace(new SpiderScene(_context, suits), Navigation.Fade());

    private void Back() => Scenes.Pop();
}
