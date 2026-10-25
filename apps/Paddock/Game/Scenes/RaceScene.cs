using Games.Input;
using Games.Race;
using Games.UI;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Plays a raced match back, round by round: each round's two runs (already decided) squeezed into 10 to 20 seconds,
/// drags from the side and everything else from above, or in 3D from behind the car if the player chose that. After each round its winner and what decided it; after five,
/// the match. Skip jumps to the round's result.
/// </summary>
internal sealed class RaceScene : Scene
{
    private static readonly Color Mine = new(216, 52, 44);
    private static readonly Color Theirs = new(47, 111, 224);
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(0.8);

    private readonly GameContext _context;
    private readonly MatchSetup _setup;
    private readonly IReadOnlyList<CarInstance> _player;
    private readonly MatchResult _result;
    private readonly Action _done;
    private readonly ButtonBar _buttons;
    private int _round = -1;
    private double _seconds;
    private Phase _phase;
    private CircuitView? _circuit;
    private DragView? _drag;
    private ChaseView? _chase;

    // Painted tracks, kept for the match: a track and condition that come round again are not painted twice.
    private readonly Dictionary<(string Track, Condition Condition), TrackArt> _arts = [];
    private Menu? _menu;
    private Rectangle _viewport;

    /// <param name="player">The player's cars in round order.</param>
    /// <param name="result">The match, already decided.</param>
    /// <param name="done">Called after the last screen, once this scene has closed.</param>
    public RaceScene(GameContext context, MatchSetup setup, IReadOnlyList<CarInstance> player, MatchResult result, Action done)
    {
        _context = context;
        _setup = setup;
        _player = player;
        _result = result;
        _done = done;
        _buttons = new ButtonBar(new Button(T("Skip"), Skip));
        StartRound(0);
    }

    private enum Phase
    {
        Racing,
        Round,
        Match,
    }

    private RoundResult Current => _result.Rounds[_round];

    private TrackDef Track => _setup.Rounds[_round].Track;

    private double WinnerTime => Math.Min(Current.Player.Time, Current.Opponent.Time);

