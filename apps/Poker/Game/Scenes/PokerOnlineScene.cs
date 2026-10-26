using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker;
using Games.Input;
using Games.Poker;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Hold'em in an online room: the server deals and decides, and this device holds one seat. Each state the server
/// sends is the hand as this seat may see it; the hand is dealt again here from it (the cards this seat may not see
/// are stand-ins, drawn face down) and drawn with the table and the controls of a game on the device. The person to
/// act has a minute, counted down for everyone; then the server checks or folds for them.
/// </summary>
internal sealed class PokerOnlineScene : Scene
{
    private const int HudHeight = 64;
    private const int LogLines = 2;

    private readonly GameContext _context;
    private readonly NetClient _client;
    private readonly ButtonBar _buttons = new();
    private readonly HoldemControls _controls = new();
    private readonly HoldemLog _log;
    private readonly PokerTableView _view;
    private readonly Func<int, bool> _faceUp;
    private HoldemOnlineState? _state;
    private HoldemHand? _hand;
    private string[] _names = [];
    private int _handNumber;
    private int _applied;
    private bool _waitingForServer;
    private TimeSpan? _turnLeft;
    private TimeSpan? _nextHandLeft;
    private int _shownSeconds = -1;
    private string _clock = "";
    private string? _problem;
    private bool _closed;
    private bool _goneShown;
    private bool _endShown;
    private FooterKind _footerKind;
    private Button[] _footer = [];

    private enum FooterKind
    {
        Menu,
        NextHand,
        NewGame,
    }

    public PokerOnlineScene(GameContext context, NetClient client, RoomJoined room)
    {
        ArgumentNullException.ThrowIfNull(room);
        _context = context;
        _client = client;
        Code = room.Code;
        Seat = room.Seat;
        _log = new HoldemLog(context);
        _view = new PokerTableView(context) { BottomSeat = Seat };
        _faceUp = seat => seat == Seat || (_state is { } state && seat < state.Hand.Holes.Count && state.Hand.Holes[seat].Count > 0);
    }

    public string Code { get; }

    public int Seat { get; }

    public HoldemOnlineState? State => _state;

    public IReadOnlyList<string> Names => _names;

    /// <summary>The chips <paramref name="seat"/> has as the hand stands.</summary>
    public int Stack(int seat) => _hand is { } hand && seat < hand.Seats ? hand.Stack(seat) : 0;

    /// <summary>Why the game cannot go on (the connection is gone or the room closed), or the server's last refusal.</summary>
    public string? Problem => _client.Status == NetStatus.Closed ? _client.Error ?? T("Connection lost.") : _problem;

    private bool IsGone => _closed || _client.Status == NetStatus.Closed;

    private bool MyTurn => !_waitingForServer && _hand is { IsOver: false } hand && hand.CurrentSeat == Seat && _state is { GameOver: false };

    /// <summary>Asks the server for a new game, once this one is over.</summary>
    public void NewGame()
    {
        _endShown = false;
        _client.Send(new PlayerAction(HoldemOnline.Write(new HoldemNewGame())));
    }

    /// <summary>The table and its connection are this screen's to close.</summary>
    public void Leave() => Navigation.ToMainMenu(Scenes, _context);

