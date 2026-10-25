using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Multiplayer;
using Games.Input;
using Games.Poker;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Poker;
using Poker.Arena;
using PokerBots;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>How fast a watched match plays.</summary>
internal enum WatchSpeed
{
    /// <summary>Nothing happens until another speed is chosen.</summary>
    Pause,

    /// <summary>One action, then pause.</summary>
    Step,

    /// <summary>An action every 650 ms.</summary>
    Normal,

    /// <summary>An action every 160 ms.</summary>
    Fast,

    /// <summary>The rest of the hand at once, then pause.</summary>
    Instant,
}

/// <summary>
/// A workshop match played out on the chip table: the bots (and perhaps the person, with the Hold'em controls) at the
/// table, a log of what happens on the right with the bots' faults marked ("Mine: late, folds"), and the speed below:
/// Pause, Step, 1x, 4x and Instant. The table is stepped off the UI thread, one step at a time; the view follows a copy
/// of the hand that only the UI thread touches. At the end the match is saved to the workshop's matches folder and its
/// results open; leaving before then stops the bots and saves nothing.
/// </summary>
internal sealed class WatchScene : Scene
{
    private const int HudHeight = 64;
    private const int MaxLog = 200;
    private static readonly TimeSpan NormalDelay = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(160);

    // At Instant the chips in flight land in a frame.
    private static readonly TimeSpan Landed = TimeSpan.FromSeconds(1);
    private static readonly Color Problem = new(236, 120, 108);

    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly ArenaTable _table;
    private readonly HumanSeat? _person;
    private readonly CancellationTokenSource _cancel = new();
    private readonly PokerTableView _view;
    private readonly HoldemControls _controls = new();
    private readonly ButtonBar _speeds = new();
    private readonly ButtonBar _moves = new();
    private readonly List<(string Text, bool Fault)> _log = [];
    private readonly Dictionary<int, string> _lastAction = [];
    private readonly HashSet<int> _shown = [];
    private readonly Func<int, bool> _faceUp;
    private readonly string[] _names;
    private Task? _starting;
    private Task<IReadOnlyList<PokerEvent>>? _step;

    // The table's hand being followed, and the copy of it the view draws and the controls read.
    private HoldemHand? _live;
    private HoldemHand? _hand;
    private int _handNumber;
    private int _faultsSeen;
    private WatchSpeed _speed = WatchSpeed.Normal;
    private WatchSpeed _resume = WatchSpeed.Normal;
    private TimeSpan _sinceStep;
    private bool _finished;
    private bool _stopped;

    /// <summary>Plays <paramref name="table"/>, which the scene then owns; <paramref name="person"/> is the person's seat at it, if they sit.</summary>
    public WatchScene(WorkshopContext workshop, ArenaTable table, HumanSeat? person = null)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(table);
        _workshop = workshop;
        _context = workshop.Game;
        _table = table;
        _person = person;
        _names = [.. table.Results().Select(r => r.Name)];
        _view = new PokerTableView(_context);

