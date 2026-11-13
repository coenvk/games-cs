using CheeseFarm;
using Games.Input;
using Games.UI;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Milking a cow by hand in the morning: left and right (keys, or a tap on either half) squeeze her teats in turn,
/// filling the pail in a steady rhythm; the same teat twice or a hurry makes her restless, her tail swishing, until
/// she kicks the pail. What is in the pail at the end goes into the tank.
/// </summary>
internal sealed class MilkingScene : Scene
{
    private const float SqueezeShow = 0.15f;

    private readonly GameContext _context;
    private readonly KeyedButtons _buttons = new();
    private readonly Milking _milking = new();
    private readonly string _cow;
    private readonly float[] _squeezed = [-1, -1];
    private float _time;
    private int? _poured;
    private Rectangle _area;

    public MilkingScene(GameContext context)
    {
        _context = context;
        var cows = Farm.Herd.Where(Farm.IsGrown).ToList();
        _cow = cows.Count == 0 ? "" : cows[Farm.Day % cows.Count].Name;
        _buttons.Set([new Button(T("Back to the farm"), Leave)]);
    }

    private Farm Farm => _context.Farm.Farm;

    public override IEnumerable<string> DebugInfo => [$"{_milking.Time:0.0} s, {_milking.Litres:0.0} L, restless {_milking.Restless:0.00}"];

