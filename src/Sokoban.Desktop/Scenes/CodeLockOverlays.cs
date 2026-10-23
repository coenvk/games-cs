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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Code lock", Lines(), _menu);

    /// <summary>The rules, then each difficulty's best so far (only the ones played).</summary>
    private PanelLine[] Lines()
    {
        var lines = new List<PanelLine> { new("Crack the code in ten tries.", Palette.Muted) };
        foreach (var (name, rules) in new[] { ("Easy", LockRules.Easy), ("Normal", LockRules.Normal), ("Hard", LockRules.Hard) })
        {
            var record = _context.Locks.Records[CodeLockScene.Key(rules)];
            if (record.Played > 0)
            {
                var best = record.BestTries is { } tries ? $"best {Tries(tries)}, " : "";
                lines.Add(new PanelLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {best}{record.Won} of {record.Played} won"), Palette.Text));
            }
        }

        return [.. lines];
    }

    private static string Tries(int count) => count == 1 ? "1 try" : string.Create(CultureInfo.InvariantCulture, $"{count} tries");

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
        var lines = game.IsSolved
            ? new List<PanelLine> { new($"Cracked in {tries}, {hints}", Palette.Text) }
            : [new("The lock holds.", Palette.Text), new($"The code was {string.Join(' ', game.Secret)}", Palette.Accent)];
        var record = _context.Locks.Records[_game.Difficulty];
        if (_game.Result is { NewBest: true } && record.Won > 1)
        {
            lines.Add(new PanelLine("A new best!", Palette.Accent));
        }
        else if (record.BestTries is { } best && game.IsSolved)
        {
            lines.Add(new PanelLine(best == 1 ? "Best: 1 try" : string.Create(CultureInfo.InvariantCulture, $"Best: {best} tries"), Palette.Muted));
        }

        if (record.Streak > 1)
        {
            var streak = string.Create(CultureInfo.InvariantCulture, $"{record.Streak} wins in a row");
            lines.Add(new PanelLine(_game.Result is { NewBestStreak: true } ? $"{streak}, your longest yet" : streak, Palette.Accent));
        }
        OverlayPanel.Draw(batch, viewport, _context.Fonts, game.IsSolved ? "Unlocked!" : "Out of tries", lines, _menu);
    }
}
