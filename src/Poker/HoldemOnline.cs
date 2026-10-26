using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Cards;
using Jentl.Multiplayer;
using Jentl.Net;
using Poker.Arena;
using PokerBots;

namespace Poker;

/// <summary>What an online player sends: a move, "deal the next hand" (once a hand is over) or "new game" (once the game is).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(HoldemMove), "move")]
[JsonDerivedType(typeof(HoldemNextHand), "next-hand")]
[JsonDerivedType(typeof(HoldemNewGame), "new-game")]
public abstract record HoldemOnlineAction;

/// <summary>A betting decision, in the words a bot uses: fold, check, call, raise (to <paramref name="To"/>) or all-in.</summary>
public sealed record HoldemMove(string Move, int? To = null) : HoldemOnlineAction;

public sealed record HoldemNextHand : HoldemOnlineAction;

public sealed record HoldemNewGame : HoldemOnlineAction;

/// <summary>A move made in the hand, by <paramref name="Seat"/>.</summary>
public sealed record HoldemPlay(int Seat, string Move, int? To = null);

/// <summary>
/// The current hand as one seat may see it: enough to deal it again on the engine and play it to where it stands
/// (see <see cref="HoldemOnline.Rebuild"/>). <paramref name="Holes"/> holds the seat's own cards and, once a hand ends in
/// a showdown, the cards shown; every other seat's are empty.
/// </summary>
/// <param name="StartStacks">Chips per seat before the blinds; 0 for a seat that is out.</param>
/// <param name="Holes">Card codes by seat (see <see cref="BotView.Code"/>).</param>
/// <param name="Board">The community cards dealt so far.</param>
/// <param name="Actions">Every move so far, in order (the blinds are not moves).</param>
public sealed record HoldemSeatHand(
    int Button,
    int SmallBlind,
    int BigBlind,
    IReadOnlyList<int> StartStacks,
    IReadOnlyList<IReadOnlyList<string>> Holes,
    IReadOnlyList<string> Board,
    IReadOnlyList<HoldemPlay> Actions);

/// <summary>What the server sends a seat: the hand as it may see it, everyone's names and the table's clocks.</summary>
/// <param name="TurnMilliseconds">While a person is to act: the time they have left before the server plays for them.</param>
/// <param name="NextHandMilliseconds">Once a hand is over and the game goes on: the time until the next is dealt.</param>
/// <param name="Ready">The seats that asked for the next hand.</param>
/// <param name="GameOver">Someone has every chip, or no person has any: a new game can start.</param>
public sealed record HoldemOnlineState(
    int Seat,
    IReadOnlyList<string> Names,
    int HandNumber,
    int HandsPerLevel,
    HoldemSeatHand Hand,
    int? TurnMilliseconds,
    int? NextHandMilliseconds,
    IReadOnlyList<int> Ready,
    int? Champion,
    bool GameOver);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HoldemOnlineState))]
[JsonSerializable(typeof(HoldemOnlineAction))]
internal sealed partial class HoldemJson : JsonSerializerContext;

/// <summary>Hold'em's online messages as JSON, for the room server's <see cref="PlayerAction"/> and <see cref="GameState"/>.</summary>
public static class HoldemOnline
{
    /// <summary>The game's name on a room server.</summary>
    public const string Game = "holdem";

    public static JsonElement Write(HoldemOnlineAction action) => JsonSerializer.SerializeToElement(action, HoldemJson.Default.HoldemOnlineAction);

    public static JsonElement Write(HoldemOnlineState state) => JsonSerializer.SerializeToElement(state, HoldemJson.Default.HoldemOnlineState);

    public static HoldemOnlineAction? ReadAction(JsonElement json) => Read(() => json.Deserialize(HoldemJson.Default.HoldemOnlineAction));

    /// <summary>The state in <paramref name="json"/>, or null when it isn't a whole one.</summary>
    public static HoldemOnlineState? ReadState(JsonElement json) =>
        Read(() => json.Deserialize(HoldemJson.Default.HoldemOnlineState)) is { Names: not null, Ready: not null, Hand: { StartStacks: not null, Holes: not null, Board: not null, Actions: not null } } state
            ? state
            : null;

    /// <summary>A move in the words of <see cref="HoldemMove"/>.</summary>
    public static (string Move, int? To) Move(PokerAction action) => action switch
    {
        Fold => ("fold", null),
        Check => ("check", null),
        CallBet => ("call", null),
        RaiseTo raise => ("raise", raise.To),
        AllIn => ("all-in", null),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };

    /// <summary>The engine's action for a played move, or null when it names none.</summary>
    public static PokerAction? Action(HoldemPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        return play.Move switch
        {
            "fold" => Fold.Instance,
            "check" => Check.Instance,
            "call" => CallBet.Instance,
            "raise" when play.To is { } to => new RaiseTo(to),
            "all-in" => AllIn.Instance,
            _ => null,
        };
    }

