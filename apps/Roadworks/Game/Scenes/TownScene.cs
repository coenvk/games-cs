using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.Traffic;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Roadworks;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>What a press on the grid does.</summary>
internal enum Tool
{
    Road,
    Bulldoze,
    Junction,
    Widen,
}

/// <summary>
/// A Roadworks level. Building: drag along the grid to lay road, drag over road with the bulldozer to take it away,
/// tap a junction to change it (give way, lights, roundabout); the cost shows against the budget. Run lets the
/// traffic in for the level's minutes, fast, drawn as it drives; then the verdict: the delay against the target,
/// the trips made, and stars.
/// </summary>
internal sealed class TownScene : Scene
{
    private const float Speed = 12;

    private readonly GameContext _context;
    private readonly ButtonBar _buttons = new();
    private readonly TrafficView _view = new();
    private Tool _tool = Tool.Road;
    private Trial? _trial;
    private Spot? _from;
    private Rectangle _area;
    private float _cell;
    private Vector2 _origin;
    private string? _note;

    public TownScene(GameContext context, int level)
    {
        _context = context;
        Number = Math.Clamp(level, 0, context.Towns.Levels.Count - 1);
        Plan = context.Towns.PlanFor(Number);
        SetButtons();
    }

    public int Number { get; }

    public Plan Plan { get; }

