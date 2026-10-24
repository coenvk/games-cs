using Jentl.Multiplayer;
using Jentl.Net;
using Uno;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Where an Uno game lives, as the table screen sees it: on this device (with bots and pass-and-play), or on a
/// server. Either way the screen draws a <see cref="UnoView"/> and sends moves; it never holds hidden cards.
/// </summary>
internal interface IUnoTable : IDisposable
{
    /// <summary>"You vs 3 bots", "Online room 4821".</summary>
    string Name { get; }

    IReadOnlyList<string> Names { get; }

    /// <summary>The game from the seat at the bottom of the screen; null until an online game's first state arrives.</summary>
    UnoView? View { get; }

    /// <summary>Whether the bottom seat's cards may be shown (not while the device is being passed on).</summary>
    bool HandVisible { get; }

    /// <summary>It is the bottom seat's turn and they can act.</summary>
    bool CanAct { get; }

    /// <summary>The next person must take the device before their hand is shown (pass and play).</summary>
    bool AwaitingHandover { get; }

    bool IsOnline { get; }

    /// <summary>Something to tell the player about the table itself ("Connection lost"), if anything.</summary>
    string? Problem { get; }

    /// <summary>The table can't go on: the connection or the room is gone.</summary>
    bool IsGone { get; }

    void ConfirmHandover();

    void Play(UnoMove move);

    /// <summary>Lets time pass: bots move, network messages arrive.</summary>
    void Update(TimeSpan elapsed);

    /// <summary>What happened since the last call, for the screen to animate and tell.</summary>
    IReadOnlyList<UnoEvent> TakeEvents();

    /// <summary>Deals the next round once one is over.</summary>
    void NextRound();
}

/// <summary>Uno on this device: people and bots by <see cref="UnoSetup"/>.</summary>
internal sealed class LocalUnoTable : IUnoTable
{
    private readonly IBot<UnoGame, UnoMove> _bot = new SmartUnoBot();
    private readonly List<UnoEvent> _events = [];
    private readonly int _seed;
    private Match<UnoGame, UnoMove> _runner;

    public LocalUnoTable(UnoSetup setup, int seed)
    {
        Setup = setup;
        _seed = seed;
        Match = new UnoMatch(setup.Players.Count, seed);
        _runner = NewRunner();
        Collect();
    }

    public UnoSetup Setup { get; }

    public UnoMatch Match { get; }

    public string Name => Setup.Name;

    public IReadOnlyList<string> Names => [.. Setup.Players.Select(p => p.Name)];

    /// <summary>The bottom seat: whoever holds the device, else whoever's turn it is, else the first person.</summary>
    public int BottomSeat => _runner.Viewer ?? (_runner.Current.IsHuman ? _runner.Current.Seat : Setup.Players.First(p => p.IsHuman).Seat);

    public UnoView View => UnoView.Of(Match, BottomSeat);

    public bool HandVisible => _runner.Viewer == BottomSeat;

    public bool CanAct => _runner.IsHumansTurn;

    public bool AwaitingHandover => _runner.AwaitingHandover;

    public bool IsOnline => false;

    public string? Problem => null;

    public bool IsGone => false;

    public void ConfirmHandover() => _runner.ConfirmHandover();

    public void Play(UnoMove move)
    {
        if (!_runner.IsHumansTurn)
        {
            return;
        }

        var seat = _runner.Current.Seat;
        _runner.Play(move);
        Collect();

        // Passing the device on: nobody else may see this hand.
        if (Match.Round.CurrentSeat != seat)
        {
            _runner.HideHands();
        }
    }

    public void Update(TimeSpan elapsed)
    {
        if (_runner.Update(elapsed) is not null)
        {
            Collect();
        }
    }

    public IReadOnlyList<UnoEvent> TakeEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    public void NextRound()
    {
        if (Match.Round.IsOver && Match.Champion is null)
        {
            Match.NextRound();
            _runner = NewRunner();
            Collect();
        }
    }

    public void Dispose()
    {
    }

    private Match<UnoGame, UnoMove> NewRunner() => new(Match.Round, Setup.Players, _ => _bot, unchecked(_seed + Match.RoundNumber));

    private void Collect()
    {
        _events.AddRange(Match.Round.TakeEvents());
        if (Match.Round.IsOver)
        {
            Match.ScoreRound();
        }
    }
}

/// <summary>Uno on a game server: this device holds one seat and shows what the server says it may see.</summary>
internal sealed class OnlineUnoTable(NetClient client, string code, int seat) : IUnoTable
{
    private readonly List<UnoEvent> _events = [];
    private string? _problem;
    private bool _closed;

    public string Code { get; } = code;

    public int Seat { get; } = seat;

    public string Name => T("Online room {0}", Code);

    public IReadOnlyList<string> Names { get; private set; } = [];

    public UnoView? View { get; private set; }

    public bool HandVisible => true;

    public bool CanAct => View is { IsOver: false } view && view.CurrentSeat == Seat;

    public bool AwaitingHandover => false;

    public bool IsOnline => true;

    public string? Problem => client.Status == NetStatus.Closed ? client.Error ?? T("Connection lost.") : _problem;

    public bool IsGone => _closed || client.Status == NetStatus.Closed;

    public void ConfirmHandover()
    {
    }

    public void Play(UnoMove move)
    {
        if (CanAct)
        {
            client.Send(new PlayerAction(UnoOnline.Write(new UnoPlay(move))));
        }
    }

    public void Update(TimeSpan elapsed)
    {
        while (client.TryReceive(out var message))
        {
            switch (message)
            {
                case GameState { State: var json } when UnoOnline.ReadState(json) is { } state:
                    View = state.View;
                    Names = [.. state.Names.Select((name, s) => s == Seat ? T("You") : name)];
                    _events.AddRange(state.Events);
                    _problem = null;
                    break;
                case ServerError error:
                    _problem = error.Text;
                    break;
                case RoomClosed closed:
                    _problem = closed.Reason;
                    _closed = true;
                    break;
            }
        }
    }

    public IReadOnlyList<UnoEvent> TakeEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    public void NextRound() => client.Send(new PlayerAction(UnoOnline.Write(View?.Champion is null ? new UnoNextRound() : new UnoNewMatch())));

    public void Dispose()
    {
        client.Send(new LeaveRoom());
        client.Dispose();
    }
}
