using System.Globalization;
using CodeLock;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

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
            new MenuItem(T("Easy: 3 digits"), () => Start(LockRules.Easy)),
            new MenuItem(T("Normal: 4 digits"), () => Start(LockRules.Normal)),
            new MenuItem(T("Hard: 5, may repeat"), () => Start(LockRules.Hard)),
            new MenuItem(T("Back"), () => Scenes.Pop()));
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

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Code lock"), Lines(), _menu);

    /// <summary>The rules, then each difficulty's best so far (only the ones played).</summary>
    private PanelLine[] Lines()
    {
        var lines = new List<PanelLine> { new(T("Crack the code in ten tries."), Palette.Muted) };
        foreach (var (name, rules) in new[] { (T("Easy"), LockRules.Easy), (T("Normal"), LockRules.Normal), (T("Hard"), LockRules.Hard) })
        {
            var record = _context.Locks.Records[CodeLockScene.Key(rules)];
            if (record.Played > 0)
            {
                var won = T("{0} of {1} won", record.Won, record.Played);
                lines.Add(new PanelLine(record.BestTries is { } tries ? T("{0}: best {1}, {2}", name, Tries(tries), won) : T("{0}: {1}", name, won), Palette.Text));
            }
        }

        return [.. lines];
    }

    private static string Tries(int count) => N(count, "{0} try", "{0} tries");

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
        _name = T("{0} lock #{1}", game.Name, game.Seed);
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("New lock"), () =>
            {
                Scenes.Pop();
                game.NewLock();
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
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Paused"), [new PanelLine(_name, Palette.Muted)], _menu);
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
            new MenuItem(T("New lock"), () =>
            {
                Scenes.Pop();
                game.NewLock();
            }),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input) => _menu.Update(input, Controls.Ui, elapsed);

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var game = _game.Game;
        var tries = N(game.Attempts.Count, "{0} try", "{0} tries");
        var hints = _game.HintsUsed == 0 ? T("no hints") : N(_game.HintsUsed, "{0} hint", "{0} hints");
        var lines = game.IsSolved
            ? new List<PanelLine> { new(T("Cracked in {0}, {1}", tries, hints), Palette.Text) }
            : [new(T("The lock holds."), Palette.Text), new(T("The code was {0}", string.Join(' ', game.Secret)), Palette.Accent)];
        var record = _context.Locks.Records[_game.Difficulty];
        if (_game.Result is { NewBest: true } && record.Won > 1)
        {
            lines.Add(new PanelLine(T("A new best!"), Palette.Accent));
        }
        else if (record.BestTries is { } best && game.IsSolved)
        {
            lines.Add(new PanelLine(N(best, "Best: {0} try", "Best: {0} tries"), Palette.Muted));
        }

        if (record.Streak > 1)
        {
            var streak = N(record.Streak, "{0} win in a row", "{0} wins in a row");
            lines.Add(new PanelLine(_game.Result is { NewBestStreak: true } ? T("{0}, your longest yet", streak) : streak, Palette.Accent));
        }
        OverlayPanel.Draw(batch, viewport, _context.Fonts, game.IsSolved ? T("Unlocked!") : T("Out of tries"), lines, _menu);
    }
}
