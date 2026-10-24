using System.Globalization;
using CodeLock;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Jentl.Core.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Code lock, Mastermind with digits: a padlock on top, the tries so far with their lights (green for a
/// digit in the right place, amber for a right digit in the wrong place), and the dial wheels to set the
/// next try. Tap the arrows or swipe a wheel to turn it; on a keyboard type digits, use the arrows, and
/// Enter to try.
/// </summary>
internal sealed class CodeLockScene : Scene
{
    private const int HudHeight = 64;
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OpenTime = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EndDelay = TimeSpan.FromMilliseconds(900);
    private static readonly Color Exact = new(86, 196, 110);
    private static readonly Color Misplaced = new(240, 176, 48);
    private static readonly Color Unlit = new(58, 64, 84);
    private static readonly Keys[] DigitKeys = [Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9];
    private static readonly Keys[] PadKeys = [Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9];

    private readonly GameContext _context;
    private readonly int[] _wheels;
    private readonly Rectangle[] _up;
    private readonly Rectangle[] _down;
    private readonly Rectangle[] _faces;
    private readonly ButtonBar _buttons;
    private readonly ActionRepeater<GameAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(110),
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right);

    private int _selected;
    private string? _message;
    private TimeSpan _messageFor;
    private TimeSpan _endIn;
    private bool _endShown;
    private Tween _shackle = Tween.At(Vector2.Zero);

    public CodeLockScene(GameContext context, LockRules rules, int? seed = null)
    {
        _context = context;
        Seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);
        Game = LockGame.Create(rules, Seed);
        _wheels = [.. Enumerable.Range(0, rules.Length).Select(i => rules.Repeats ? 0 : i % rules.Digits)];
        _up = new Rectangle[rules.Length];
        _down = new Rectangle[rules.Length];
        _faces = new Rectangle[rules.Length];
        _buttons = new ButtonBar(
            new Button("Try", TryCode, Highlight: true),
            new Button("Hint", ShowHint),
            new Button("Menu", () => Scenes.Push(new CodeLockPauseScene(context, this))));
    }

    public LockGame Game { get; }

    public int Seed { get; }

    public int HintsUsed { get; private set; }

    public string Name => Game.Rules == LockRules.Easy ? "Easy" : Game.Rules == LockRules.Hard ? "Hard" : "Normal";

    /// <summary>Which record this lock counts for.</summary>
    public string Difficulty => Key(Game.Rules);

    /// <summary>What finishing this lock did to the records; set once the game is over.</summary>
    public LockResult? Result { get; private set; }

    public static string Key(LockRules rules) => rules == LockRules.Easy ? "easy" : rules == LockRules.Hard ? "hard" : "normal";

    public void NewLock() => Scenes.Replace(new CodeLockScene(_context, Game.Rules), Navigation.Fade());

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Puzzle);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        _shackle.Update(elapsed);
        _messageFor -= elapsed;
        if (Game.IsOver)
        {
            _endIn -= elapsed;
            if (!_endShown && _endIn <= TimeSpan.Zero)
            {
                _endShown = true;
                Scenes.Push(new CodeLockEndScene(_context, this));
            }

            return;
        }

        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new CodeLockPauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        for (var d = 0; d < 10; d++)
        {
            if (input.WasPressed(DigitKeys[d]) || input.WasPressed(PadKeys[d]))
            {
                if (d < Game.Rules.Digits)
                {
                    _wheels[_selected] = d;
                    _selected = Math.Min(_selected + 1, _wheels.Length - 1);
                }

                return;
            }
        }

        switch (_repeater.Update(input, actions, elapsed))
        {
            case GameAction.Up:
                Turn(_selected, 1);
                return;
            case GameAction.Down:
                Turn(_selected, -1);
                return;
            case GameAction.Left:
                _selected = Math.Max(0, _selected - 1);
                return;
            case GameAction.Right:
                _selected = Math.Min(_wheels.Length - 1, _selected + 1);
                return;
        }

        if (actions.WasPressed(input, GameAction.Undo))
        {
            _selected = Math.Max(0, _selected - 1);
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            TryCode();
        }
        else if (actions.WasPressed(input, GameAction.Hint))
        {
            ShowHint();
        }
        else if (input.Tap is { } tap)
        {
            for (var i = 0; i < _wheels.Length; i++)
            {
                if (_up[i].Contains(tap))
                {
                    Turn(i, 1);
                }
                else if (_down[i].Contains(tap))
                {
                    Turn(i, -1);
                }
                else if (_faces[i].Contains(tap))
                {
                    _selected = i;
                }
            }
        }
        else if (input.Swipe is { } swipe and (SwipeDirection.Up or SwipeDirection.Down))
        {
            var start = input.Gestures.PressedAt;
            for (var i = 0; i < _wheels.Length; i++)
            {
                if (Rectangle.Union(_up[i], _down[i]).Contains(start))
                {
                    Turn(i, swipe == SwipeDirection.Up ? 1 : -1);
                }
            }
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);

        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"Code lock ({Name}) #{Seed}"), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, Game.AttemptsLeft == 1 ? "1 try left" : string.Create(CultureInfo.InvariantCulture, $"{Game.AttemptsLeft} tries left"), text, Game.AttemptsLeft <= 2 ? Misplaced : Palette.Accent, Align.Right);

        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var statusHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var status = new Rectangle(viewport.X, buttons.Top - statusHeight, viewport.Width, statusHeight);

        // The wheels: as wide as fits, up to a comfortable size.
        var count = _wheels.Length;
        var wheel = Math.Min(fonts.Px(88), (viewport.Width - 2 * margin) / (count + (count - 1) / 4 + 1));
        var gap = wheel / 4;
        var arrow = wheel / 2;
        var wheelsHeight = wheel + 2 * arrow;
        var wheelsTop = status.Top - fonts.Px(8) - wheelsHeight;
        var left = viewport.Center.X - (count * wheel + (count - 1) * gap) / 2;
        for (var i = 0; i < count; i++)
        {
            var x = left + i * (wheel + gap);
            _up[i] = new Rectangle(x, wheelsTop, wheel, arrow);
            _faces[i] = new Rectangle(x, wheelsTop + arrow, wheel, wheel);
            _down[i] = new Rectangle(x, wheelsTop + arrow + wheel, wheel, arrow);
            batch.FillTriangle(Arrow(_up[i]), pointsUp: true, Palette.Muted);
            batch.FillTriangle(Arrow(_down[i]), pointsUp: false, Palette.Muted);
            batch.FillRectangle(_faces[i], i == _selected && !Game.IsOver ? Palette.Accent : Palette.PanelLight);
            var face = _faces[i];
            face.Inflate(-fonts.Px(4), -fonts.Px(4));
            batch.FillRectangle(face, Palette.Ink);
            batch.DrawText(fonts.Sized(wheel * 0.6f), Digit(_wheels[i]), _faces[i], Palette.Text);
        }

        // The padlock, then as many past tries as fit between it and the wheels (the latest ones).
        var lockHeight = Math.Min(fonts.Px(110), (wheelsTop - hud.Bottom) / 3);
        DrawPadlock(batch, new Rectangle(viewport.Center.X - lockHeight / 2, hud.Bottom + fonts.Px(10), lockHeight, lockHeight));
        var historyTop = hud.Bottom + fonts.Px(20) + lockHeight;
        // Rows shrink as tries pile up, so all of them fit; only on a very short screen do the oldest scroll away.
        var room = wheelsTop - fonts.Px(12) - historyTop;
        var row = Math.Clamp(room / Math.Max(1, Game.Attempts.Count) - 4, fonts.Px(24), Math.Min(fonts.Px(44), wheel / 2));
        var rows = Math.Max(1, room / (row + 4));
        var shown = Game.Attempts.TakeLast(rows).ToArray();
        for (var r = 0; r < shown.Length; r++)
        {
            DrawAttempt(batch, shown[r], Game.Attempts.Count - shown.Length + r + 1, new Rectangle(viewport.X, historyTop + r * (row + 4), viewport.Width, row));
        }

        var (message, color) = _message is not null && _messageFor > TimeSpan.Zero
            ? (_message, Palette.Accent)
            : Game.Attempts.Count == 0
                ? ("Green: right digit, right place. Amber: right digit, wrong place.", Palette.Muted)
                : (string.Create(CultureInfo.InvariantCulture, $"{Format.Count(Game.PossibleCodes, "code")} still fit"), Palette.Muted);
        batch.DrawText(fonts.Small, message, status, color, shadow: false);
        _buttons.Draw(batch, buttonFont, buttons);
    }

    private static string Digit(int digit) => digit.ToString(CultureInfo.InvariantCulture);

    private void Turn(int wheel, int by)
    {
        if (!Game.IsOver)
        {
            _selected = wheel;
            _wheels[wheel] = (_wheels[wheel] + by + Game.Rules.Digits) % Game.Rules.Digits;
            _context.Sounds.Play(Sound.Click);
        }
    }

    private void TryCode()
    {
        if (Game.IsOver)
        {
            return;
        }

        if (Game.Rules.Problem(_wheels) is { } problem)
        {
            Say(problem);
            _context.Sounds.Play(Sound.Error);
            return;
        }

        if (Game.Attempts.Any(a => a.Code.SequenceEqual(_wheels)))
        {
            Say("You tried that code already");
            _context.Sounds.Play(Sound.Error);
            return;
        }

        Game.Try(_wheels);
        _selected = 0;
        _context.Sounds.Play(Game.IsSolved ? Sound.Fanfare : Game.IsOver ? Sound.Lose : Sound.Navigate);
        if (Game.IsSolved)
        {
            _shackle = new Tween(Vector2.Zero, Vector2.One, OpenTime, Easing.CubicOut);
        }

        if (Game.IsOver)
        {
            _endIn = EndDelay;
            Result = _context.Locks.Record(Difficulty, Game.IsSolved, Game.Attempts.Count);
        }
    }

    private void ShowHint()
    {
        if (Game.IsOver)
        {
            return;
        }

        var hint = Game.Hint();
        for (var i = 0; i < _wheels.Length; i++)
        {
            _wheels[i] = hint[i];
        }

        HintsUsed++;
        Say(Game.Attempts.Count == 0
            ? "Any code is as good as another for a first try"
            : string.Create(CultureInfo.InvariantCulture, $"This fits every answer so far ({Format.Count(Game.PossibleCodes, "code")} {(Game.PossibleCodes == 1 ? "does" : "do")})"));
    }

    private void Say(string message)
    {
        _message = message;
        _messageFor = MessageDuration;
    }

    /// <summary>A try's digits on the left, its lights on the right: green ones first, then amber, then unlit.</summary>
    private void DrawAttempt(SpriteBatch batch, Attempt attempt, int number, Rectangle row)
    {
        var fonts = _context.Fonts;
        var count = attempt.Code.Count;
        var cell = row.Height;
        var light = cell / 2;
        var width = fonts.Px(40) + count * (cell + 4) + fonts.Px(24) + count * (light + 6);
        var x = row.Center.X - width / 2;
        batch.DrawText(fonts.Small, Digit(number), new Rectangle(x, row.Y, fonts.Px(32), cell), Palette.Muted, Align.Right, shadow: false);
        x += fonts.Px(40);
        var font = fonts.Sized(cell * 0.62f);
        foreach (var digit in attempt.Code)
        {
            var box = new Rectangle(x, row.Y, cell, cell);
            batch.FillRectangle(box, Palette.Panel);
            batch.DrawText(font, Digit(digit), box, Palette.Text, shadow: false);
            x += cell + 4;
        }

        x += fonts.Px(24);
        for (var i = 0; i < count; i++)
        {
            var colour = i < attempt.Feedback.Exact ? Exact : i < attempt.Feedback.Exact + attempt.Feedback.Misplaced ? Misplaced : Unlit;
            batch.FillRectangle(new Rectangle(x, row.Center.Y - light / 2, light, light), colour);
            x += light + 6;
        }
    }

    /// <summary>A padlock of rectangles: the shackle lifts out of the body when the lock is cracked.</summary>
    private void DrawPadlock(SpriteBatch batch, Rectangle area)
    {
        var steel = new Color(170, 178, 196);
        var brass = Game.IsSolved ? Exact : Palette.Accent;
        var thick = Math.Max(3, area.Width / 9);
        var bodyTop = area.Y + area.Height * 45 / 100;
        var body = new Rectangle(area.X, bodyTop, area.Width, area.Bottom - bodyTop);
        var lift = (int)(_shackle.Value.X * area.Height * 0.18f);
        var inset = area.Width / 6;
        var shackleTop = area.Y + thick / 2 - lift;
        var shackle = new Rectangle(area.X + inset, shackleTop, area.Width - 2 * inset, bodyTop - shackleTop + thick);
        batch.FillRectangle(new Rectangle(shackle.X, shackle.Y, shackle.Width, thick), steel);
        batch.FillRectangle(new Rectangle(shackle.X, shackle.Y, thick, shackle.Height), steel);

        // The right leg comes out of the body once open.
        var rightLeg = Game.IsSolved ? shackle.Height - lift - thick : shackle.Height;
        batch.FillRectangle(new Rectangle(shackle.Right - thick, shackle.Y, thick, Math.Max(thick, rightLeg)), steel);
        batch.FillRectangle(body, brass);
        batch.FillRectangle(new Rectangle(body.X, body.Bottom - body.Height / 6, body.Width, body.Height / 6), brass * 0.75f);
        var hole = Math.Max(4, body.Width / 7);
        batch.FillRectangle(new Rectangle(body.Center.X - hole / 2, body.Center.Y - hole, hole, hole * 2), Palette.Ink);
    }

    /// <summary>The arrow inside a wheel's up or down area: three fifths of it, centred.</summary>
    private static Rectangle Arrow(Rectangle area)
    {
        var inner = new Rectangle(0, 0, area.Width * 3 / 5, area.Height * 3 / 5);
        inner.Offset(area.Center.X - inner.Width / 2, area.Center.Y - inner.Height / 2);
        return inner;
    }
}