    private TownLevel Level => Plan.Level;

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"Level {Number + 1}: {Plan.Roads.Count} stretches, cost {Plan.Cost} of {Level.Budget}"),
        _trial is { } trial ? string.Create(CultureInfo.InvariantCulture, $"Time {trial.Simulation.Time:0} s, {trial.Simulation.Vehicles.Count} driving, {trial.Simulation.Completed} arrived") : "Building",
    ];

    protected override void OnEnter() => _context.PlayMood(Music.Puzzle);

    public void Restart()
    {
        StopRun();
        Plan.Clear();
        _context.Towns.Keep(Number, Plan);
    }

    public void NextLevel() => Scenes.Replace(new TownScene(_context, Math.Min(Number + 1, _context.Towns.Levels.Count - 1)), Navigation.Fade());

    public void StopRun()
    {
        _trial = null;
        SetButtons();
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new TownPauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (_trial is { } trial)
        {
            trial.Advance((float)elapsed.TotalSeconds * Speed);
            if (trial.IsDone)
            {
                Finish(trial);
            }

            return;
        }

        Build(input);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(14);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(60));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        _area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - (2 * margin), buttons.Top - hud.Bottom - (2 * margin));

        // The grid with a cell of room round it for the roads out of town, and their gates' discs past that.
        _cell = MathF.Min(_area.Width / (Level.Width + 2.6f), _area.Height / (Level.Height + 2.6f));
        _origin = new Vector2(_area.Center.X - (Level.Width * _cell / 2), _area.Center.Y - (Level.Height * _cell / 2));
        batch.FillRectangle(viewport, new Color(64, 104, 62));
        var town = new Rectangle((int)(_origin.X - (_cell / 2)), (int)(_origin.Y - (_cell / 2)), (int)((Level.Width + 1) * _cell), (int)((Level.Height + 1) * _cell));
        batch.FillRoundedRectangle(town, (int)(_cell / 3), new Color(78, 122, 72));

        if (_trial is { } trial)
        {
            _view.Scale = _cell / Plan.Cell;
            _view.Centre = Plan.World(new Spot(0, 0)) + new System.Numerics.Vector2(Level.Width * Plan.Cell / 2, -Level.Height * Plan.Cell / 2);
            DrawBlocked(batch);
            _view.Draw(batch, trial.Simulation, _area);
            DrawGates(batch);
        }
        else
        {
            DrawGrid(batch);
        }

        DrawHud(batch, hud, margin);
        _buttons.Draw(batch, fonts.Small, buttons);
    }

    private void SetButtons()
    {
        if (_trial is not null)
        {
            _buttons.Set(new Button(T("Stop"), StopRun, Highlight: true), new Button(T("Menu"), () => Scenes.Push(new TownPauseScene(_context, this))));
            return;
        }

        _buttons.Set(
            new Button(T("Road"), () => Pick(Tool.Road), Highlight: _tool == Tool.Road),
            new Button(T("Bulldoze"), () => Pick(Tool.Bulldoze), Highlight: _tool == Tool.Bulldoze),
            new Button(T("Junction"), () => Pick(Tool.Junction), Highlight: _tool == Tool.Junction),
            new Button(T("Widen"), () => Pick(Tool.Widen), Highlight: _tool == Tool.Widen),
            new Button(T("Run"), Run),
            new Button(T("Menu"), () => Scenes.Push(new TownPauseScene(_context, this))));
    }

    private void Pick(Tool tool)
    {
        _tool = tool;
        SetButtons();
    }

    private void Run()
    {
        var trial = new Trial(Plan, seed: Number + 1);
        if (trial.Unreachable is { } missing)
        {
            _note = T("No way yet: {0}", missing);
            _context.Sounds.Play(Sound.Error);
            return;
        }

        _note = null;
        _trial = trial;
        _context.Towns.Keep(Number, Plan);
        SetButtons();
    }

    private void Finish(Trial trial)
    {
        var verdict = trial.Verdict();
        var best = _context.Towns.Record(Number, verdict.Stars);
        _context.Sounds.Play(verdict.Passed ? Sound.Fanfare : Sound.Lose);
        if (verdict.Passed)
        {
            Celebration.Confetti(Scenes);
        }

        StopRun();
        Scenes.Push(new TownVerdictScene(_context, this, verdict, best));
    }

    /// <summary>The nearest spot to a point, if it is close enough to mean it.</summary>
    private Spot? SpotAt(Vector2 point)
    {
        var x = (int)MathF.Round((point.X - _origin.X) / _cell);
        var y = (int)MathF.Round((point.Y - _origin.Y) / _cell);
        var spot = new Spot(x, y);
        return Level.Inside(spot) && Vector2.Distance(Screen(spot), point) < _cell * 0.45f ? spot : null;
    }

    private Vector2 Screen(Spot spot) => _origin + (new Vector2(spot.X, spot.Y) * _cell);

    private void Build(InputState input)
    {
        var pointer = input.Cursor;
        if (_tool == Tool.Junction)
        {
            if (input.Tap is { } tap && SpotAt(tap) is { } spot)
            {
                var changed = Plan.Cycle(spot);
                _context.Sounds.Play(changed ? Sound.Click : Sound.Error);
                Kept();
            }

            return;
        }

        if (_tool == Tool.Widen)
        {
            // A tap on the middle of a stretch: two lanes each way, or one again.
            if (input.Tap is { } tap && Plan.Roads.FirstOrDefault(s => Vector2.Distance((Screen(s.A) + Screen(s.B)) / 2, tap) < _cell * 0.35f) is var stretch
                && Plan.Roads.Contains(stretch))
            {
                Plan.Widen(stretch);
                _context.Sounds.Play(Sound.Click);
                Kept();
            }

            return;
        }

        if (!pointer.IsDown)
        {
            _from = null;
            return;
        }

        if (_tool == Tool.Bulldoze)
        {
            // Over the middle of a stretch: away with it.
            foreach (var stretch in Plan.Roads.ToList())
            {
                var middle = (Screen(stretch.A) + Screen(stretch.B)) / 2;
                if (Vector2.Distance(middle, pointer.Position) < _cell * 0.3f && Plan.Remove(stretch))
                {
                    _context.Sounds.Play(Sound.Card);
                    Kept();
                }
            }

            return;
        }

        if (SpotAt(pointer.Position) is not { } at)
        {
            return;
        }

        // A drag lays road a spot at a time towards where the pointer is, however fast it moves.
        if (_from is { } last)
        {
            while (last != at)
            {
                var (dx, dy) = (at.X - last.X, at.Y - last.Y);
                var next = Math.Abs(dx) >= Math.Abs(dy) ? last with { X = last.X + Math.Sign(dx) } : last with { Y = last.Y + Math.Sign(dy) };
                if (Plan.Lay(last, next))
                {
                    _context.Sounds.Play(Sound.Click, volume: 0.5f);
                }
                else if (Level.IsBlocked(next))
                {
                    break;
                }

                last = next;
            }

            Kept();
        }

        _from = Level.IsBlocked(at) ? null : at;
    }

    private void Kept()
    {
        _note = null;
        _context.Towns.Keep(Number, Plan);
    }

    private void DrawGrid(SpriteBatch batch)
    {
        DrawBlocked(batch);
        var width = _cell * 0.22f;
        foreach (var gate in Level.Gates)
        {
            batch.DrawLine(Screen(gate.At), Screen(Plan.Outside(gate)), new Color(70, 72, 78), width);
        }

        foreach (var stretch in Plan.Roads)
        {
            var wide = Plan.Wide.Contains(stretch);
            batch.DrawLine(Screen(stretch.A), Screen(stretch.B), new Color(70, 72, 78), wide ? width * 1.8f : width);
            if (wide)
            {
                // The line between the directions.
                batch.DrawLine(Screen(stretch.A), Screen(stretch.B), new Color(230, 230, 220, 160), MathF.Max(1, width * 0.08f));
            }
        }

        // Spots: dots where road can go; road ends rounded off; junctions marked by how they are run.
        for (var y = 0; y <= Level.Height; y++)
        {
            for (var x = 0; x <= Level.Width; x++)
            {
                var spot = new Spot(x, y);
                if (Level.IsBlocked(spot))
                {
                    continue;
                }

                var degree = Plan.Degree(spot);
                if (degree > 0)
                {
                    batch.FillCircle(Screen(spot), width / 2, new Color(70, 72, 78));
                }
                else
                {
                    batch.FillCircle(Screen(spot), MathF.Max(2, _cell * 0.05f), new Color(200, 220, 190, 140));
                }

                if (degree >= 3)
                {
                    DrawJunction(batch, spot, Plan.JunctionAt(spot));
                }
            }
        }

        DrawGates(batch);
    }

    private void DrawJunction(SpriteBatch batch, Spot spot, Junction junction)
    {
        var at = Screen(spot);
        switch (junction)
        {
            case Junction.Lights:
                var box = new Rectangle((int)(at.X - (_cell * 0.09f)), (int)(at.Y - (_cell * 0.2f)), (int)(_cell * 0.18f), (int)(_cell * 0.4f));
                batch.FillRoundedRectangle(box, (int)(_cell * 0.05f), new Color(30, 30, 34));
                batch.FillCircle(at - new Vector2(0, _cell * 0.11f), _cell * 0.055f, new Color(230, 70, 60));
                batch.FillCircle(at + new Vector2(0, _cell * 0.11f), _cell * 0.055f, new Color(80, 210, 100));
                break;
            case Junction.Roundabout:
                batch.FillCircle(at, _cell * 0.24f, new Color(70, 72, 78));
                batch.FillCircle(at, _cell * 0.13f, new Color(92, 150, 82));
                break;
            default:
                batch.FillCircle(at, _cell * 0.06f, new Color(236, 236, 228));
                break;
        }
    }

    private void DrawBlocked(SpriteBatch batch)
    {
        // Houses: a block with a roof.
        foreach (var spot in Level.Blocked)
        {
            var at = Screen(spot);
            var size = _cell * 0.6f;
            batch.FillRectangle(new Rectangle((int)(at.X - (size / 2)), (int)(at.Y - (size / 2)), (int)size, (int)size), new Color(200, 170, 130));
            batch.FillRectangle(new Rectangle((int)(at.X - (size / 2)), (int)(at.Y - (size / 2)), (int)size, (int)(size * 0.35f)), new Color(170, 70, 60));
        }
    }

    private void DrawGates(SpriteBatch batch)
    {
        var font = _context.Fonts.Small;
        foreach (var gate in Level.Gates)
        {
            var at = Screen(Plan.Outside(gate));
            batch.FillCircle(at, _cell * 0.26f, Palette.Accent);
            batch.DrawText(font, gate.Name, new Rectangle((int)(at.X - _cell), (int)(at.Y - (_cell / 2)), (int)(_cell * 2), (int)_cell), Palette.Ink, Align.Center, shadow: false);
        }
    }

    private void DrawHud(SpriteBatch batch, Rectangle hud, int margin)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, T("{0}. {1}", Number + 1, Level.Name), text, Palette.Text, Align.Left);
        if (_trial is { } trial)
        {
            var time = TimeSpan.FromSeconds(trial.Simulation.Time);
            var verdict = trial.Verdict();
            batch.DrawText(fonts.Small, T("{0} of {1} min  ·  {2} arrived  ·  delay {3:0} s (target {4:0})", $"{(int)time.TotalMinutes}:{time.Seconds:00}", Level.Minutes, verdict.Arrived, verdict.AverageDelay, Level.TargetDelay), text, Palette.Accent, Align.Right);
            return;
        }

        var cost = T("Cost {0} of {1}", Plan.Cost, Level.Budget);
        batch.DrawText(fonts.Body, cost, text, Plan.OverBudget ? new Color(240, 100, 90) : Palette.Accent, Align.Right);
        var hint = _note ?? (_tool switch
        {
            Tool.Bulldoze => T("Drag over road to take it away"),
            Tool.Junction => T("Tap a junction: give way, lights, roundabout"),
            Tool.Widen => T("Tap a stretch: two lanes each way, for overtaking and turning traffic"),
            _ => T("Drag along the grid to lay road from gate to gate"),
        });
        batch.DrawText(fonts.Small, hint, text, _note is null ? Palette.Muted : new Color(240, 100, 90), Align.Center);
    }
}

