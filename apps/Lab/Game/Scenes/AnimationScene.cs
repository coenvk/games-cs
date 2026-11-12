using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;

namespace Games.Scenes;

/// <summary>
/// Jentl's animation pieces at work. On the left a figure whose poses are animation states (idle, walk, jump, hurt)
/// with footsteps as frame events, stretching as it jumps, squashing as it lands, flashing and shaking when hit. On
/// the right cards dealt by a sequence of tweens with overshoot, and a badge pulsing in a yoyo loop.
/// </summary>
internal sealed class AnimationScene : Scene
{
    private const float Ground = 0;

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(120);

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly AnimationStates<Pose> _states;
    private readonly Flash _flash = new();
    private readonly SquashStretch _squash = new();
    private readonly Shake _shake = new();
    private readonly Motions _motions = new();
    private readonly MotionPlayer _pulse = new();
    private readonly Vector2[] _cards = new Vector2[5];
    private readonly float[] _turns = new float[5];
    private float _x = 0.2f;
    private float _direction = 1;
    private float _height;
    private float _rise;
    private bool _hit;
    private float _badge = 1;
    private int _steps;

    public AnimationScene(GameContext context)
    {
        _context = context;
        _buttons = new ButtonBar(new Button("Jump", Jump, Highlight: true), new Button("Hit", Hit), new Button("Deal", Deal), new Button("Back", () => Scenes.Pop()));
        _states = new AnimationStates<Pose>(Pose.Walk)
            .Add(Pose.Idle, new Animation(TimeSpan.FromMilliseconds(400), AnimationMode.PingPong, 0, 1))
            .Add(Pose.Walk, new Animation(Step, AnimationMode.Loop, 2, 3, 4, 5) { Events = new Dictionary<int, string> { [0] = "step", [2] = "step" } })
            .Add(Pose.Jump, new Animation(Step, AnimationMode.Once, 6, 7))
            .Add(Pose.Hurt, new Animation(TimeSpan.FromMilliseconds(90), AnimationMode.Once, 8, 9, 8, 9, 8))
            .WhenAny(Pose.Hurt, () => _hit)
            .After(Pose.Hurt, Pose.Walk)
            .When(Pose.Walk, Pose.Jump, () => _height > Ground)
            .When(Pose.Jump, Pose.Walk, () => _height <= Ground && _rise <= 0);
        _states.Animator.FrameEvent += name =>
        {
            if (name == "step")
            {
                _steps++;
            }
        };
        _pulse.Play(Motion.Repeat(Motion.Tween(s => _badge = s, 1, 1.15f, 0.6, Easing.SineInOut), yoyo: true));
        Deal();
    }

    private enum Pose
    {
        Idle,
        Walk,
        Jump,
        Hurt,
    }

