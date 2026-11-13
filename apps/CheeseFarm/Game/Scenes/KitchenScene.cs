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
using Microsoft.Xna.Framework.Input;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The cheese kitchen: one wheel made in four steps. Choose a flavour (once the farm has bought one); warm the milk
/// by holding on, into the band; add the rennet and test the curd when it should give a clean break; cut it with the
/// harp as the knife crosses each mark; and choose the days in the brine. The wheel then goes to the cellar.
/// </summary>
internal sealed class KitchenScene : Scene
{
    private const float StartWarmth = 18;
    private const float HeatRate = 3.5f;
    private const float CoolRate = 0.9f;
    private const float KnifeSpeed = 0.55f;
    private static readonly float[] Marks = [0.2f, 0.4f, 0.6f, 0.8f];

    private readonly GameContext _context;
    private readonly KeyedButtons _buttons = new();
    private readonly List<float> _cuts = [];
    private readonly List<float> _misses = [];
    private Making? _making;
    private float _warmth = StartWarmth;
    private float _clock;
    private float _time;
    private bool _held;
    private float _knife;
    private int _knifeWay = 1;
    private int _brine = 2;
    private Wheel? _wheel;
    private string? _note;
    private Rectangle _area;
    private CheeseSize _size;

    public KitchenScene(GameContext context)
    {
        _context = context;
        _size = Farm.CanMakeSize(CheeseSize.Wheel) ? CheeseSize.Wheel : CheeseSize.Baby;
        SetButtons();
    }

    /// <summary>The next size there is milk for, round again from the smallest.</summary>
    private void NextSize()
    {
        var sizes = Enum.GetValues<CheeseSize>().Where(Farm.CanMakeSize).ToList();
        _size = sizes.Count == 0 ? _size : sizes[(sizes.IndexOf(_size) + 1) % sizes.Count];
        _context.Sounds.Play(Sound.Click);
        SetButtons();
    }

    private Farm Farm => _context.Farm.Farm;

    public override IEnumerable<string> DebugInfo =>
    [
        _making is { } m
            ? string.Create(CultureInfo.InvariantCulture, $"{m.Next?.ToString() ?? "Done"}: warmth {_warmth:0.0}, clock {_clock:0.0} (sets {m.SetsAfter:0.0}), knife {_knife:0.00}, made {m.Made:0.00}")
            : "Choosing a flavour",
    ];

    protected override void OnEnter() => _context.PlayMood(Music.Kitchen);

    private void Begin(Flavour flavour)
    {
        _making = Farm.StartMaking(flavour, _size);
        if (_making is null)
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        _note = T("Hold on the kettle to warm the milk to {0} to {1} °C", _making.GoodKettle ? Making.LowWarmth - 1 : Making.LowWarmth, _making.GoodKettle ? Making.HighWarmth + 1 : Making.HighWarmth);
        SetButtons();
    }

    private void SetButtons()
    {
        if (_making is null)
        {
            _buttons.Set(
            [
                new Button(T("Size: {0}, {1} litres", Cheese.Name(_size), Sizes.Milk(_size)), NextSize),
                .. Farm.Flavours.Order().Select(f => new Button(Cheese.Name(f), () => Begin(f), Highlight: f == Flavour.Plain)),
                new Button(T("Back"), Leave),
            ]);
            return;
        }

        if (_wheel is not null)
        {
            _buttons.Set(
            [
                new Button(T("To the cellar"), () => Scenes.Replace(new CellarScene(_context, _wheel), Navigation.Fade()), Highlight: true),
                .. Farm.CanMake ? [new Button(T("Make another"), () => Scenes.Replace(new KitchenScene(_context), Navigation.Fade()))] : Array.Empty<Button>(),
                new Button(T("Back to the farm"), Leave),
            ]);
            return;
        }

        _buttons.Set(_making.Next switch
        {
            Stage.Warm => [new Button(T("Add the rennet"), AddRennet, Highlight: true), new Button(T("Back"), Leave)],
            Stage.Set => [new Button(T("Test the curd"), TestCurd, Highlight: true)],
            Stage.Cut => [new Button(T("Cut"), Cut, Highlight: true)],
            _ =>
            [
                new Button("−", () => ChangeBrine(-1)),
                new Button(_brine == 1 ? T("1 day in the brine") : T("{0} days in the brine", _brine), Brine, Highlight: true),
                new Button("+", () => ChangeBrine(1)),
            ],
        });
    }

