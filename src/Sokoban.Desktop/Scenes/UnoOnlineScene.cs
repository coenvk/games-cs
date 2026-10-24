using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Uno;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// The online lobby: connects to the game server, then creates a room (and shows its code to share) or joins
/// one by its code. In a room it lists who has come; the host starts, and bots take the empty seats.
/// </summary>
internal sealed class UnoOnlineScene : PanelScene
{
    private readonly NetClient _client;
    private string? _problem;
    private RoomJoined? _room;
    private IReadOnlyList<SeatInfo> _seats = [];
    private bool _handedOver;
    private Stage _stage = Stage.Connecting;

    public UnoOnlineScene(GameContext context)
        : base(context)
    {
        Server = context.GameServer;
        _client = new NetClient(Server);
        Menu = BuildMenu();
    }

    private enum Stage
    {
        Connecting,
        Choosing,
        Waiting,
        InRoom,
    }

    public Uri Server { get; }

    protected override string Title => _room is { } room ? $"Room {room.Code}" : "Uno online";

    protected override IReadOnlyList<PanelLine> Lines
    {
        get
        {
            var lines = new List<PanelLine>();
            switch (_stage)
            {
                case Stage.Connecting:
                    lines.Add(new($"Connecting to {Server.Host}…", Palette.Muted));
                    break;
                case Stage.Choosing:
                    lines.Add(new("Make a room and share its code, or join a friend's.", Palette.Muted));
                    break;
                case Stage.Waiting:
                    lines.Add(new("One moment…", Palette.Muted));
                    break;
                case Stage.InRoom:
                    lines.Add(new(_room!.IsHost ? "Share the code; start when everyone is in." : "Waiting for the host to start.", Palette.Muted));
                    lines.AddRange(_seats.Select(s => new PanelLine(
                        s.Kind == SeatKind.Open ? "(open: a bot plays if nobody joins)" : s.Seat == _room.Seat ? $"{s.Name} (you)" : s.Name,
                        s.Kind == SeatKind.Open ? Palette.Muted : Palette.Text)));
                    break;
            }

            if (_problem is not null)
            {
                lines.Add(new(_problem, Palette.Accent));
            }

            return lines;
        }
    }

    /// <summary>Asks to join the room with this code (from the keypad).</summary>
    public void Join(string code)
    {
        _client.Send(new JoinRoom(code));
        Wait();
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        Receive();
        if (_handedOver)
        {
            return;
        }

        base.Update(elapsed, input);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_handedOver)
        {
            _client.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Receive()
    {
        if (_stage == Stage.Connecting && _client.Status == NetStatus.Connected)
        {
            Go(Stage.Choosing);
        }

        if (_client.Status == NetStatus.Closed && _problem != _client.Error)
        {
            _problem = $"{_client.Error} Is the server at {Server} running?";
            Menu = new Menu(Context.Sounds, new MenuItem("Back", () => Scenes.Pop()));
        }

        while (!_handedOver && _client.TryReceive(out var message))
        {
            switch (message)
            {
                case RoomJoined joined:
                    _room = joined;
                    _problem = null;
                    Go(Stage.InRoom);
                    break;
                case LobbyChanged lobby:
                    _seats = lobby.Seats;
                    break;
                case ServerError error:
                    _problem = error.Text;
                    Go(_room is null ? Stage.Choosing : Stage.InRoom);
                    break;
                case RoomClosed closed:
                    _problem = closed.Reason;
                    _room = null;
                    _seats = [];
                    Go(Stage.Choosing);
                    break;
                case GameStarted:
                    // The table takes the connection from here; its states follow in the queue.
                    _handedOver = true;
                    var table = new OnlineUnoTable(_client, _room!.Code, _room.Seat);
                    Scenes.Replace(new UnoScene(Context, table), Navigation.Fade());
                    break;
            }
        }
    }

    private void Go(Stage stage)
    {
        if (_stage != stage)
        {
            _stage = stage;
            Menu = BuildMenu();
        }
    }

    private void Wait()
    {
        _problem = null;
        Go(Stage.Waiting);
    }

    private Menu BuildMenu()
    {
        var sounds = Context.Sounds;
        var back = new MenuItem("Back", () => Scenes.Pop());
        return _stage switch
        {
            Stage.Choosing => new Menu(
                sounds,
                new MenuItem("New room for 2", () => Create(2)),
                new MenuItem("New room for 3", () => Create(3)),
                new MenuItem("New room for 4", () => Create(4)),
                new MenuItem("Join a room", () => Scenes.Push(new UnoJoinScene(Context, this))),
                back),
            Stage.InRoom when _room!.IsHost => new Menu(sounds, new MenuItem("Start", Start), new MenuItem("Leave", Leave)),
            Stage.InRoom => new Menu(sounds, new MenuItem("Leave", Leave)),
            _ => new Menu(sounds, back),
        };
    }

    private void Create(int seats)
    {
        _client.Send(new CreateRoom(UnoOnline.Game, seats));
        Wait();
    }

    private void Start()
    {
        _client.Send(new StartGame());
        Wait();
    }

    private void Leave()
    {
        _client.Send(new LeaveRoom());
        _room = null;
        _seats = [];
        _problem = null;
        Go(Stage.Choosing);
    }
}

/// <summary>A keypad for a four-digit room code: tap the keys, type digits, or move with arrows and press.</summary>
internal sealed class UnoJoinScene(GameContext context, UnoOnlineScene lobby) : Scene
{
    private const int Length = 4;
    private static readonly string[] Keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "Del", "0", "Join"];
    private static readonly Microsoft.Xna.Framework.Input.Keys[] DigitKeys =
    [
        Microsoft.Xna.Framework.Input.Keys.D0, Microsoft.Xna.Framework.Input.Keys.D1, Microsoft.Xna.Framework.Input.Keys.D2,
        Microsoft.Xna.Framework.Input.Keys.D3, Microsoft.Xna.Framework.Input.Keys.D4, Microsoft.Xna.Framework.Input.Keys.D5,
        Microsoft.Xna.Framework.Input.Keys.D6, Microsoft.Xna.Framework.Input.Keys.D7, Microsoft.Xna.Framework.Input.Keys.D8,
        Microsoft.Xna.Framework.Input.Keys.D9,
    ];

