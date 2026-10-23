using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Picks a card game from the main menu.</summary>
internal sealed class CardGamesScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public CardGamesScene(GameContext context)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem("Klondike", () => Scenes.Replace(new KlondikeScene(context), Navigation.Fade())),
            new MenuItem("FreeCell", () => Scenes.Replace(new FreeCellScene(context), Navigation.Fade())),
            new MenuItem("Back", () => Scenes.Pop()));
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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Card games", [], _menu);
}

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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Paused", [new PanelLine($"{_game.Name} #{_game.Deal}", Palette.Muted)], _menu);
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
            [new PanelLine($"{_game.Name} #{_game.Deal}: {Format.Count(_game.MoveCount, "move")}, {Format.Duration(_game.Time)}", Palette.Text)],
            _menu);
}