        // Watching bots only, every hand is open; with the person at the table they see what a player would.
        _faceUp = person is null ? _ => true : seat => seat == person.Seat || _shown.Contains(seat);
        _starting = Task.Run(table.StartAsync);
        SetSpeed(WatchSpeed.Normal);
    }

    /// <summary>The scene for <paramref name="request"/>: its bots seated (and the person, at a null seat) at a new table.</summary>
    public static Scene Open(WorkshopContext workshop, MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(request);
        var seats = new List<IArenaSeat>();
        HumanSeat? person = null;

        // A bot seated twice is numbered, so that each keeps its own results and statistics.
        var names = SeatNames.Distinct([.. request.Seats.Select(b => b?.Name ?? T("You"))]);
        try
        {
            for (var seat = 0; seat < request.Seats.Count; seat++)
            {
                if (request.Seats[seat] is { } bot)
                {
                    seats.Add(workshop.Seat(bot, unchecked((int)ArenaTable.SeatSeed(request.Settings.Seed, 0, seat)), names[seat]));
                }
                else
                {
                    person = new HumanSeat(names[seat]);
                    seats.Add(person);
                }
            }

            return new WatchScene(workshop, new ArenaTable(seats, request.Settings), person);
        }
        catch
        {
            seats.ForEach(s => s.Dispose());
            throw;
        }
    }

    // The person's turn: the table is waiting for them and the copy agrees it is their move.
    private bool IsPersonsTurn => _person is { IsAsked: true } person && _hand is { IsOver: false } hand && hand.CurrentSeat == person.Seat;

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Table);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _view.Update(_speed == WatchSpeed.Instant ? Landed : elapsed, []);
        if (_speeds.Update(input) || UpdateSpeedKeys(input))
        {
            return;
        }

        if (_starting is { } starting)
        {
            if (!starting.IsCompleted)
            {
                return;
            }

            _starting = null;
            starting.GetAwaiter().GetResult();
            SayProblems();
        }

        if (_step is { } step)
        {
            if (!step.IsCompleted)
            {
                UpdatePerson(input);
                return;
            }

            _step = null;
            Follow(step.GetAwaiter().GetResult());
        }

        if (_table.IsOver)
        {
            // The last chips land before the results open.
            if (!_finished && (!_view.IsAnimating || _speed == WatchSpeed.Instant))
            {
                Finish();
            }

            return;
        }

        _sinceStep += elapsed;
        if (IsTimeToStep())
        {
            _sinceStep = TimeSpan.Zero;
            _step = Task.Run(() => _table.StepAsync(_cancel.Token));
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
        var title = _hand is null ? T("Watch a match") : T("Watching: hand {0}, blinds {1}/{2}", _handNumber, _hand.SmallBlind, _hand.BigBlind);
        batch.DrawText(fonts.Body, title, text, Palette.Text, Align.Left);
        var cards = _person is null ? T("Every bot's cards are face up") : T("Your cards, and those shown at showdown");
        batch.DrawText(fonts.Small, $"{T("Speed: {0}", SpeedName(_speed))}  ·  {cards}", text, Palette.Muted, Align.Right, shadow: false);

        // The speed below everything, the log on the right, the table in what is left with the person's controls under it.
        var buttonFont = _speeds.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var speeds = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var logWidth = Math.Min(fonts.Px(340), viewport.Width * 3 / 10);
        var log = new Rectangle(viewport.Right - margin - logWidth, hud.Bottom + margin, logWidth, speeds.Top - hud.Bottom - margin);
        DrawLog(batch, log);

        // A hand shown at the showdown is labelled wider than its seat: the table keeps clear of the log for it.
        var left = new Rectangle(viewport.X + margin, hud.Bottom + margin, log.Left - viewport.X - 4 * margin, speeds.Top - hud.Bottom - margin);
        var personsTurn = IsPersonsTurn;
        var movesFont = _moves.Width(fonts.Body) <= left.Width ? fonts.Body : fonts.Small;
        var moves = new Rectangle(left.X, left.Bottom - (personsTurn ? buttonsHeight : 0), left.Width, buttonsHeight);
        var area = new Rectangle(left.X, left.Y, left.Width, moves.Top - left.Y - margin);
        if (_hand is { } hand)
        {
            _view.BottomSeat = Math.Max(0, _person?.Seat ?? 0);
            _view.Draw(batch, area, hand, _names, _faceUp, _lastAction);
        }
        else
        {
            batch.DrawText(fonts.Body, T("Getting the bots ready"), area, Palette.Muted, Align.Center);
        }

        if (personsTurn)
        {
            _moves.Draw(batch, movesFont, moves);
        }

        _speeds.Draw(batch, buttonFont, speeds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _view.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string SpeedName(WatchSpeed speed) => speed switch
    {
        WatchSpeed.Pause => T("Pause"),
        WatchSpeed.Step => T("Step"),
        WatchSpeed.Normal => "1x",
        WatchSpeed.Fast => "4x",
        _ => T("Instant"),
    };

    private static BotAction ToAnswer(PokerAction move) => move switch
    {
        Fold => BotAction.Fold,
        Check => BotAction.Check,
        CallBet => BotAction.Call,
        RaiseTo raise => BotAction.Raise(raise.To),
        _ => BotAction.AllIn,
    };

    private void SetSpeed(WatchSpeed speed)
    {
        _speed = speed;
        if (speed is WatchSpeed.Normal or WatchSpeed.Fast)
        {
            _resume = speed;
        }

        _speeds.Set(
        [
            .. Enum.GetValues<WatchSpeed>().Select(s => new Button(SpeedName(s), () => SetSpeed(s), Highlight: s == _speed)),
            new Button(T("Back"), () => Scenes.Pop()),
        ]);
    }

    // P pauses and goes on again, N takes a step, 1 and 4 play at that speed, I plays the rest of the hand.
    private bool UpdateSpeedKeys(InputState input)
    {
        WatchSpeed? chosen =
            input.WasPressed(Keys.P) ? (_speed == WatchSpeed.Pause ? _resume : WatchSpeed.Pause)
            : input.WasPressed(Keys.N) ? WatchSpeed.Step
            : input.WasPressed(Keys.D1) ? WatchSpeed.Normal
            : input.WasPressed(Keys.D4) ? WatchSpeed.Fast
            : input.WasPressed(Keys.I) ? WatchSpeed.Instant
            : null;
        if (chosen is { } speed)
        {
            SetSpeed(speed);
        }

        return chosen is not null;
    }

    private bool IsTimeToStep()
    {
        switch (_speed)
        {
            case WatchSpeed.Pause:
                return false;
            case WatchSpeed.Instant:
                return true;
        }

        // The chips land before anything else happens; the person's turn comes without a wait.
        if (_view.IsAnimating)
        {
            return false;
        }

        var personNext = _person is { } person && _hand is { IsOver: false } hand && hand.CurrentSeat == person.Seat;
        return _speed == WatchSpeed.Step || personNext || _sinceStep >= (_speed == WatchSpeed.Fast ? FastDelay : NormalDelay);
    }

    private void UpdatePerson(InputState input)
    {
        if (!IsPersonsTurn)
        {
            return;
        }

        var hand = _hand!;
        var seat = _person!.Seat;
        _moves.Set(_controls.Buttons(hand, seat, Act));
        if (!_moves.Update(input))
        {
            _controls.Update(_context.Actions, input, hand, seat, Act);
        }
    }

    private void Act(PokerAction move)
    {
        if (IsPersonsTurn)
        {
            _person!.Act(ToAnswer(move));
        }
    }

    /// <summary>Takes in a finished step: the copy of the hand follows the table, the log and the view the events.</summary>
    private void Follow(IReadOnlyList<PokerEvent> events)
    {
        if (!ReferenceEquals(_table.Hand, _live))
        {
            // A new hand. No step is running, so the table's hand can be copied.
            _live = _table.Hand;
            _hand = _live!.Copy();
            _handNumber = _table.Hands.Count + 1;
            _faultsSeen = 0;
            _lastAction.Clear();
            _shown.Clear();
            _controls.Reset(_hand);
            Say(T("Hand {0}, dealer: {1}", _handNumber, _names[_hand.Button]));
        }
        else if (_hand is not null)
        {
            foreach (var acted in events.OfType<Acted>())
            {
                _hand.Apply(acted.Seat, acted.Action);
            }

            _hand.TakeEvents();
        }

        var faults = _table.HandFaults;
        var fresh = faults.Skip(_faultsSeen).ToList();
        _faultsSeen = faults.Count;
        foreach (var e in events)
        {
            Describe(e, fresh);
        }

        _view.Update(TimeSpan.Zero, events);
        if ((_speed == WatchSpeed.Step && events.Any(e => e is Acted)) || (_speed == WatchSpeed.Instant && _hand is { IsOver: true }))
        {
            SetSpeed(WatchSpeed.Pause);
        }
    }

    private void Describe(PokerEvent e, List<(int Seat, Fault Fault)> faults)
    {
        var sound = _speed != WatchSpeed.Instant;
        switch (e)
        {
            case BlindPosted b:
                _lastAction[b.Seat] = b.Big ? T("Big blind") : T("Small blind");
                break;
            case Acted a:
                if (a.Amount > 0 && sound)
                {
                    _context.Sounds.Play(Sound.Chips);
                }

                var label = HoldemText.Action(a);
                _lastAction[a.Seat] = label;
                var fault = faults.FindIndex(f => f.Seat == a.Seat) is var at and >= 0 ? faults[at].Fault : Fault.None;
                Say(fault == Fault.None ? $"{_names[a.Seat]}: {label}" : FaultLine(_names[a.Seat], fault, a.Action is Fold, fault == Fault.Gone ? _table.Results()[a.Seat].Problem : null), fault != Fault.None);
                break;
            case StreetDealt d:
                HoldemText.NewStreet(_lastAction);
                Say(T("{0}: {1}", HoldemText.StreetName(d.Street), string.Join(" ", d.Cards)));
                break;
            case HandShown s:
                _shown.Add(s.Seat);
                _lastAction[s.Seat] = HoldemText.Describe(s.Value);
                break;
            case PotWon w:
                if (sound)
                {
                    _context.Sounds.Play(Sound.Chips);
                }

                Say(w.Value is { } value
                    ? T("{0} wins {1}: {2}", _names[w.Seat], w.Amount, HoldemText.Describe(value))
                    : T("{0} takes {1}", _names[w.Seat], w.Amount));
                break;
        }
    }

    // A move the bot did not make itself: why (for a bot that is gone, what is wrong with it), and what was played for it
    // (a check when that was free, else a fold).
    private static string FaultLine(string name, Fault fault, bool folded, string? problem) => (fault, folded) switch
    {
        (Fault.Late, true) => T("{0}: late, folds", name),
        (Fault.Late, false) => T("{0}: late, checks", name),
        (Fault.Illegal, true) => T("{0}: illegal move, folds", name),
        (Fault.Illegal, false) => T("{0}: illegal move, checks", name),
        (_, true) when problem is not null => T("{0}: gone ({1}), folds", name, problem),
        (_, false) when problem is not null => T("{0}: gone ({1}), checks", name, problem),
        (_, true) => T("{0}: gone, folds", name),
        _ => T("{0}: gone, checks", name),
    };

    // The bots that could not get ready say so before the first hand.
    private void SayProblems()
    {
        foreach (var seat in _table.Results().Where(r => r.Problem is not null))
        {
            Say(T("{0} cannot play: {1}", seat.Name, seat.Problem!), fault: true);
        }
    }

    private void Say(string message, bool fault = false)
    {
        _log.Add((message, fault));
        if (_log.Count > MaxLog)
        {
            _log.RemoveAt(0);
        }
    }

    // The newest lines at the bottom, wrapped to the width; older ones go off the top.
    private void DrawLog(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var font = fonts.Small;
        var padding = fonts.Px(10);
        batch.FillRectangle(area, Palette.Panel);
        var line = (int)font.LineHeight + 2;
        var y = area.Bottom - padding;
        for (var i = _log.Count - 1; i >= 0 && y - line >= area.Y + padding; i--)
        {
            var (message, fault) = _log[i];
            var color = fault ? Problem : i == _log.Count - 1 ? Palette.Text : Palette.Muted;
            var parts = font.Wrap(message, area.Width - 2 * padding);
            for (var p = parts.Count - 1; p >= 0 && y - line >= area.Y + padding; p--)
            {
                y -= line;
                batch.DrawText(font, parts[p], new Vector2(area.X + padding, y), color, shadow: false);
            }
        }
    }

    /// <summary>Saves the match and opens its results in place of the table.</summary>
    private void Finish()
    {
        _finished = true;
        var match = MatchResult.FromTable(_table, DateTimeOffset.Now);
        string? path;
        try
        {
            path = MatchFile.Save(_workshop.MatchesFolder, match);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            path = null;
        }

        Scenes.Replace(_workshop.MatchResults(match, path));
    }

    /// <summary>
    /// Stops the match: a step being run ends (one waiting for the person at once, one waiting for a bot within the
    /// time limit) and the table, with the bots' processes, is disposed once nothing runs on it any more.
    /// </summary>
    private void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _cancel.Cancel();
        _person?.Dispose();
        var running = (Task?)_step ?? _starting;
        if (running is null || running.IsCompleted)
        {
            Release();
            return;
        }

        running.ContinueWith(
            finished =>
            {
                // Nobody waits for the step any more: its failure, if any, is looked at here so it is not left unobserved.
                _ = finished.Exception;
                Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    // Runs off the UI thread when the scene was left mid-step, where an exception would go unseen: the bots are
    // stopped as well as they can be.
    private void Release()
    {
        try
        {
            _table.Dispose();
        }
#pragma warning disable CA1031 // A seat failing to stop must not take the app down after the player has left.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            _cancel.Dispose();
        }
    }
}
