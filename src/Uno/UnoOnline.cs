using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Multiplayer;
using Jentl.Net;

namespace Uno;

/// <summary>
/// A round of Uno as one seat may see it: their own hand, only the size of everyone else's, and the moves they
/// may make. Both the local table and the online server hand the screen one of these, so the screen never needs
/// (or gets) the hidden cards.
/// </summary>
public sealed record UnoView(
    int Seat,
    int RoundNumber,
    int Target,
    int CurrentSeat,
    int Direction,
    UnoCard Top,
    UnoColor Color,
    int DrawPileCount,
    UnoCard? Drawn,
    IReadOnlyList<UnoCard> Hand,
    IReadOnlyList<int> HandCounts,
    IReadOnlyList<int> Scores,
    int? Winner,
    int RoundPoints,
    int? Champion,
    IReadOnlyList<UnoMove> LegalMoves)
{
    public int Players => HandCounts.Count;

    public bool IsOver => Winner is not null;

    /// <summary>Whether this seat may play <paramref name="card"/> now (only the drawn card, after drawing).</summary>
    public bool CanPlay(UnoCard card) => LegalMoves.OfType<PlayCard>().Any(m => m.Card == card);

    /// <summary>What <paramref name="seat"/> sees of the match's current round.</summary>
    public static UnoView Of(UnoMatch match, int seat)
    {
        ArgumentNullException.ThrowIfNull(match);
        var round = match.Round;
        var seats = Enumerable.Range(0, match.Players).ToArray();
        return new UnoView(
            seat,
            match.RoundNumber,
            match.Target,
            round.CurrentSeat,
            round.Direction,
            round.Top,
            round.Color,
            round.DrawPileCount,
            round.CurrentSeat == seat ? round.Drawn : null,
            [.. round.Hand(seat)],
            [.. seats.Select(s => round.Hand(s).Count)],
            [.. match.Scores],
            round.Winner,
            round.Winner is { } winner ? seats.Where(s => s != winner).Sum(round.HandPoints) : 0,
            match.Champion,
            round.LegalMoves(seat));
    }

    /// <summary>Whether <paramref name="move"/> is one of <paramref name="legal"/>; calling Uno or not is the player's own business.</summary>
    public static bool IsLegal(UnoMove move, IReadOnlyList<UnoMove> legal) => move switch
    {
        PlayCard play => legal.OfType<PlayCard>().Any(l => l.Card == play.Card && l.Chosen == play.Chosen),
        _ => legal.Contains(move),
    };
}

/// <summary>What an online player sends: a move, or (once a round is over) "deal the next one" or "new match".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(UnoPlay), "move")]
[JsonDerivedType(typeof(UnoNextRound), "next-round")]
[JsonDerivedType(typeof(UnoNewMatch), "new-match")]
public abstract record UnoAction;

public sealed record UnoPlay(UnoMove Move) : UnoAction;

public sealed record UnoNextRound : UnoAction;

public sealed record UnoNewMatch : UnoAction;

/// <summary>What the server sends a seat: its view, everyone's names, and what happened since the last state.</summary>
public sealed record UnoOnlineState(UnoView View, IReadOnlyList<string> Names, IReadOnlyList<UnoEvent> Events);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UnoOnlineState))]
[JsonSerializable(typeof(UnoAction))]
internal sealed partial class UnoJson : JsonSerializerContext;

/// <summary>Uno's online messages as JSON, for the room server's <see cref="PlayerAction"/> and <see cref="GameState"/>.</summary>
public static class UnoOnline
{
    /// <summary>The game's name on a room server.</summary>
    public const string Game = "uno";

    public static JsonElement Write(UnoAction action) => JsonSerializer.SerializeToElement(action, UnoJson.Default.UnoAction);

    public static JsonElement Write(UnoOnlineState state) => JsonSerializer.SerializeToElement(state, UnoJson.Default.UnoOnlineState);

    public static UnoAction? ReadAction(JsonElement json) => Read(() => json.Deserialize(UnoJson.Default.UnoAction));

    public static UnoOnlineState? ReadState(JsonElement json) => Read(() => json.Deserialize(UnoJson.Default.UnoOnlineState));