    private void Leave() => Scenes.Pop(Navigation.Fade());

    private void AddRennet()
    {
        var score = _making!.Warm(_warmth);
        _clock = 0;
        _note = score >= 1 ? T("Just right: the rennet is in. Test the curd when it has set")
            : _warmth < Making.LowWarmth ? T("A little cold: the rennet works slowly. Test the curd when it has set")
            : T("A little hot: the curd will be tough. Test the curd when it has set");
        _context.Sounds.Play(score >= 1 ? Sound.Pickup : Sound.Click);
        SetButtons();
    }

    private void TestCurd()
    {
        var score = _making!.Set(_clock);
        _note = _clock < _making.SetsAfter - 1 ? T("Too soon: the curd was still soft. Now cut it as the knife crosses each mark")
            : _clock > _making.SetsAfter + 1 ? T("Too late: the curd went firm. Now cut it as the knife crosses each mark")
            : T("A clean break! Now cut it as the knife crosses each mark");
        _context.Sounds.Play(score >= 1 ? Sound.Pickup : Sound.Click);
        _knife = 0;
        _knifeWay = 1;
        SetButtons();
    }

    private void Cut()
    {
        var mark = Marks[_cuts.Count];
        _cuts.Add(_knife);
        _misses.Add(_knife - mark);
        _context.Sounds.Play(MathF.Abs(_knife - mark) < 0.03f ? Sound.Pickup : Sound.Click);
        if (_cuts.Count == Marks.Length)
        {
            var score = _making!.Cut(_misses);
            _note = score > 0.8f ? T("Even cuts: the whey drains well. How long in the brine?")
                : T("Uneven cuts: some curd will be wetter. How long in the brine?");
            SetButtons();
        }
    }

    private void ChangeBrine(int change)
    {
        _brine = Math.Clamp(_brine + change, 1, 6);
        SetButtons();
    }