    /// <summary>Keeps talking to the server while a panel covers the table.</summary>
    public void KeepAlive(TimeSpan elapsed)
    {
        Receive();
        Tick(elapsed);
    }

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Table);

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"room {Code}  seat {Seat}  hand {_handNumber}  moves {_applied}"),
    ];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        Receive();
        Tick(elapsed);
        _view.Update(elapsed, []);
        if (IsGone && !_goneShown)
        {
            _goneShown = true;
            Scenes.Push(new PokerGoneScene(_context, this, Problem ?? T("The game has ended.")));
            return;
        }

        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Pause();
            return;
        }

        if (_state is not { } state || _hand is not { } hand)
        {
            return;
        }

        if (state.GameOver && !_endShown && !_view.IsAnimating)
        {
            _endShown = true;
            var won = state.Champion == Seat;
            _context.Sounds.Play(won ? Sound.Fanfare : Sound.Lose);
            if (won)
            {
                Celebration.Confetti(Scenes);
            }

            Scenes.Push(new PokerOnlineEndScene(_context, this));
            return;
        }

        if (MyTurn)
        {
            _buttons.Set(_controls.Buttons(hand, Seat, Act, new Button(T("Menu"), Pause)));
            if (!_buttons.Update(input))
            {
                _controls.Update(actions, input, hand, Seat, Act);
            }

            return;
        }

        _buttons.Set(Footer(state, hand));
        _buttons.Update(input);
        if (hand.IsOver && !state.GameOver && actions.WasPressed(input, GameAction.Confirm))
        {
            NextHand();
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
        if (_state is not { } state || _hand is not { } hand)
        {
            batch.DrawText(fonts.Body, T("Online room {0}", Code), text, Palette.Text, Align.Left);
            batch.DrawText(fonts.Body, Problem ?? T("Waiting for the deal…"), viewport, Palette.Muted);
            return;
        }

        batch.DrawText(fonts.Body, T("Room {0}: hand {1}", Code, state.HandNumber), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Small, T("blinds {0}/{1}, rising every {2} hands", hand.SmallBlind, hand.BigBlind, state.HandsPerLevel), text, Palette.Muted, Align.Right, shadow: false);

        // Footer: the clock and the log, then the buttons.
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var lineHeight = (int)fonts.Small.LineHeight;
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var logTop = buttons.Top - (LogLines + 1) * lineHeight - fonts.Px(6);
        if (_clock.Length > 0)
        {
            var urgent = _turnLeft is { TotalSeconds: <= 10 };
            batch.DrawText(fonts.Small, _clock, new Vector2(viewport.Center.X, logTop), urgent ? Palette.Accent : Palette.Text, Align.Center, shadow: false);
        }

        var lines = _log.Lines;
        var first = Math.Max(0, lines.Count - LogLines);
        for (var i = first; i < lines.Count; i++)
        {
            batch.DrawText(fonts.Small, lines[i], new Vector2(viewport.Center.X, logTop + (i - first + 1) * lineHeight), i == lines.Count - 1 ? Palette.Text : Palette.Muted, Align.Center, shadow: false);
        }

        // The table between the header and the messages, with this seat at the bottom.
        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - 2 * margin, logTop - hud.Bottom - 2 * margin);
        _view.Draw(batch, area, hand, _names, _faceUp, _log.LastAction);
        _buttons.Draw(batch, buttonFont, buttons);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.Dispose();
            _client.Send(new LeaveRoom());
            _client.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The buttons when it is not this seat's turn: Next hand or New game when they apply, and Menu; made again only when they change.</summary>
    private Button[] Footer(HoldemOnlineState state, HoldemHand hand)
    {
        var kind = state.GameOver ? FooterKind.NewGame : hand.IsOver && !state.Ready.Contains(Seat) ? FooterKind.NextHand : FooterKind.Menu;
        if (kind != _footerKind || _footer.Length == 0)
        {
            _footerKind = kind;
            var menu = new Button(T("Menu"), Pause);
            _footer = kind switch
            {
                FooterKind.NewGame => [new Button(T("New game"), NewGame, Highlight: true), menu],
                FooterKind.NextHand => [new Button(T("Next hand"), NextHand, Highlight: true), menu],
                _ => [menu],
            };
        }

        return _footer;
    }

    private void Pause() => Scenes.Push(new PokerOnlinePauseScene(_context, this));

    private void Act(PokerAction action)
    {
        if (!MyTurn)
        {
            return;
        }

        var (move, to) = HoldemOnline.Move(action);
        _client.Send(new PlayerAction(HoldemOnline.Write(new HoldemMove(move, to))));
        _waitingForServer = true;
    }

    private void NextHand()
    {
        if (_state is { GameOver: false } && _hand is { IsOver: true })
        {
            _client.Send(new PlayerAction(HoldemOnline.Write(new HoldemNextHand())));
        }
    }

    private void Receive()
    {
        while (_client.TryReceive(out var message))
        {
            switch (message)
            {
                case GameState { State: var json } when HoldemOnline.ReadState(json) is { } state:
                    Follow(state);
                    break;
                case ServerError error:
                    _problem = error.Text;
                    _waitingForServer = false;
                    _log.Say(error.Text);
                    break;
                case RoomClosed closed:
                    _problem = closed.Reason;
                    _closed = true;
                    break;
            }
        }
    }

    /// <summary>
    /// Takes the server's latest state: deals the hand again from it and plays the moves not yet shown, so the table
    /// moves on from where it was (or starts afresh on a new hand).
    /// </summary>
    private void Follow(HoldemOnlineState state)
    {
        _state = state;
        _waitingForServer = false;
        _problem = null;
        _names = [.. state.Names.Select((name, s) => s == Seat ? T("You") : name)];
        _turnLeft = state.TurnMilliseconds is { } turn ? TimeSpan.FromMilliseconds(turn) : null;
        _nextHandLeft = state.NextHandMilliseconds is { } next ? TimeSpan.FromMilliseconds(next) : null;
        _shownSeconds = -1;

        var fresh = _hand is null || state.HandNumber != _handNumber;
        var moves = state.Hand.Actions.Count;
        if (!fresh && moves == _applied)
        {
            return;
        }

        try
        {
            // The events up to the moves already shown come out the same, however much more is known now.
            var shown = fresh ? 0 : HoldemOnline.Rebuild(state.Hand, _applied).TakeEvents().Count;
            var hand = HoldemOnline.Rebuild(state.Hand, moves);
            var events = hand.TakeEvents().Skip(shown).ToArray();
            if (fresh)
            {
                _handNumber = state.HandNumber;
                _log.NewHand();
                _log.Say(T("Hand {0}, dealer: {1}", state.HandNumber, _names[hand.Button]));
                _controls.Reset(hand);
            }
            else
            {
                _view.Carry(_hand!, hand);
            }

            _hand = hand;
            _applied = moves;
            _log.Show(events, _names);
            _view.Update(TimeSpan.Zero, events);
        }
        catch (InvalidDataException)
        {
            _problem = T("The server's hand does not add up.");
            _log.Say(_problem);
        }
    }

    /// <summary>Counts the clocks down between the server's states, and puts the one that runs into words.</summary>
    private void Tick(TimeSpan elapsed)
    {
        _turnLeft = _turnLeft is { } turn ? Max(turn - elapsed) : null;
        _nextHandLeft = _nextHandLeft is { } next ? Max(next - elapsed) : null;
        var left = _turnLeft ?? _nextHandLeft;
        var seconds = left is { } time ? (int)Math.Ceiling(time.TotalSeconds) : -2;
        if (seconds == _shownSeconds)
        {
            return;
        }

        _shownSeconds = seconds;
        _clock = (_turnLeft, _nextHandLeft, _hand) switch
        {
            ({ }, _, { } hand) when hand.CurrentSeat == Seat => T("Your turn: {0} s", seconds),
            ({ }, _, { } hand) when hand.CurrentSeat < _names.Length => T("{0} to act: {1} s", _names[hand.CurrentSeat], seconds),
            (null, { }, _) => T("Next hand in {0} s", seconds),
            _ => "",
        };
    }

    private static TimeSpan Max(TimeSpan time) => time > TimeSpan.Zero ? time : TimeSpan.Zero;
}