    private readonly Rectangle[] _buttons = new Rectangle[Keys.Length];
    private string _code = "";
    private int _focus;

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        for (var d = 0; d < 10; d++)
        {
            if (input.WasPressed(DigitKeys[d]) || input.WasPressed(Microsoft.Xna.Framework.Input.Keys.NumPad0 + d))
            {
                Press(d.ToString(CultureInfo.InvariantCulture));
                return;
            }
        }

        if (input.WasPressed(Microsoft.Xna.Framework.Input.Keys.Back))
        {
            Press("Del");
        }
        else if (actions.WasPressed(input, GameAction.Left))
        {
            _focus = _focus % 3 == 0 ? _focus + 2 : _focus - 1;
        }
        else if (actions.WasPressed(input, GameAction.Right))
        {
            _focus = _focus % 3 == 2 ? _focus - 2 : _focus + 1;
        }
        else if (actions.WasPressed(input, GameAction.Up))
        {
            _focus = (_focus + Keys.Length - 3) % Keys.Length;
        }
        else if (actions.WasPressed(input, GameAction.Down))
        {
            _focus = (_focus + 3) % Keys.Length;
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            Press(input.IsDown(Microsoft.Xna.Framework.Input.Keys.Enter) && _code.Length == Length ? "Join" : Keys[_focus]);
        }
        else if (input.Tap is { } tap)
        {
            for (var i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].Contains(tap))
                {
                    _focus = i;
                    Press(Keys[i]);
                    return;
                }
            }
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = context.Fonts;
        batch.FillRectangle(viewport, Palette.Dim);
        var key = Math.Min(fonts.Px(96), (viewport.Height - fonts.Px(260)) / 4);
        var gap = fonts.Px(10);
        var width = 3 * key + 2 * gap + 2 * fonts.Px(32);
        var height = fonts.Px(190) + 4 * key + 3 * gap;
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);
        batch.FillRectangle(panel, Palette.Panel);
        batch.DrawText(fonts.Heading, "Room code", new Vector2(panel.Center.X, panel.Y + fonts.Px(16)), Palette.Text, Align.Center);
        var shown = string.Join(" ", _code.PadRight(Length, '_').ToCharArray());
        batch.DrawText(fonts.Heading, shown, new Vector2(panel.Center.X, panel.Y + fonts.Px(84)), Palette.Accent, Align.Center);

        var left = panel.Center.X - (3 * key + 2 * gap) / 2;
        var top = panel.Y + fonts.Px(160);
        for (var i = 0; i < Keys.Length; i++)
        {
            var bounds = new Rectangle(left + i % 3 * (key + gap), top + i / 3 * (key + gap), key, key);
            _buttons[i] = bounds;
            var enabled = Keys[i] != "Join" || _code.Length == Length;
            batch.FillRectangle(bounds, i == _focus ? Palette.Accent : Palette.PanelLight);
            batch.DrawText(Keys[i].Length > 1 ? fonts.Small : fonts.Body, Keys[i], bounds, i == _focus ? Palette.Ink : enabled ? Palette.Text : Palette.Muted, shadow: false);
        }
    }

    private void Press(string key)
    {
        context.Sounds.Play(UiSounds.Navigate);
        switch (key)
        {
            case "Del":
                _code = _code.Length > 0 ? _code[..^1] : _code;
                break;
            case "Join" when _code.Length == Length:
                Scenes.Pop();
                lobby.Join(_code);
                break;
            case "Join":
                break;
            default:
                if (_code.Length < Length)
                {
                    _code += key;
                }

                if (_code.Length == Length)
                {
                    _focus = Keys.Length - 1;
                }

                break;
        }
    }
}