    private double Duration => Playback.Duration(WinnerTime, Track.Kind);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_phase != Phase.Racing)
        {
            _menu?.Update(input, Controls.Ui, elapsed);
            return;
        }

        if (_buttons.Update(input) || _context.Actions.WasPressed(input, GameAction.Confirm) || _context.Actions.WasPressed(input, GameAction.Back))
        {
            Skip();
            return;
        }

        _seconds += elapsed.TotalSeconds;
        var (_, mine, theirs) = Positions();
        _circuit?.Update(mine, theirs, _viewport, elapsed);
        _chase?.Update(mine, theirs, elapsed);
        if (_seconds >= Duration + Linger.TotalSeconds)
        {
            ShowRound();
        }
    }

    public override void DrawLayers(SpriteBatch batch, Rectangle viewport)
    {
        if (_chase is not null)
        {
            var (_, mine, theirs) = Positions();
            _chase.Render(batch.GraphicsDevice, viewport, mine, theirs);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var (time, mine, theirs) = Positions();
        if (_viewport != viewport)
        {
            // The first frame, or a new window size: place the camera before drawing.
            _viewport = viewport;
            _circuit?.Update(mine, theirs, viewport, TimeSpan.Zero);
        }

        batch.FillRectangle(viewport, Palette.Background);
        if (_chase is not null)
        {
            _chase.Draw(batch, viewport);
        }
        else if (_circuit is not null)
        {
            _circuit.Draw(batch, mine, theirs);
        }
        else
        {
            _drag!.Draw(batch, fonts, viewport, mine, theirs);
        }

        var (_, condition) = _setup.Rounds[_round];
        Jentl.Core.Graphics.Weather.Draw(batch, viewport, condition switch
        {
            Condition.Wet => Precipitation.Rain,
            Condition.Snow or Condition.Ice => Precipitation.Snow,
            _ => Precipitation.None,
        }, _seconds, Jentl.Core.Graphics.Weather.Gust(_seconds, Wind(condition)));
        Hud(batch, viewport, time, mine, theirs);
        if (_phase == Phase.Racing)
        {
            var bar = new Rectangle(viewport.X, viewport.Bottom - ButtonBar.Height(fonts.Body) - fonts.Px(16), viewport.Width, ButtonBar.Height(fonts.Body));
            _buttons.Draw(batch, fonts.Body, bar);
        }
        else if (_menu is not null)
        {
            var (title, lines) = _phase == Phase.Round ? RoundSummary.Round(_result, _round) : RoundSummary.Match(_result);
            OverlayPanel.Draw(batch, viewport, fonts, title, lines, _menu);
        }
    }

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Race);

    // The screens a race returns to have no music of their own.
    protected override void OnExit() => _context.Sounds.PlayMusic(Music.Menu);

    protected override void Dispose(bool disposing)
    {
        foreach (var art in _arts.Values)
        {
            art.Dispose();
        }

        _chase?.Dispose();

        base.Dispose(disposing);
    }

    private void StartRound(int round)
    {
        _chase?.Dispose();
        (_circuit, _drag, _chase) = (null, null, null);
        _round = round;
        _seconds = 0;
        _phase = Phase.Racing;
        _menu = null;
        var (track, condition) = _setup.Rounds[round];
        var cars = _context.PaddockData.Cars;
        var view = track.Kind == TrackKind.Drag ? "side" : "top";
        var mine = cars[$"{view}/{_player[round].ModelId}"];
        var theirs = cars[$"{view}/{_setup.Opponent[round].ModelId}"];
        if (track.Kind == TrackKind.Drag)
        {
            _drag = new DragView(track.Length, mine, theirs, condition);
        }
        else
        {
            if (!_arts.TryGetValue((track.Id, condition), out var art))
            {
                art = _arts[(track.Id, condition)] = TrackArt.Build(_context.PaddockData.Cars.Texture.GraphicsDevice, track, condition);
            }
            // The top-down view also keeps the mini-map, under the 3D one too.
            var camera = _context.Paddock.Settings.Value.RaceCamera;
            _circuit = new CircuitView(art, mine, theirs, camera, condition);
            if (camera == RaceCamera.Chase)
            {
                var data = _context.PaddockData;
                _chase = new ChaseView(art, data.CarPicture($"side/{_player[round].ModelId}"), data.CarPicture($"side/{_setup.Opponent[round].ModelId}"), condition);
            }

            _viewport = Rectangle.Empty;
        }

        _context.Sounds.Play(Sound.Navigate);
    }

    private void Skip()
    {
        if (_phase == Phase.Racing)
        {
            _seconds = Duration;
            ShowRound();
        }
    }

    private void ShowRound()
    {
        _phase = Phase.Round;
        _context.Sounds.Play(Current.Winner == 0 ? Sound.Completed : Sound.Lose);
        var last = _round == _result.Rounds.Count - 1;
        _menu = new Menu(_context.Sounds, new MenuItem(last ? T("See the result") : T("Next round"), () =>
        {
            if (last)
            {
                ShowMatch();
            }
            else
            {
                StartRound(_round + 1);
            }
        }));
    }

    private void ShowMatch()
    {
        _phase = Phase.Match;
        _context.Sounds.Play(_result.PlayerWon ? Sound.Fanfare : Sound.Lose);
        _menu = new Menu(_context.Sounds, new MenuItem(T("Continue"), () =>
        {
            Scenes.Pop(Navigation.Fade());
            _done();
        }));
    }

    private void Hud(SpriteBatch batch, Rectangle viewport, double time, double mine, double theirs)
    {
        var fonts = _context.Fonts;
        var small = fonts.Small;
        var pad = fonts.Px(12);
        var (track, condition) = _setup.Rounds[_round];
        string[] lines =
        [
            T("Round {0} of {1}", _round + 1, _result.Rounds.Count),
            T(track.Name),
            $"{Words.Of(track.Kind)} · {Words.Of(condition)}",
        ];
        var width = (int)lines.Max(l => small.MeasureString(l).X) + (2 * pad);
        var box = new Rectangle(viewport.X + pad, viewport.Y + pad, width, (int)(small.LineHeight * (lines.Length + 1.2f)) + pad);
        batch.FillRectangle(box, new Color(0, 0, 0, 150));
        for (var i = 0; i < lines.Length; i++)
        {
            batch.DrawText(small, lines[i], new Vector2(box.X + pad, box.Y + (pad / 2) + (i * small.LineHeight)), i == 0 ? Palette.Accent : Palette.Text, shadow: false);
        }

        // The gap: how long until the car behind reaches where the leader is now.
        var ahead = mine >= theirs;
        var gap = Math.Max(0, ahead ? Current.Opponent.TimeAt(mine) - time : Current.Player.TimeAt(theirs) - time);
        var gapText = ahead ? T("Ahead +{0:0.0} s", gap) : T("Behind {0:0.0} s", gap);
        batch.DrawText(small, gapText, new Vector2(box.X + pad, box.Y + (pad / 2) + (lines.Length * small.LineHeight)), ahead ? new Color(136, 255, 136) : new Color(255, 136, 136), shadow: false);

        if (_circuit is not null)
        {
            var map = _circuit.Art.Map;
            _circuit.DrawMap(batch, new Point(viewport.Right - map.Width - pad, viewport.Y + pad), mine, theirs, Mine, Theirs);
        }
    }

    // Rain comes with a stiff wind, snow with a breeze; each track has its own side the wind blows from.
    private float Wind(Condition condition)
    {
        var strength = condition switch
        {
            Condition.Wet => 0.55f,
            Condition.Snow or Condition.Ice => 0.35f,
            _ => 0,
        };
        return Jentl.Collect.StableSeed.Of(Track.Id) % 2 == 0 ? strength : -strength;
    }

    // The race time on screen, and how far each car has come.
    private (double Time, double Mine, double Theirs) Positions()
    {
        var time = Playback.RaceTime(Math.Min(_seconds, Duration), WinnerTime, Track.Kind);
        return (time, Current.Player.DistanceAt(time), Current.Opponent.DistanceAt(time));
    }
}
