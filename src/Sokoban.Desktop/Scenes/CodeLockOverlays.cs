using System.Globalization;
using CodeLock;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Picks how hard the lock is, from the main menu.</summary>
internal sealed class CodeLockLevelsScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public CodeLockLevelsScene(GameContext context)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem("Easy: 3 digits", () => Start(LockRules.Easy)),
            new MenuItem("Normal: 4 digits", () => Start(LockRules.Normal)),
            new MenuItem("Hard: 5, may repeat", () => Start(LockRules.Hard)),
            new MenuItem("Back", () => Scenes.Pop()));
        _menu.Selected = 1;
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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Code lock", [new PanelLine("Crack the code in ten tries.", Palette.Muted)], _menu);

    private void Start(LockRules rules) => Scenes.Replace(new CodeLockScene(_context, rules), Navigation.Fade());
}

internal sealed class CodeLockPauseScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;
    private readonly string _name;

    public CodeLockPauseScene(GameContext context, CodeLockScene game)
    {
        _context = context;
        _name = $"{game.Name} lock #{game.Seed}";
        _menu = new Menu(
            context.Sounds,
            new MenuItem("Resume", () => Scenes.Pop()),
            new MenuItem("New lock", () =>
            {
                Scenes.Pop();
                game.NewLock();
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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Paused", [new PanelLine(_name, Palette.Muted)], _menu);
}

/// <summary>The lock opened, or the tries ran out (and the code is revealed).</summary>
internal sealed class CodeLockEndScene : Scene
{
    private readonly GameContext _context;
    private readonly CodeLockScene _game;
    private readonly Menu _menu;

    public CodeLockEndScene(GameContext context, CodeLockScene game)
    {
        _context = context;
        _game = game;
        _menu = new Menu(
            context.Sounds,
            new MenuItem("New lock", () =>
            {
                Scenes.Pop();
                game.NewLock();
            }),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input) => _menu.Update(input, _context.Actions, elapsed);

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var game = _game.Game;
        var tries = game.Attempts.Count == 1 ? "1 try" : string.Create(CultureInfo.InvariantCulture, $"{game.Attempts.Count} tries");
        var hints = _game.HintsUsed == 0 ? "no hints" : Format.Count(_game.HintsUsed, "hint");
        PanelLine[] lines = game.IsSolved
            ? [new($"Cracked in {tries}, {hints}", Palette.Text)]
            : [new("The lock holds.", Palette.Text), new($"The code was {string.Join(' ', game.Secret)}", Palette.Accent)];
        OverlayPanel.Draw(batch, viewport, _context.Fonts, game.IsSolved ? "Unlocked!" : "Out of tries", lines, _menu);
    }
}