    private void Brine()
    {
        _making!.Brine(_brine);
        _wheel = Farm.Finish(_making);
        _context.Farm.Save();
        _context.Sounds.Play(Sound.Splash);
        if (_wheel.Stars >= 5)
        {
            _context.Sounds.Play(Sound.Fanfare);
        }

        _note = _brine < Making.BestBrine ? T("A little short in the brine: mild and soft")
            : _brine > Making.BestBrine ? T("A little long in the brine: salty")
            : T("Three days in the brine: just right");
        SetButtons();
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var seconds = (float)elapsed.TotalSeconds;
        _time += seconds;
        if (_context.Actions.WasPressed(input, GameAction.Back) && (_making is null || _making.Next == Stage.Warm || _wheel is not null))
        {
            Leave();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        // Enter does the step's main thing (the highlighted button); left and right choose the days in the brine.
        if (_making?.Next is not Stage.Cut && input.WasPressed(Keys.Enter) && _buttons.Buttons.FirstOrDefault(b => b.Highlight) is { } main)
        {
            main.Press();
            return;
        }

        if (_making?.Next == Stage.Brine && _wheel is null)
        {
            if (_context.Actions.WasPressed(input, GameAction.Left))
            {
                ChangeBrine(-1);
            }
            else if (_context.Actions.WasPressed(input, GameAction.Right))
            {
                ChangeBrine(1);
            }
        }

        switch (_making?.Next)
        {
            case Stage.Warm:
                // Held on the kettle (or Up, or Space): the fire is on; let go and the milk cools.
                _held = (input.Cursor.IsDown && _area.Contains(input.Cursor.Position)) || _context.Actions.IsDown(input, GameAction.Up) || input.IsDown(Keys.Space);
                _warmth = Math.Clamp(_warmth + ((_held ? HeatRate : -CoolRate) * seconds), StartWarmth, 45);
                break;
            case Stage.Set:
                _clock += seconds;
                if (_clock > _making.SetsAfter + 5)
                {
                    TestCurd();
                }

                break;
            case Stage.Cut:
                _knife += _knifeWay * KnifeSpeed * seconds;
                if (_knife is > 1 or < 0)
                {
                    _knifeWay = -_knifeWay;
                    _knife = Math.Clamp(_knife, 0, 1);
                }

                if (input.Tapped(_area) || _context.Actions.WasPressed(input, GameAction.Confirm) || input.WasPressed(Keys.Space))
                {
                    Cut();
                }

                break;
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(14);
        batch.FillRectangle(viewport, new Color(64, 54, 50));
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(60));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var noteHeight = (int)fonts.Small.LineHeight + margin;
        _area = new Rectangle(viewport.X + margin, hud.Bottom + noteHeight + margin, viewport.Width - (2 * margin), buttons.Top - hud.Bottom - noteHeight - (2 * margin));

        // Tiles on the kitchen wall.
        var tile = fonts.Px(48);
        for (var y = hud.Bottom; y < buttons.Top; y += tile)
        {
            for (var x = viewport.X + ((y / tile) % 2 * tile / 2) - tile; x < viewport.Right; x += tile)
            {
                batch.FillRectangle(new Rectangle(x + 1, y + 1, tile - 2, tile - 2), new Color(74, 64, 60));
            }
        }

        if (_making is null)
        {
            var flavours = Farm.Flavours.Order().ToList();
            var size = Math.Min(_area.Height / 6f, _area.Width / (flavours.Count * 3f));
            var radius = size * Cheese.Scale(_size);
            var above = _area.Center.Y + (size * 0.8f) - (size * 1.3f);
            batch.DrawText(fonts.Body, T("Which cheese today?"), new Vector2(_area.Center.X, above - fonts.Px(70)), Palette.Text, Align.Center);
            batch.DrawText(fonts.Small, T("A baby cheese ripens twice as fast; a big wheel takes twice as long, and fetches the most"), new Vector2(_area.Center.X, above - fonts.Px(38)), Palette.Muted, Align.Center, shadow: false);
            for (var i = 0; i < flavours.Count; i++)
            {
                var at = new Vector2(_area.Center.X + ((i - ((flavours.Count - 1) / 2f)) * radius * 2.6f), _area.Center.Y + (radius * 0.8f));
                FarmScene.DrawWheel(batch, at, radius, Ripeness.Young, flavours[i]);
                batch.DrawText(fonts.Small, Cheese.Name(flavours[i]), at + new Vector2(0, radius * 1.2f), Palette.Text, Align.Center);
            }
        }
        else if (_wheel is not null)
        {
            DrawResult(batch, _wheel);
        }
        else
        {
            switch (_making.Next)
            {
                case Stage.Warm:
                    DrawKettle(batch, curd: 0);
                    DrawThermometer(batch);
                    break;
                case Stage.Set:
                    DrawKettle(batch, curd: Math.Clamp(_clock / _making.SetsAfter, 0, 1.4f));
                    batch.DrawText(fonts.Title, string.Create(CultureInfo.InvariantCulture, $"0:{(int)_clock:00}"), new Vector2(_area.Right - fonts.Px(120), _area.Y + fonts.Px(10)), Palette.Text, Align.Center);
                    break;
                case Stage.Cut:
                    DrawVat(batch);
                    break;
                default:
                    DrawBrine(batch);
                    break;
            }
        }

        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, T("Cheese kitchen"), text, Palette.Text, Align.Left);
        if (_making is { } making)
        {
            batch.DrawText(fonts.Body, Cheese.Name(making.Flavour), text, Palette.Accent, Align.Right);
            batch.DrawText(fonts.Small, StepLine(making), text, Palette.Muted, Align.Center, shadow: false);
        }

        if (_note is not null)
        {
            batch.DrawText(fonts.Small, _note, new Rectangle(viewport.X, hud.Bottom + (margin / 2), viewport.Width, noteHeight), Palette.Accent, Align.Center);
        }

        _buttons.Draw(batch, fonts.Small, buttons);
    }

    /// <summary>"Warm · Set · Cut · Brine", the one under way shown in brackets.</summary>
    private static string StepLine(Making making) => string.Join("  ·  ", Enum.GetValues<Stage>().Select(stage =>
    {
        var name = stage switch
        {
            Stage.Warm => T("Warm"),
            Stage.Set => T("Set"),
            Stage.Cut => T("Cut"),
            _ => T("Brine"),
        };
        return making.Next == stage ? $"[{name}]" : making.Score(stage) is { } score ? $"{name} {(int)MathF.Round(score * 100)}%" : name;
    }));