internal sealed class TownPauseScene : PanelScene
{
    public TownPauseScene(GameContext context, TownScene town)
        : base(context) => Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Clear the plan"), () =>
            {
                Scenes.Pop();
                town.Restart();
            }),
            new MenuItem(T("Select level"), () => Scenes.Replace(new TownLevelsScene(context))),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));

    protected override string Title => "Roadworks";
}

internal sealed class TownVerdictScene : PanelScene
{
    private readonly Verdict _verdict;
    private readonly bool _best;

    public TownVerdictScene(GameContext context, TownScene town, Verdict verdict, bool best)
        : base(context)
    {
        (_verdict, _best) = (verdict, best);
        List<MenuItem> items = [];
        if (verdict.Passed && town.Number < context.Towns.Levels.Count - 1)
        {
            items.Add(new MenuItem(T("Next level"), () =>
            {
                Scenes.Pop();
                town.NextLevel();
            }));
        }

        items.Add(new MenuItem(T("Back to the plan"), () => Scenes.Pop()));
        items.Add(new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
        Menu = new Menu(context.Sounds, [.. items]);
    }

    protected override string Title => _verdict.Passed ? T("The town flows!") : T("Not good enough yet");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(new string('★', _verdict.Stars) + new string('☆', 3 - _verdict.Stars), Palette.Accent),
        new(T("Average delay {0:0} s", _verdict.AverageDelay), Palette.Text),
        new(T("{0} of {1} trips made", _verdict.Arrived, _verdict.Expected), Palette.Text),
        new(T("Cost {0}", _verdict.Cost), Palette.Muted),
        new(_best ? T("Your best for this town") : _verdict.Passed ? T("Fewer stops, more stars") : T("Try lights, roundabouts or another way round"), Palette.Muted),
    ];
}

internal sealed class TownLevelsScene : PanelScene
{
    public TownLevelsScene(GameContext context)
        : base(context)
    {
        var towns = context.Towns;
        Menu = new Menu(
            context.Sounds,
            [
                .. Enumerable.Range(0, towns.Levels.Count).Where(towns.IsOpen).Select(level => new MenuItem(
                    T("{0}. {1}  {2}", level + 1, towns.Levels[level].Name, new string('★', towns.Stars(level)) + new string('☆', 3 - towns.Stars(level))),
                    () => Scenes.Replace(new TownScene(context, level), Navigation.Fade()))),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    protected override string Title => T("Select level");
}
