using System.Globalization;
using Games.Input;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.Traffic;
using Jentl.Core.UI;
using Jentl.Traffic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Numerics = System.Numerics;

namespace Games.Scenes;

/// <summary>
/// A small town's traffic on Jentl.Traffic: lights in the middle on a two-lane main road, a roundabout to the east, a
/// crossroads with priority from the right to the west, and a priority junction on a bending road to the north.
/// Cycle paths run along the roads from the lights to the east and north, cyclists turning left in two at the lights
/// and riding their own ring round the roundabout. Turn the traffic up or down (the cycling with it), speed time up,
/// and watch the measurements.
/// </summary>
internal sealed class TrafficScene : Scene
{
    private static readonly float[] Speeds = [1, 2, 4, 8];

    private readonly GameContext _context;
    private readonly ButtonBar _buttons = new();
    private readonly TrafficView _view = new();
    private TrafficSimulation _simulation;
    private float _demand = 150;
    private int _speed;
    private bool _paused;
    private float _zoom = 3;
    private Numerics.Vector2 _look;
    private Rectangle _area;
    private Vector2? _dragFrom;

    public TrafficScene(GameContext context)
    {
        _context = context;
        _simulation = Start();
        SetButtons();
    }

    public override IEnumerable<string> DebugInfo => [string.Create(CultureInfo.InvariantCulture, $"Lanes {_simulation.Network.Lanes.Count}  vehicles {_simulation.Vehicles.Count}  time {_simulation.Time:0} s")];

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

        // The wheel zooms round the pointer; a drag moves the town.
        if (input.Wheel != 0 && _area.Contains(input.Cursor.Position))
        {
            var before = _view.ToWorld(_area, input.Cursor.Position);
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.2f, input.Wheel), 1, 12);
            Frame();
            _look += before - _view.ToWorld(_area, input.Cursor.Position);
        }

        if (input.Cursor.IsDown && _area.Contains(input.Cursor.Position))
        {
            if (_dragFrom is { } from)
            {
                var moved = input.Cursor.Position - from;
                _look -= new Numerics.Vector2(moved.X, -moved.Y) / _view.Scale;
            }

            _dragFrom = input.Cursor.Position;
        }
        else
        {
            _dragFrom = null;
        }

        if (!_paused)
        {
            _simulation.Step((float)Math.Min(elapsed.TotalSeconds, 0.1) * Speeds[_speed]);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(12);
        batch.FillRectangle(viewport, new Color(112, 156, 96));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)(fonts.Small.LineHeight * 2) + margin);
        _area = new Rectangle(viewport.X, hud.Bottom, viewport.Width, buttons.Top - hud.Bottom);
        Frame();
        _view.Draw(batch, _simulation, _area);

        var stats = _simulation.Stats();
        batch.FillRectangle(hud, new Color(0, 0, 0, 150));
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{_demand:0} vehicles/h per edge   ×{Speeds[_speed]:0}{(_paused ? " (paused)" : "")}   time {(int)_simulation.Time / 60}:{(int)_simulation.Time % 60:00}   (scroll to zoom, drag to move)");
        var line2 = string.Create(CultureInfo.InvariantCulture,
            $"on the road {stats.Active}   arrived {stats.Completed} ({stats.Throughput:0}/h)   trip {stats.AverageTravelTime:0} s, delay {stats.AverageDelay:0} s, {stats.AverageStops:0.0} stops   standing {stats.Standing}   waiting to enter {stats.Queued}");
        batch.DrawText(fonts.Small, line, new Vector2(hud.X + margin, hud.Y + (margin / 2)), Palette.Text, shadow: false);
        batch.DrawText(fonts.Small, line2, new Vector2(hud.X + margin, hud.Y + (margin / 2) + fonts.Small.LineHeight), Palette.Muted, shadow: false);
        _buttons.Draw(batch, fonts.Small, buttons);
    }

    // The whole town fitted, then zoomed in round where the player looks.
    private void Frame()
    {
        _view.Fit(_simulation.Network, _area);
        _view.Scale *= _zoom;
        _view.Centre = _look;
    }

    private static RoadNetwork Town()
    {
        var b = new NetworkBuilder();
        var lights = b.Node(0, 0);
        var round = b.Node(260, 0);
        var plain = b.Node(-260, 0);
        var junction = b.Node(0, 200);
        var east = b.Node(480, 0);
        var west = b.Node(-480, 0);

        // The main road east and west through the lights, two lanes each way.
        var main = b.Road(lights, round, 2, 50);
        b.Road(lights, plain, 2, 50);
        var eastOut = b.Road(round, east, 2, 70);
        b.Road(plain, west, 1, 70);
        var up = b.Road(lights, junction, 1, 50);
        var south = b.Road(lights, b.Node(0, -230), 1, 50);
        var roundNorth = b.Road(round, b.Node(260, 230), 1, 50);
        var roundSouth = b.Road(round, b.Node(260, -230), 1, 50);
        b.Road(plain, b.Node(-260, -230), 1, 50);
        var bend = b.Road(plain, junction, 1, 50, new Numerics.Vector2(-240, 150), new Numerics.Vector2(-120, 210));
        var north = b.Road(junction, b.Node(0, 330), 1, 50);
        b.CyclePaths(main, eastOut, up, south, roundNorth, roundSouth, north);
        b.Road(junction, b.Node(200, 230), 1, 50);
        b.Signals(lights, green: 18);
        b.Roundabout(round, 16);
        b.Priority(junction, north, bend);
        return b.Build();
    }

    private TrafficSimulation Start()
    {
        // Drivers change lanes on the two-lane roads: overtaking, and moving out of a lane held up by a turn.
        var network = Town();
        var simulation = new TrafficSimulation(network, seed: _context.Deal ?? 1) { LaneChanges = true };
        simulation.SetDemand(_demand);
        simulation.SetCycling(_demand / 2);

        // A bus line across town, east to west, stopping before the lights and on the way out; one every minute and a half.
        Node At(float x, float y) => network.Nodes.First(n => n.Position == new Numerics.Vector2(x, y));
        Lane Toward(Node from, Node to) => from.Roads.First(r => r.Other(from) == to).LeavingFrom(from)[^1];
        simulation.AddLine(new BusLine("1", At(480, 0), At(-480, 0), [BusStop.On(Toward(At(260, 0), At(0, 0))), BusStop.On(Toward(At(-260, 0), At(-480, 0)), 0.4f)], Headway: 90));
        return simulation;
    }

    private void SetButtons() => _buttons.Set(
        new Button("Less traffic", () => Demand(-50)),
        new Button("More traffic", () => Demand(50)),
        new Button($"Speed ×{Speeds[_speed]:0}", () =>
        {
            _speed = (_speed + 1) % Speeds.Length;
            SetButtons();
        }),
        new Button(_paused ? "Run" : "Pause", () =>
        {
            _paused = !_paused;
            SetButtons();
        }),
        new Button("Restart", () => _simulation = Start()),
        new Button("Back", () => Scenes.Pop()));

    private void Demand(float change)
    {
        _demand = Math.Clamp(_demand + change, 0, 600);
        _simulation.SetDemand(_demand);
        _simulation.SetCycling(_demand / 2);
    }
}
