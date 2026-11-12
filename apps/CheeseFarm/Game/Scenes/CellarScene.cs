using System.Globalization;
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
/// The ripening cellar: the racks, four wheels to a rack, each wheel's rind deepening as it ripens. A wheel that wants
/// turning shows a red mark; tap it (or choose it and press Enter) to turn it. The line below tells the chosen wheel's
/// ripeness, stars and age, and the best price the shop's customers would give for it today.
/// </summary>
internal sealed class CellarScene : Scene
{
    private static readonly TimeSpan TurnTime = TimeSpan.FromMilliseconds(400);

    private readonly GameContext _context;
    private readonly KeyedButtons _buttons = new();
    private readonly Dictionary<Wheel, TimeSpan> _turning = [];
    private readonly List<(Wheel Wheel, Vector2 At, float Radius)> _spots = [];
    private int _chosen;

    public CellarScene(GameContext context, Wheel? chosen = null)
    {
        _context = context;
        _chosen = chosen is null ? 0 : Math.Max(0, Farm.Wheels.ToList().IndexOf(chosen));
        SetButtons();
    }

    private Farm Farm => _context.Farm.Farm;

    private Wheel? Chosen => _chosen >= 0 && _chosen < Farm.Wheels.Count ? Farm.Wheels[_chosen] : null;

    public override IEnumerable<string> DebugInfo => [Farm.ToString(), string.Create(CultureInfo.InvariantCulture, $"{Farm.Due} due, chosen {_chosen}")];

    protected override void OnEnter() => _context.PlayMood(Music.Cellar);