    /// <summary>The copper kettle from the side, the milk inside turning to curd as it sets.</summary>
    private void DrawKettle(SpriteBatch batch, float curd)
    {
        var width = Math.Min(_area.Width * 0.5f, _area.Height * 1.1f);
        var height = width * 0.7f;
        var kettle = new Rectangle((int)(_area.Center.X - (width / 2)), (int)(_area.Center.Y - (height / 2) + (_area.Height * 0.08f)), (int)width, (int)height);

        // The fire under it, burning while it is held.
        var hot = _making?.Next == Stage.Warm && _held;
        for (var i = 0; i < 7; i++)
        {
            var x = kettle.X + (kettle.Width * (0.15f + (0.7f * i / 6f)));
            var flame = height * (hot ? 0.12f : 0.05f) * (1 + (0.3f * MathF.Sin((i * 1.7f) + (_time * 9))));
            batch.FillTriangle(new Rectangle((int)(x - (height * 0.04f)), (int)(kettle.Bottom + (height * 0.1f) - flame), (int)(height * 0.08f), (int)flame), pointsUp: true, new Color(80, 140, 240));
        }

        batch.FillRoundedRectangle(kettle, (int)(height * 0.12f), new Color(186, 110, 60));
        batch.FillRoundedRectangle(new Rectangle(kettle.X + (int)(width * 0.04f), kettle.Y + (int)(height * 0.06f), (int)(width * 0.92f), (int)(height * 0.15f)), (int)(height * 0.05f), new Color(214, 138, 82));
        var milk = Color.Lerp(new Color(250, 248, 238), new Color(246, 226, 150), Math.Clamp(curd, 0, 1));
        batch.FillEllipse(new Vector2(kettle.Center.X, kettle.Y + (height * 0.06f)), new Vector2(width * 0.46f, height * 0.08f), milk);
        if (curd > 1)
        {
            // Overset: the curd shrinks from the side as the whey shows round it.
            batch.FillEllipse(new Vector2(kettle.Center.X, kettle.Y + (height * 0.06f)), new Vector2(width * 0.46f, height * 0.08f), new Color(220, 226, 196));
            batch.FillEllipse(new Vector2(kettle.Center.X, kettle.Y + (height * 0.06f)), new Vector2(width * 0.46f * (1 - ((curd - 1) * 0.6f)), height * 0.07f), new Color(236, 206, 120));
        }

        // Steam once it is warm.
        if (_warmth > 28)
        {
            for (var i = 0; i < 3; i++)
            {
                var rise = ((_time + (i * 0.33f)) % 1) * height * 0.5f;
                batch.FillCircle(new Vector2(kettle.Center.X + ((i - 1) * width * 0.2f), kettle.Y - rise), height * 0.05f, new Color(255, 255, 255, (int)(90 * (1 - (rise / (height * 0.5f))))));
            }
        }
    }