    private void Leave() => Scenes.Pop(Navigation.Fade());

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var seconds = (float)elapsed.TotalSeconds;
        _time += seconds;
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Finish();
            Leave();
            return;
        }

        if (_milking.IsDone)
        {
            Finish();
            _buttons.Update(input);
            if (input.WasPressed(Microsoft.Xna.Framework.Input.Keys.Enter))
            {
                Leave();
            }

            return;
        }

        _milking.Advance(seconds);
        if (_context.Actions.WasPressed(input, GameAction.Left))
        {
            Squeeze(Teat.Left);
        }
        else if (_context.Actions.WasPressed(input, GameAction.Right))
        {
            Squeeze(Teat.Right);
        }
        else if (input.Tap is { } tap && _area.Contains(tap))
        {
            Squeeze(tap.X < _area.Center.X ? Teat.Left : Teat.Right);
        }
    }

    private void Squeeze(Teat teat)
    {
        var good = _milking.Squeeze(teat);
        _squeezed[(int)teat] = _time;
        _context.Sounds.Play(_milking.Kicked ? Sound.Splash : good ? Sound.Click : Sound.Error);
    }

    /// <summary>Once over, the pail goes into the tank.</summary>
    private void Finish()
    {
        if (_poured is null)
        {
            _poured = Farm.MilkByHand(_milking);
            _context.Farm.Save();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(14);
        batch.FillRectangle(viewport, new Color(196, 170, 120));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        _area = new Rectangle(viewport.X, viewport.Y + fonts.Px(70), viewport.Width, viewport.Height - fonts.Px(70) - buttonsHeight);
        var unit = Math.Min(_area.Width / 12f, _area.Height / 8f);

        // The barn's floor and wall, and the cow seen from the side, her udder over the pail.
        batch.FillRectangle(new Rectangle(viewport.X, _area.Center.Y + (int)(unit * 2.6f), viewport.Width, viewport.Bottom), new Color(150, 120, 80));
        var body = new Vector2(_area.Center.X, _area.Center.Y - (unit * 0.8f));
        var legs = new Color(40, 40, 40);
        foreach (var leg in new[] { -2.6f, -1.8f, 1.8f, 2.6f })
        {
            batch.FillRectangle(new Rectangle((int)(body.X + (leg * unit)), (int)(body.Y + unit), (int)(unit * 0.45f), (int)(unit * 2.6f)), legs);
        }

        batch.FillEllipse(body, new Vector2(unit * 3.6f, unit * 1.7f), new Color(246, 244, 238));
        batch.FillEllipse(body + new Vector2(-unit * 1.4f, -unit * 0.4f), new Vector2(unit * 1.2f, unit * 0.9f), new Color(36, 36, 40));
        batch.FillEllipse(body + new Vector2(unit * 1.5f, unit * 0.2f), new Vector2(unit * 0.9f, unit * 0.7f), new Color(36, 36, 40));
        var head = body + new Vector2(unit * 3.9f, -unit * 0.9f);
        batch.FillEllipse(head, new Vector2(unit * 1.0f, unit * 0.8f), new Color(246, 244, 238));
        batch.FillEllipse(head + new Vector2(unit * 0.6f, unit * 0.3f), new Vector2(unit * 0.45f, unit * 0.35f), new Color(232, 170, 170));
        batch.FillCircle(head + new Vector2(0, -unit * 0.2f), unit * 0.1f, new Color(30, 30, 30));

        // The tail swishes faster the more restless she is.
        var tailRoot = body + new Vector2(-unit * 3.5f, -unit * 0.8f);
        var swish = MathF.Sin(_time * (2 + (12 * _milking.Restless))) * unit * (0.3f + (1.2f * _milking.Restless));
        batch.DrawLine(tailRoot, tailRoot + new Vector2(-unit * 0.4f + swish, unit * 2.2f), new Color(40, 40, 40), Math.Max(2, unit * 0.12f));

        // The udder and two teats, each stretching when squeezed, a squirt of milk into the pail.
        var udder = body + new Vector2(unit * 0.6f, unit * 1.5f);
        batch.FillEllipse(udder, new Vector2(unit * 1.0f, unit * 0.6f), new Color(236, 176, 176));
        var pail = new Rectangle((int)(udder.X - (unit * 1.0f)), (int)(body.Y + (unit * 3.6f)) - (int)(unit * 1.6f) + (int)unit, (int)(unit * 2.0f), (int)(unit * 1.6f));
        for (var i = 0; i < 2; i++)
        {
            var squeezing = _time - _squeezed[i] < SqueezeShow;
            var top = udder + new Vector2((i == 0 ? -0.4f : 0.4f) * unit, unit * 0.3f);
            var length = unit * (squeezing ? 0.75f : 0.55f);
            batch.FillRectangle(new Rectangle((int)(top.X - (unit * 0.12f)), (int)top.Y, (int)(unit * 0.24f), (int)length), new Color(226, 150, 150));
            if (squeezing && !_milking.Kicked)
            {
                batch.DrawLine(top + new Vector2(0, length), new Vector2(top.X, pail.Y + (unit * 0.2f)), Color.White * 0.9f, Math.Max(2, unit * 0.08f));
            }
        }

        // The pail, tipped over once kicked.
        var steel = new Color(170, 176, 184);
        if (_milking.Kicked)
        {
            var lying = new Rectangle(pail.X + (int)(unit * 0.6f), pail.Bottom - (int)(unit * 1.1f), (int)(unit * 1.6f), (int)(unit * 1.1f));
            batch.FillRectangle(lying, steel);
            batch.FillEllipse(new Vector2(lying.Right + (unit * 1.0f), lying.Bottom - (unit * 0.1f)), new Vector2(unit * 1.4f, unit * 0.2f), Color.White * 0.85f);
        }
        else
        {
            batch.FillRectangle(pail, steel);
            var fill = (int)(pail.Height * 0.85f * _milking.Litres / Milking.Pail);
            batch.FillRectangle(new Rectangle(pail.X + 3, pail.Bottom - 3 - fill, pail.Width - 6, fill), new Color(252, 250, 240));
        }

        // The top: what to do, the time left, the litres, how restless she is.
        var hud = new Rectangle(viewport.X + margin, viewport.Y + margin, viewport.Width - (2 * margin), fonts.Px(56));
        var title = _milking.IsDone
            ? _milking.Kicked
                ? _poured == 1 ? T("{0} kicked the pail over: 1 litre saved", _cow) : T("{0} kicked the pail over: {1} litres saved", _cow, _poured ?? 0)
                : _poured == 1 ? T("1 litre of milk from {0}, into the tank", _cow) : T("{0} litres of milk from {1}, into the tank", _poured ?? 0, _cow)
            : T("Milking {0}: left, right, left, right, steady", _cow);
        batch.DrawText(fonts.Body, title, new Rectangle(hud.X, hud.Y, hud.Width, hud.Height / 2), Palette.Ink, Align.Center, shadow: false);
        var bar = new Rectangle(hud.X + (hud.Width / 4), hud.Bottom - fonts.Px(14), hud.Width / 2, fonts.Px(10));
        batch.FillRectangle(bar, new Color(0, 0, 0, 60));
        batch.FillRectangle(new Rectangle(bar.X, bar.Y, (int)(bar.Width * (1 - (_milking.Time / Milking.Length))), bar.Height), new Color(80, 130, 60));
        var calm = new Rectangle(bar.X, bar.Bottom + fonts.Px(6), (int)(bar.Width * _milking.Restless), fonts.Px(6));
        batch.FillRectangle(calm, Color.Lerp(new Color(230, 190, 60), new Color(200, 60, 40), _milking.Restless));
        if (!_milking.IsDone)
        {
            batch.DrawText(fonts.Small, T("{0:0.0} litres", _milking.Litres), new Vector2(pail.Center.X, pail.Bottom + (unit * 0.2f)), Palette.Ink, Align.Center, shadow: false);
        }

        if (_milking.IsDone)
        {
            var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
            _buttons.Draw(batch, fonts.Small, buttons);
        }
    }
}