    private void SetButtons()
    {
        List<Button> buttons = [];
        if (Chosen is { } wheel)
        {
            buttons.Add(new Button(T("Turn"), () => Turn(wheel), Highlight: wheel.NeedsTurning(Farm.Day)));
            if (Farm.BestOffer(wheel) is { } offer)
            {
                buttons.Add(new Button(T("Sell for €{0}", offer.Price), () => Sell(wheel, offer.Customer)));
            }
        }

        buttons.Add(new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())));
        _buttons.Set([.. buttons]);
    }

    private void Turn(Wheel wheel)
    {
        Farm.Turn(wheel);
        _context.Farm.Save();
        _turning[wheel] = TurnTime;
        _context.Sounds.Play(Sound.Card);
        SetButtons();
    }

    private void Sell(Wheel wheel, int customer)
    {
        if (Farm.Sell(customer, wheel) > 0)
        {
            _context.Farm.Save();
            _context.Sounds.Play(Sound.Chips);
            _chosen = Math.Min(_chosen, Farm.Wheels.Count - 1);
            SetButtons();
        }
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        foreach (var wheel in _turning.Keys.ToList())
        {
            _turning[wheel] -= elapsed;
            if (_turning[wheel] <= TimeSpan.Zero)
            {
                _turning.Remove(wheel);
            }
        }

        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        var count = Farm.Wheels.Count;
        if (count > 0)
        {
            var before = _chosen;
            if (_context.Actions.WasPressed(input, GameAction.Right))
            {
                _chosen = (_chosen + 1) % count;
            }
            else if (_context.Actions.WasPressed(input, GameAction.Left))
            {
                _chosen = (_chosen + count - 1) % count;
            }
            else if (_context.Actions.WasPressed(input, GameAction.Down))
            {
                _chosen = Math.Min(count - 1, _chosen + Farm.WheelsPerRack);
            }
            else if (_context.Actions.WasPressed(input, GameAction.Up))
            {
                _chosen = Math.Max(0, _chosen - Farm.WheelsPerRack);
            }
            else if (_context.Actions.WasPressed(input, GameAction.Confirm) && Chosen is { } chosen)
            {
                Turn(chosen);
            }
            else if (input.Tap is { } tap && _spots.FirstOrDefault(s => Vector2.Distance(s.At, tap) <= s.Radius) is { Wheel: not null } spot)
            {
                var index = Farm.Wheels.ToList().IndexOf(spot.Wheel);
                if (index == _chosen)
                {
                    Turn(spot.Wheel);
                }

                _chosen = index;
            }

            if (before != _chosen)
            {
                SetButtons();
            }
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(14);
        batch.FillRectangle(viewport, new Color(46, 40, 38));
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(60));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var info = new Rectangle(viewport.X, buttons.Y - (int)fonts.Small.LineHeight - margin, viewport.Width, (int)fonts.Small.LineHeight);
        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - (2 * margin), info.Y - hud.Bottom - (2 * margin));

        DrawRacks(batch, area);

        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, T("Cellar"), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, T("{0} of {1}", Farm.Wheels.Count, Farm.Room), text, Palette.Accent, Align.Right);
        batch.DrawText(fonts.Small, Farm.Due > 0 ? T("{0} to turn today", Farm.Due) : T("All turned"), text, Farm.Due > 0 ? new Color(240, 120, 100) : Palette.Muted, Align.Center, shadow: false);

        var line = Chosen is { } wheel
            ? T("{0}, {1}  ·  {2}  ·  {3} days old", Cheese.Name(wheel.Ripeness(Farm.Day)), Cheese.Name(wheel.Flavour), Cheese.Stars(wheel.Stars), wheel.Age(Farm.Day))
                + (wheel.Ripeness(Farm.Day) < Ripeness.VeryOld ? "  ·  " + T("{0} in {1} days", Cheese.Name(wheel.Ripeness(Farm.Day) + 1), Ripening.StartOf(wheel.Ripeness(Farm.Day) + 1) - wheel.Age(Farm.Day)) : "")
            : T("The cellar is empty: make a cheese in the kitchen");
        batch.DrawText(fonts.Small, line, info, Palette.Text, Align.Center);
        _buttons.Draw(batch, fonts.Small, buttons);
    }

    /// <summary>The racks in one or two columns, a plank under each, the wheels on them.</summary>
    private void DrawRacks(SpriteBatch batch, Rectangle area)
    {
        _spots.Clear();
        var columns = Farm.Racks > 4 ? 2 : 1;
        var rows = (Farm.Racks + columns - 1) / columns;
        var rackWidth = area.Width / columns;
        var rowHeight = area.Height / Math.Max(1, rows);
        var radius = MathF.Min(MathF.Min(rowHeight * 0.38f, rackWidth / (Farm.WheelsPerRack * 2.6f)), _context.Fonts.Px(80));
        var wheels = Farm.Wheels;
        for (var rack = 0; rack < Farm.Racks; rack++)
        {
            var (column, row) = (rack / rows, rack % rows);
            var x = area.X + (column * rackWidth);
            var plank = area.Y + ((row + 1) * rowHeight) - (rowHeight * 0.12f);
            batch.FillRectangle(new Rectangle(x + (int)(rackWidth * 0.04f), (int)plank, (int)(rackWidth * 0.92f), Math.Max(4, (int)(rowHeight * 0.06f))), new Color(120, 86, 56));
            for (var slot = 0; slot < Farm.WheelsPerRack; slot++)
            {
                var index = (rack * Farm.WheelsPerRack) + slot;
                var at = new Vector2(x + (rackWidth * (slot + 0.5f) / Farm.WheelsPerRack), plank - radius - 2);
                if (index >= wheels.Count)
                {
                    batch.FillCircle(at, radius * 0.9f, new Color(58, 50, 46));
                    continue;
                }

                var wheel = wheels[index];
                _spots.Add((wheel, at, radius));
                if (index == _chosen)
                {
                    batch.FillCircle(at, radius * 1.12f, Palette.Accent);
                }

                // Turning: the wheel narrows to its edge and widens again, showing its other face.
                var squash = _turning.TryGetValue(wheel, out var left) ? MathF.Abs(MathF.Cos((float)(1 - (left / TurnTime)) * MathF.PI)) : 1;
                if (squash < 1)
                {
                    batch.FillEllipse(at, new Vector2(radius * MathF.Max(0.12f, squash), radius), Cheese.Rind(wheel.Ripeness(_context.Farm.Farm.Day)));
                }
                else
                {
                    FarmScene.DrawWheel(batch, at, radius, wheel.Ripeness(Farm.Day), wheel.Flavour);
                }

                if (wheel.NeedsTurning(Farm.Day))
                {
                    var mark = at + new Vector2(radius * 0.7f, -radius * 0.7f);
                    batch.FillCircle(mark, radius * 0.26f, new Color(220, 70, 60));
                    batch.DrawText(_context.Fonts.Small, "!", new Rectangle((int)(mark.X - radius), (int)(mark.Y - (radius * 0.3f)), (int)(radius * 2), (int)(radius * 0.6f)), Color.White, Align.Center, shadow: false);
                }
            }
        }
    }
}