    /// <summary>
    /// The hand as it stood after its first <paramref name="actions"/> moves, dealt on this device from what the seat
    /// may see: the cards it doesn't know are stand-ins, to be drawn face down (<see cref="IsHidden"/>). It is the
    /// technique <see cref="HandReplay"/> uses for a downloaded match.
    /// </summary>
    /// <exception cref="InvalidDataException">The hand does not deal or play out on the engine.</exception>
    public static HoldemHand Rebuild(HoldemSeatHand hand, int actions)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentOutOfRangeException.ThrowIfNegative(actions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(actions, hand.Actions.Count);
        HoldemHand rebuilt;
        try
        {
            rebuilt = HandReplay.Deal(
                0,
                hand.StartStacks,
                hand.Button,
                hand.SmallBlind,
                hand.BigBlind,
                [.. hand.Holes.Select(h => (IReadOnlyList<Card>)[.. h.Select(BotView.Card)])],
                [.. hand.Board.Select(BotView.Card)]);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"The hand cannot be dealt: {e.Message}", e);
        }

        for (var i = 0; i < actions; i++)
        {
            var play = hand.Actions[i];
            if (Action(play) is not { } action || rebuilt.IsOver || rebuilt.CurrentSeat != play.Seat)
            {
                throw new InvalidDataException($"Move {i + 1} ({play.Seat}: {play.Move}) does not fit the hand.");
            }

            try
            {
                rebuilt.Apply(play.Seat, action);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException($"Move {i + 1} ({play.Seat}: {play.Move}): {e.Message}", e);
            }
        }

        return rebuilt;
    }

    /// <summary>Whether <paramref name="seat"/> was dealt in but its cards are not known: its rebuilt cards are stand-ins.</summary>
    public static bool IsHidden(HoldemSeatHand hand, int seat)
    {
        ArgumentNullException.ThrowIfNull(hand);
        return hand.StartStacks[seat] > 0 && hand.Holes[seat].Count == 0;
    }

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
/// A Hold'em sit-and-go on a room server: the table lives here and nowhere else. The engine checks each seat's moves,
/// house bots play their seats (and the seats of people who leave), a person who lets the minute run out checks or
/// folds, and each seat is told only what it may see. A finished hand deals the next after a pause, or sooner when
/// every person asks. The deal comes from a fresh secret seed for every game, kept on the server.
/// </summary>
public sealed class HoldemRoomGame : IRoomGame
{
    /// <summary>The chips everyone starts with.</summary>
    public const int StartingStack = 1000;

    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila", "Hugo", "Iris"];

    private readonly DealSeed? _seed;
    private readonly int _samples;
    private readonly List<HoldemPlay> _plays = [];
    private readonly HashSet<int> _ready = [];
    private Player[] _players = [];
    private PokerBot[] _bots = [];
    private int[] _startStacks = [];
    private Random _random = new(0);
    private TimeSpan _waited;
    private TimeSpan _overFor;

    public HoldemRoomGame()
        : this(null)
    {
    }

    /// <param name="seed">The deal of every game, for tests; null deals each game from a new secret seed.</param>
    /// <param name="samples">How many deals the bots try for each decision.</param>
    internal HoldemRoomGame(DealSeed? seed, int samples = 150)
    {
        _seed = seed;
        _samples = samples;
    }