    /// <summary>The thermometer beside the kettle: the band to reach in green, the milk's warmth in red.</summary>
    private void DrawThermometer(SpriteBatch batch)
    {
        var fonts = _context.Fonts;
        var (low, high) = (_making!.GoodKettle ? Making.LowWarmth - 1 : Making.LowWarmth, _making.GoodKettle ? Making.HighWarmth + 1 : Making.HighWarmth);
        var tube = new Rectangle(_area.Right - fonts.Px(110), _area.Y + fonts.Px(10), fonts.Px(26), _area.Height - fonts.Px(30));
        float Y(float degrees) => tube.Bottom - ((degrees - 15) / 30f * tube.Height);
        batch.FillRoundedRectangle(tube, tube.Width / 2, new Color(236, 236, 230));
        batch.FillRectangle(new Rectangle(tube.X, (int)Y(high), tube.Width, (int)(Y(low) - Y(high))), new Color(90, 190, 100));
        var top = (int)Y(Math.Clamp(_warmth, 15, 45));
        batch.FillRectangle(new Rectangle(tube.X + (tube.Width / 3), top, tube.Width / 3, tube.Bottom - top), new Color(220, 60, 50));
        batch.FillCircle(new Vector2(tube.Center.X, tube.Bottom), tube.Width * 0.8f, new Color(220, 60, 50));
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"{_warmth:0.0} °C"), new Vector2(tube.X - fonts.Px(12), top - (fonts.Body.LineHeight / 2)), Palette.Text, Align.Right);
    }

    /// <summary>The vat from above: the curd, the marks to cut along, the cuts made, and the knife sweeping across.</summary>
    private void DrawVat(SpriteBatch batch)
    {
        var vat = _area;
        vat.Inflate(-_area.Width / 8, -_area.Height / 10);
        batch.FillRoundedRectangle(vat, vat.Height / 10, new Color(186, 110, 60));
        var curd = vat;
        curd.Inflate(-vat.Height / 16, -vat.Height / 16);
        batch.FillRoundedRectangle(curd, curd.Height / 12, new Color(246, 226, 150));
        float X(float share) => curd.X + (share * curd.Width);
        for (var i = 0; i < Marks.Length; i++)
        {
            var color = i < _cuts.Count ? new Color(200, 170, 100) : new Color(120, 160, 90);
            for (var y = curd.Y; y < curd.Bottom; y += 14)
            {
                batch.FillRectangle(new Rectangle((int)X(Marks[i]) - 1, y, 3, 7), color);
            }
        }

        foreach (var cut in _cuts)
        {
            batch.FillRectangle(new Rectangle((int)X(cut) - 2, curd.Y, 4, curd.Height), new Color(150, 120, 60));
        }

        // The harp: a frame of wires.
        var knife = X(_knife);
        batch.FillRectangle(new Rectangle((int)knife - 3, vat.Y - (vat.Height / 12), 6, vat.Height + (vat.Height / 6)), new Color(200, 205, 210));
        batch.FillRectangle(new Rectangle((int)knife - 12, vat.Y - (vat.Height / 12) - 10, 24, 10), new Color(110, 80, 60));
        if (_cuts.Count < Marks.Length)
        {
            batch.FillTriangle(new Rectangle((int)X(Marks[_cuts.Count]) - 10, curd.Bottom + 6, 20, 14), pointsUp: true, Palette.Accent);
        }
    }

    /// <summary>The pressed wheel lying in the brine tank, with the days chosen.</summary>
    private void DrawBrine(SpriteBatch batch)
    {
        var fonts = _context.Fonts;
        var tank = _area;
        tank.Inflate(-_area.Width / 5, -_area.Height / 6);
        batch.FillRoundedRectangle(tank, fonts.Px(16), new Color(120, 130, 140));
        var water = tank;
        water.Inflate(-fonts.Px(10), -fonts.Px(10));
        batch.FillRoundedRectangle(water, fonts.Px(12), new Color(150, 196, 220));
        var radius = Math.Min(water.Width, water.Height) * 0.32f;
        FarmScene.DrawWheel(batch, new Vector2(water.Center.X, water.Center.Y), radius * Cheese.Scale(_making!.Size), Ripeness.Young, _making.Flavour);
        for (var i = 0; i < 6; i++)
        {
            batch.FillCircle(new Vector2(water.X + (water.Width * (0.1f + (0.16f * i))), water.Y + (water.Height * (0.15f + (0.1f * (i % 3))))), fonts.Px(4), new Color(240, 248, 255, 160));
        }
    }

    /// <summary>The finished wheel: its stars and how each step went.</summary>
    private void DrawResult(SpriteBatch batch, Wheel wheel)
    {
        var fonts = _context.Fonts;
        var radius = Math.Min(_area.Height / 4.5f, _area.Width / 6f);
        var at = new Vector2(_area.Center.X, _area.Y + radius + fonts.Px(10));
        FarmScene.DrawWheel(batch, at, radius * Cheese.Scale(wheel.Size), Ripeness.Young, wheel.Flavour);
        var y = at.Y + radius + fonts.Px(14);
        batch.DrawText(fonts.Title, Cheese.Stars(wheel.Stars), new Vector2(at.X, y), Palette.Accent, Align.Center);
        y += fonts.Title.LineHeight;
        batch.DrawText(fonts.Small, StepLine(_making!), new Vector2(at.X, y), Palette.Text, Align.Center);
        y += fonts.Small.LineHeight * 1.4f;
        batch.DrawText(fonts.Small, T("Into the cellar to ripen: turn it every other day"), new Vector2(at.X, y), Palette.Muted, Align.Center, shadow: false);
    }
}