    public override IEnumerable<string> DebugInfo => [$"Pose {_states.State}, frame {_states.Frame}, {_steps} {(_steps == 1 ? "step" : "steps")}"];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Up) || _context.Actions.WasPressed(input, GameAction.Confirm))
        {
            Jump();
        }

        var seconds = (float)elapsed.TotalSeconds;
        if (_states.State != Pose.Hurt)
        {
            _x += _direction * seconds * 0.18f;
            if (_x is > 0.85f or < 0.15f)
            {
                _direction = -_direction;
                _x = Math.Clamp(_x, 0.15f, 0.85f);
            }
        }

        if (_height > Ground || _rise > 0)
        {
            _height += _rise * seconds;
            _rise -= 900 * seconds;
            if (_height <= Ground)
            {
                _height = Ground;
                _rise = 0;
                _squash.Trigger(0.35f);
            }
        }

        _states.Update(elapsed);
        _hit = false;
        _flash.Update(elapsed);
        _squash.Update(elapsed);
        _shake.Update(elapsed);
        _motions.Update(elapsed);
        _pulse.Update(elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var buttons = ButtonBar.Height(fonts.Small) + fonts.Px(20);
        var area = new Rectangle(viewport.X, viewport.Y, viewport.Width, viewport.Height - buttons);
        _buttons.Draw(batch, fonts.Small, new Rectangle(viewport.X, area.Bottom, viewport.Width, buttons));

        var left = new Rectangle(area.X, area.Y, area.Width / 2, area.Height);
        var right = new Rectangle(area.Center.X, area.Y, area.Width / 2, area.Height);
        batch.DrawText(fonts.Small, "States, frame events, squash, flash, shake", new Vector2(left.Center.X, left.Y + fonts.Px(16)), Palette.Muted, Align.Center);
        batch.DrawText(fonts.Small, "Tween sequences and loops", new Vector2(right.Center.X, right.Y + fonts.Px(16)), Palette.Muted, Align.Center);
        DrawFigure(batch, left);
        DrawCards(batch, right);
    }

    private void Jump()
    {
        if (_height <= Ground && _states.State != Pose.Hurt)
        {
            _rise = 520;
            _height = 0.01f;
            _squash.Trigger(-0.3f, TimeSpan.FromMilliseconds(300));
        }
    }

    private void Hit()
    {
        _hit = true;
        _flash.Trigger(TimeSpan.FromMilliseconds(250), Color.White);
        _shake.Trigger(14, TimeSpan.FromMilliseconds(400));
    }

    /// <summary>The five cards fly in one after another from below, overshooting and settling, fanned out.</summary>
    private void Deal()
    {
        _motions.Clear();
        List<Motion> deals = [];
        for (var i = 0; i < _cards.Length; i++)
        {
            var index = i;
            var target = new Vector2(-0.3f + (i * 0.15f), 0);
            _cards[i] = new Vector2(0, 1.4f);
            _turns[i] = 0;
            deals.Add(Motion.Together(
                Motion.Tween(v => _cards[index] = v, new Vector2(0, 1.4f), target, 0.45, Easing.BackOut),
                Motion.Tween(r => _turns[index] = r, 0.8f, (index - 2) * 0.12f, 0.45, Easing.CubicOut)));
        }

        _motions.Play(Motion.Sequence([.. deals.SelectMany(d => new[] { d, Motion.Wait(0.05) })]));
    }

    private void DrawFigure(SpriteBatch batch, Rectangle area)
    {
        var floor = area.Y + (area.Height * 0.72f);
        batch.FillRectangle(new Rectangle(area.X + 20, (int)floor, area.Width - 40, 4), Palette.PanelLight);
        var scale = _squash.Scale;
        var size = new Vector2(60 * scale.X, 110 * scale.Y);
        var feet = new Vector2(area.X + (area.Width * _x), floor - _height) + _shake.Offset;
        var body = new Rectangle((int)(feet.X - (size.X / 2)), (int)(feet.Y - size.Y), (int)size.X, (int)size.Y);
        var colour = _flash.Tint(_states.State == Pose.Hurt ? new Color(230, 90, 80) : new Color(90, 160, 240));

        // Legs by frame: the walk's four frames stride, the jump tucks them up, idle stands.
        var frame = _states.Frame;
        var stride = frame switch { 2 => 14, 3 => 4, 4 => -14, 5 => -4, 6 or 7 => 0, _ => 0 } * scale.X;
        var tuck = frame is 6 or 7 ? 16 : 0;
        batch.DrawLine(new Vector2(feet.X - 8, body.Bottom - 20), new Vector2(feet.X - 8 + stride, body.Bottom + 18 - tuck), colour, 8);
        batch.DrawLine(new Vector2(feet.X + 8, body.Bottom - 20), new Vector2(feet.X + 8 - stride, body.Bottom + 18 - tuck), colour, 8);
        batch.FillRoundedRectangle(new Rectangle(body.X, body.Y, body.Width, body.Height - 18), 16, colour);
        var eye = new Vector2(feet.X + (_direction * size.X * 0.2f), body.Y + (size.Y * 0.25f));
        batch.FillCircle(eye, frame == 1 ? 2 : 5, Palette.Ink);
        batch.DrawText(_context.Fonts.Small, $"{_states.State} · {_steps} {(_steps == 1 ? "step" : "steps")}", new Vector2(area.Center.X, floor + 30), Palette.Text, Align.Center);
    }

    private void DrawCards(SpriteBatch batch, Rectangle area)
    {
        var centre = new Vector2(area.Center.X, area.Y + (area.Height * 0.55f));
        var size = new Vector2(area.Width * 0.16f, area.Width * 0.23f);
        for (var i = 0; i < _cards.Length; i++)
        {
            var at = centre + (_cards[i] * new Vector2(area.Width, area.Height / 2f));
            batch.FillRotated(at + new Vector2(3, 5), size, _turns[i], Palette.Shadow);
            batch.FillRotated(at, size, _turns[i], new Color(250, 246, 236));
            batch.FillRotated(at, size * 0.8f, _turns[i], i % 2 == 0 ? new Color(200, 60, 60) : new Color(40, 50, 70));
        }

        var badge = new Vector2(area.Center.X, area.Y + (area.Height * 0.2f));
        var radius = area.Width * 0.07f * _badge;
        batch.FillCircle(badge, radius, Palette.Accent);
        batch.DrawText(_context.Fonts.Small, "New!", new Rectangle((int)(badge.X - radius), (int)(badge.Y - radius), (int)(radius * 2), (int)(radius * 2)), Palette.Ink, Align.Center, shadow: false);
    }
}