    /// <summary>A person's time for a decision; then the server checks (when it is free) or folds for them.</summary>
    public static TimeSpan TurnLimit { get; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a bot "thinks" while a person is still in the hand.</summary>
    public static TimeSpan BotDelay { get; } = TimeSpan.FromMilliseconds(650);

    /// <summary>How long a bot "thinks" once no person is left in the hand.</summary>
    public static TimeSpan FastBotDelay { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The pause after a hand, unless every person asks for the next one sooner.</summary>
    public static TimeSpan NextHandAfter { get; } = TimeSpan.FromSeconds(8);

    public int MinSeats => 2;

    public int MaxSeats => 6;

    public int Version { get; private set; }

    /// <summary>The table, for tests and tools; null before the start.</summary>
    public HoldemTable? Table { get; private set; }

    /// <summary>Someone has every chip, or no person has any left: nothing more is dealt until a new game.</summary>
    public bool GameOver => Table is { } table
        && (table.Champion is not null || (table.Hand is { IsOver: true } && !_players.Any(p => p.IsHuman && table.Stacks[p.Seat] > 0)));

    /// <summary>Whether a bot plays <paramref name="seat"/>: it was empty at the start, or its person left.</summary>
    public bool IsBot(int seat) => !_players[seat].IsHuman;

    public void Start(IReadOnlyList<SeatInfo> seats, int seed)
    {
        ArgumentNullException.ThrowIfNull(seats);
        _players = [.. seats.Select(s => new Player(s.Seat, Name(s), s.Kind == SeatKind.Human ? PlayerKind.Human : PlayerKind.Bot))];

        // Each bot plays its own way, as at a table on the device: from tight and careful to loose and pushy.
        _bots = [.. Enumerable.Range(0, seats.Count).Select(i => new PokerBot(
            tightness: 0.25 + 0.15 * (i % 4),
            aggression: 0.7 - 0.12 * (i % 4),
            bluff: 0.04 + 0.03 * (i % 3),
            samples: _samples))];
        _random = new Random(seed);
        NewGame();
        Version++;
    }

    public string? Act(int seat, JsonElement action)
    {
        if (Table?.Hand is not { } hand)
        {
            return "The game hasn't started.";
        }

        switch (HoldemOnline.ReadAction(action))
        {
            case HoldemMove move:
                if (hand.IsOver)
                {
                    return "The hand is over.";
                }

                if (hand.CurrentSeat != seat)
                {
                    return "It's not your turn.";
                }

                if (move.Move is null || BotView.ToAction(new BotAction(move.Move, move.To), hand, seat, 0) is not { } chosen)
                {
                    return "You can't do that now.";
                }

                Play(seat, chosen);
                break;
            case HoldemNextHand when hand.IsOver && !GameOver:
                _ready.Add(seat);
                if (_players.Where(p => p.IsHuman).All(p => _ready.Contains(p.Seat)))
                {
                    NextHand();
                }

                break;
            case HoldemNewGame when GameOver:
                NewGame();
                break;
            case HoldemNextHand or HoldemNewGame:
                return "Not now.";
            default:
                return "That isn't a Hold'em action.";
        }

        Version++;
        return null;
    }

    public void Update(TimeSpan elapsed)
    {
        if (Table?.Hand is not { } hand || GameOver)
        {
            return;
        }

        if (hand.IsOver)
        {
            _overFor += elapsed;
            if (_overFor >= NextHandAfter)
            {
                NextHand();
                Version++;
            }

            return;
        }

        _waited += elapsed;
        var seat = hand.CurrentSeat;
        if (IsBot(seat))
        {
            var personIn = _players.Any(p => p.IsHuman && hand.IsLive(p.Seat));
            if (_waited >= (personIn ? BotDelay : FastBotDelay))
            {
                Play(seat, _bots[seat].Choose(hand, seat, _random));
                Version++;
            }
        }
        else if (_waited >= TurnLimit)
        {
            Play(seat, BotView.Fallback(hand, seat));
            Version++;
        }
    }

    public void Replace(int seat)
    {
        if (seat < 0 || seat >= _players.Length || !_players[seat].IsHuman)
        {
            return;
        }

        _players[seat] = new Player(seat, $"{_players[seat].Name} (bot)", PlayerKind.Bot);
        _ready.Remove(seat);
        Version++;
    }

    public JsonElement StateFor(int seat, int since)
    {
        if (Table?.Hand is not { } hand)
        {
            throw new InvalidOperationException("The game hasn't started.");
        }

        // Cards are shown at a showdown only: a hand won by everyone else folding shows nothing.
        var showdown = hand.IsOver && Enumerable.Range(0, hand.Seats).Count(hand.IsLive) > 1;
        var holes = Enumerable.Range(0, hand.Seats)
            .Select(s => s == seat || (showdown && hand.IsLive(s)) ? (IReadOnlyList<string>)[.. hand.Hole(s).Select(BotView.Code)] : [])
            .ToArray();
        var over = GameOver;
        var personToAct = !hand.IsOver && !IsBot(hand.CurrentSeat);
        return HoldemOnline.Write(new HoldemOnlineState(
            seat,
            [.. _players.Select(p => p.Name)],
            Table.HandNumber,
            Table.HandsPerLevel,
            new HoldemSeatHand(hand.Button, hand.SmallBlind, hand.BigBlind, _startStacks, holes, [.. hand.Board.Select(BotView.Code)], [.. _plays]),
            personToAct ? Milliseconds(TurnLimit - _waited) : null,
            hand.IsOver && !over ? Milliseconds(NextHandAfter - _overFor) : null,
            [.. _ready.Order()],
            Table.Champion,
            over));
    }

    /// <summary>A seat's name: the house bots get the names they have at a table on the device.</summary>
    private static string Name(SeatInfo seat) =>
        seat.Kind == SeatKind.Bot && seat.Seat >= 1 && seat.Name == string.Create(CultureInfo.InvariantCulture, $"Bot {seat.Seat + 1}")
            ? BotNames[(seat.Seat - 1) % BotNames.Length]
            : seat.Name;

    private static int Milliseconds(TimeSpan time) => (int)Math.Max(0, time.TotalMilliseconds);

    private void NewGame()
    {
        Table = new HoldemTable(_players.Length, _seed ?? DealSeed.New(), StartingStack);
        NextHand();
    }

    private void NextHand()
    {
        var hand = Table!.DealHand();
        hand.TakeEvents();
        _startStacks = [.. Table.Stacks];
        _plays.Clear();
        _ready.Clear();
        _waited = TimeSpan.Zero;
        _overFor = TimeSpan.Zero;
    }

    private void Play(int seat, PokerAction action)
    {
        var hand = Table!.Hand!;
        var (move, to) = HoldemOnline.Move(action);
        hand.Apply(seat, action);
        hand.TakeEvents();
        _plays.Add(new HoldemPlay(seat, move, to));
        _waited = TimeSpan.Zero;
        if (hand.IsOver)
        {
            Table.Settle();
            _overFor = TimeSpan.Zero;
            _ready.Clear();
        }
    }
}