    private static T? Read<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// Uno on a room server: the match lives here and nowhere else. Each seat's actions are checked against the
/// rules, bots play their seats (and the seats of people who leave), and each seat is told only what it may see.
/// A finished round deals the next after a pause, or sooner when someone asks.
/// </summary>
public sealed class UnoRoomGame : IRoomGame
{
    private const int KeptEvents = 200;
    private static readonly TimeSpan NextRoundAfter = TimeSpan.FromSeconds(15);

    private readonly List<(int Version, UnoEvent Event)> _events = [];
    private readonly IBot<UnoGame, UnoMove> _bot = new SmartUnoBot();
    private UnoMatch? _match;
    private Match<UnoGame, UnoMove>? _runner;
    private Player[] _players = [];
    private int _seed;
    private TimeSpan _roundOverFor;

    public int MinSeats => 2;

    public int MaxSeats => 4;

    public int Version { get; private set; }

    /// <summary>The match, for tests and tools; null before the start.</summary>
    public UnoMatch? Match => _match;

    public void Start(IReadOnlyList<SeatInfo> seats, int seed)
    {
        ArgumentNullException.ThrowIfNull(seats);
        _players = [.. seats.Select(s => new Player(s.Seat, s.Name, s.Kind == SeatKind.Human ? PlayerKind.Human : PlayerKind.Bot))];
        _seed = seed;
        _match = new UnoMatch(_players.Length, seed);
        NewRunner();
        Collect();
    }

    public string? Act(int seat, JsonElement action)
    {
        if (_match is not { } match)
        {
            return "The game hasn't started.";
        }

        var round = match.Round;
        switch (UnoOnline.ReadAction(action))
        {
            case UnoPlay { Move: { } move }:
                if (round.IsOver)
                {
                    return "The round is over.";
                }

                if (round.CurrentSeat != seat)
                {
                    return "It's not your turn.";
                }

                if (!UnoView.IsLegal(move, round.LegalMoves(seat)))
                {
                    return "You can't do that now.";
                }

                round.Apply(seat, move);
                break;
            case UnoNextRound when round.IsOver && match.Champion is null:
                NextRound();
                break;
            case UnoNewMatch when match.Champion is not null:
                _seed++;
                _match = new UnoMatch(_players.Length, _seed);
                _events.Clear();
                NewRunner();
                break;
            case UnoNextRound or UnoNewMatch:
                return "Not now.";
            default:
                return "That isn't an Uno action.";
        }

        Collect();
        return null;
    }

    public void Update(TimeSpan elapsed)
    {
        if (_match is not { } match || _runner is null)
        {
            return;
        }

        if (match.Round.IsOver)
        {
            _roundOverFor += elapsed;
            if (match.Champion is null && _roundOverFor >= NextRoundAfter)
            {
                NextRound();
                Collect();
            }

            return;
        }

        if (_runner.Update(elapsed) is not null)
        {
            Collect();
        }
    }

    public void Replace(int seat)
    {
        if (seat < 0 || seat >= _players.Length || !_players[seat].IsHuman)
        {
            return;
        }

        _players[seat] = new Player(seat, $"{_players[seat].Name} (bot)", PlayerKind.Bot);
        NewRunner();
        Version++;
    }

    public JsonElement StateFor(int seat, int since)
    {
        if (_match is not { } match)
        {
            throw new InvalidOperationException("The game hasn't started.");
        }

        var events = _events.Where(e => e.Version > since).Select(e => e.Event).ToList();
        return UnoOnline.Write(new UnoOnlineState(UnoView.Of(match, seat), [.. _players.Select(p => p.Name)], events));
    }

    private void NextRound()
    {
        _match!.NextRound();
        _roundOverFor = TimeSpan.Zero;
        NewRunner();
    }

    private void NewRunner() => _runner = new Match<UnoGame, UnoMove>(_match!.Round, _players, _ => _bot, unchecked(_seed + _match.RoundNumber));

    /// <summary>Moves the round's new events into the log, scores a finished round, and marks a new version.</summary>
    private void Collect()
    {
        Version++;
        foreach (var e in _match!.Round.TakeEvents())
        {
            _events.Add((Version, e));
        }

        if (_events.Count > KeptEvents)
        {
            _events.RemoveRange(0, _events.Count - KeptEvents);
        }

        if (_match.Round.IsOver)
        {
            _match.ScoreRound();
        }
    }
}
