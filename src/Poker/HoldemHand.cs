using Jentl.Cards;
using Jentl.Multiplayer;

namespace Poker;

public enum Street
{
    Preflop,
    Flop,
    Turn,
    River,
    Showdown,
}

/// <summary>A betting decision.</summary>
public abstract record PokerAction;

public sealed record Fold : PokerAction
{
    public static Fold Instance { get; } = new();
}

public sealed record Check : PokerAction
{
    public static Check Instance { get; } = new();
}

/// <summary>Matches the current bet (or puts in everything, if that is less).</summary>
public sealed record CallBet : PokerAction
{
    public static CallBet Instance { get; } = new();
}

/// <summary>Bets or raises to a total of <paramref name="To"/> on this street.</summary>
public sealed record RaiseTo(int To) : PokerAction;

/// <summary>Puts in every chip left: a call, a bet or a raise depending on the amount.</summary>
public sealed record AllIn : PokerAction
{
    public static AllIn Instance { get; } = new();
}

/// <summary>Something that happened in a hand, for the screen to show.</summary>
public abstract record PokerEvent;

public sealed record BlindPosted(int Seat, int Amount, bool Big) : PokerEvent;

/// <param name="Amount">Chips put in by the action.</param>
/// <param name="Total">The player's bet on this street afterwards.</param>
/// <param name="Opened">The first bet of the street (a bet rather than a raise).</param>
public sealed record Acted(int Seat, PokerAction Action, int Amount, int Total, bool Opened = false) : PokerEvent;

public sealed record StreetDealt(Street Street, IReadOnlyList<Card> Cards) : PokerEvent;

public sealed record HandShown(int Seat, IReadOnlyList<Card> Hole, HandValue Value) : PokerEvent;

/// <param name="Value">The winning hand, or null when everyone else folded.</param>
public sealed record PotWon(int Seat, int Amount, HandValue? Value) : PokerEvent;

/// <summary>
/// One hand of No-Limit Texas Hold'em: blinds, two hole cards each, four betting rounds around a five-card
/// board, and a showdown that splits the pot into side pots when players are all in for different amounts.
/// Seats with no chips sit the hand out. Minimum raises follow the usual rule (at least the size of the
/// last raise, and at least the big blind); a short all-in raise also reopens the betting (a simplification).
/// </summary>
public sealed class HoldemHand : ITurnBasedGame<PokerAction>
{
    private readonly SeatState[] _seats;
    private readonly List<Card> _deck;
    private readonly List<Card> _board = [];
    private readonly List<PokerEvent> _events = [];
    private readonly HashSet<int> _toAct = [];

    /// <param name="stacks">Chips per seat; 0 sits out.</param>
    /// <param name="button">The dealer seat (must have chips).</param>
    public HoldemHand(IReadOnlyList<int> stacks, int button, int smallBlind, int bigBlind, int seed)
        : this(stacks, button, smallBlind, bigBlind, Deck.Shuffled(Deck.Standard52(), new Random(seed)).Reverse().ToArray())
    {
    }

    /// <summary>A hand dealt from <paramref name="deck"/> in order (first card first), for tests.</summary>
    internal HoldemHand(IReadOnlyList<int> stacks, int button, int smallBlind, int bigBlind, IReadOnlyList<Card> deck)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smallBlind);
        ArgumentOutOfRangeException.ThrowIfLessThan(bigBlind, smallBlind);
        if (stacks.Count(s => s > 0) < 2)
        {
            throw new ArgumentException("A hand needs at least two players with chips.", nameof(stacks));
        }

        if (button < 0 || button >= stacks.Count || stacks[button] <= 0)
        {
            throw new ArgumentException("The button must be a player with chips.", nameof(button));
        }

        _seats = [.. stacks.Select(s => new SeatState { Stack = s, InHand = s > 0 })];
        Button = button;
        SmallBlind = smallBlind;
        BigBlind = bigBlind;
        _deck = [.. deck.Reverse()];

        // Two cards each, one at a time, starting left of the button.
        for (var round = 0; round < 2; round++)
        {
            foreach (var seat in Around(Button))
            {
                _seats[seat].Hole.Add(Take());
            }
        }

        var playing = Around(Button).ToArray();
        var small = playing.Length == 2 ? Button : NextInHand(Button);
        var big = NextInHand(small);
        SmallBlindSeat = small;
        BigBlindSeat = big;
        Put(small, Math.Min(smallBlind, _seats[small].Stack));
        _events.Add(new BlindPosted(small, _seats[small].Bet, Big: false));
        Put(big, Math.Min(bigBlind, _seats[big].Stack));
        _events.Add(new BlindPosted(big, _seats[big].Bet, Big: true));

        CurrentBet = bigBlind;
        MinRaise = bigBlind;
        Street = Street.Preflop;
        StartBetting(firstAfter: big);
    }

    public int Seats => _seats.Length;

    public int Button { get; }

    public int SmallBlind { get; }

    public int BigBlind { get; }

    public int SmallBlindSeat { get; }

    public int BigBlindSeat { get; }

    public Street Street { get; private set; }

    public IReadOnlyList<Card> Board => _board;

    public int CurrentSeat { get; private set; }

    /// <summary>The bet to match on this street.</summary>
    public int CurrentBet { get; private set; }

    /// <summary>The smallest raise allowed now (on top of <see cref="CurrentBet"/>).</summary>
    public int MinRaise { get; private set; }

    /// <summary>Every chip put in this hand.</summary>
    public int Pot => _seats.Sum(s => s.Committed);

    public bool IsOver { get; private set; }

    /// <summary>What each seat won (including chips returned to it), once the hand is over.</summary>
    public IReadOnlyDictionary<int, int> Winnings { get; private set; } = new Dictionary<int, int>();

    public int Stack(int seat) => _seats[seat].Stack;

    /// <summary>What the seat has bet on this street.</summary>
    public int Bet(int seat) => _seats[seat].Bet;

    public int Committed(int seat) => _seats[seat].Committed;

    public IReadOnlyList<Card> Hole(int seat) => _seats[seat].Hole;

    public bool InHand(int seat) => _seats[seat].InHand;

    public bool HasFolded(int seat) => _seats[seat].Folded;

    public bool IsAllIn(int seat) => _seats[seat].InHand && !_seats[seat].Folded && _seats[seat].Stack == 0;

    /// <summary>Still in the running for the pot.</summary>
    public bool IsLive(int seat) => _seats[seat].InHand && !_seats[seat].Folded;

    public int ToCall(int seat) => Math.Min(CurrentBet - _seats[seat].Bet, _seats[seat].Stack);

    /// <summary>The smallest total a bet or raise can go to now (an all-in may be less).</summary>
    public int MinRaiseTo => CurrentBet + MinRaise;

    /// <summary>The largest total <paramref name="seat"/> can bet: everything.</summary>
    public int MaxRaiseTo(int seat) => _seats[seat].Bet + _seats[seat].Stack;

    /// <summary>The events since the last call, emptying the list.</summary>
    public IReadOnlyList<PokerEvent> TakeEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    /// <summary>
    /// The choices: fold (when facing a bet), check or call, and when raising is possible a minimum raise, a
    /// half-pot and a pot-sized raise, and all in.
    /// </summary>
    public IReadOnlyList<PokerAction> LegalMoves(int seat)
    {
        if (IsOver || seat != CurrentSeat)
        {
            return [];
        }

        var state = _seats[seat];
        var toCall = CurrentBet - state.Bet;
        var moves = new List<PokerAction>();
        if (toCall > 0)
        {
            moves.Add(Fold.Instance);
            moves.Add(state.Stack > toCall ? CallBet.Instance : AllIn.Instance);
        }
        else
        {
            moves.Add(Check.Instance);
        }

        if (CanRaise(seat))
        {
            var max = MaxRaiseTo(seat);
            var pot = Pot + toCall;
            foreach (var to in new[] { MinRaiseTo, CurrentBet + toCall + pot / 2, CurrentBet + toCall + pot }.Distinct())
            {
                if (to >= MinRaiseTo && to < max)
                {
                    moves.Add(new RaiseTo(to));
                }
            }

            moves.Add(AllIn.Instance);
        }

        return [.. moves.Distinct()];
    }

    public void Apply(int seat, PokerAction move)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (IsOver || seat != CurrentSeat)
        {
            throw new InvalidOperationException($"It is not seat {seat}'s turn.");
        }

        var state = _seats[seat];
        var toCall = CurrentBet - state.Bet;
        var before = state.Bet;
        var opened = CurrentBet == 0;
        switch (move)
        {
            case Fold:
                state.Folded = true;
                break;
            case Check when toCall == 0:
                break;
            case Check:
                throw new InvalidOperationException("Cannot check facing a bet.");
            case CallBet when toCall > 0:
                Put(seat, Math.Min(toCall, state.Stack));
                break;
            case CallBet:
                throw new InvalidOperationException("There is nothing to call.");
            case AllIn:
                RaiseOrCall(seat, state.Bet + state.Stack);
                break;
            case RaiseTo raise:
                if (!CanRaise(seat) || raise.To <= CurrentBet || raise.To > MaxRaiseTo(seat)
                    || (raise.To < MinRaiseTo && raise.To != MaxRaiseTo(seat)))
                {
                    throw new InvalidOperationException($"Cannot raise to {raise.To} (minimum {MinRaiseTo}, maximum {MaxRaiseTo(seat)}).");
                }

                RaiseOrCall(seat, raise.To);
                break;
            default:
                throw new InvalidOperationException($"Unknown action {move}.");
        }

        _events.Add(new Acted(seat, move, state.Bet - before, state.Bet, opened && state.Bet > 0));
        _toAct.Remove(seat);
        Continue(seat);
    }

    private bool CanRaise(int seat)
    {
        var state = _seats[seat];

        // Raising needs chips beyond a call, and someone left who could answer it.
        return state.Stack > CurrentBet - state.Bet
            && Enumerable.Range(0, Seats).Any(s => s != seat && IsLive(s) && _seats[s].Stack > 0);
    }

    private void RaiseOrCall(int seat, int to)
    {
        Put(seat, to - _seats[seat].Bet);
        if (to > CurrentBet)
        {
            // A full raise sets the new minimum; every other player gets to answer it.
            MinRaise = Math.Max(MinRaise, to - CurrentBet);
            CurrentBet = to;
            _toAct.Clear();
            foreach (var other in Around(seat).Where(s => s != seat && IsLive(s) && _seats[s].Stack > 0))
            {
                _toAct.Add(other);
            }
        }
    }

    /// <summary>After an action: the next player, the next street, or the end of the hand.</summary>
    private void Continue(int seat)
    {
        if (Enumerable.Range(0, Seats).Count(IsLive) == 1)
        {
            Award();
            return;
        }

        if (_toAct.Count > 0)
        {
            CurrentSeat = Around(seat).First(_toAct.Contains);
            return;
        }

        // The street's betting is done.
        while (true)
        {
            foreach (var state in _seats)
            {
                state.Bet = 0;
            }

            CurrentBet = 0;
            MinRaise = BigBlind;
            if (Street == Street.River)
            {
                Award();
                return;
            }

            Street++;
            var cards = Street == Street.Flop ? new[] { Take(), Take(), Take() } : [Take()];
            _board.AddRange(cards);
            _events.Add(new StreetDealt(Street, cards));

            // With fewer than two players able to bet, the rest of the board is just dealt out.
            if (Enumerable.Range(0, Seats).Count(s => IsLive(s) && _seats[s].Stack > 0) >= 2)
            {
                StartBetting(firstAfter: Button);
                return;
            }
        }
    }

    private void StartBetting(int firstAfter)
    {
        _toAct.Clear();
        foreach (var seat in Enumerable.Range(0, Seats).Where(s => IsLive(s) && _seats[s].Stack > 0))
        {
            _toAct.Add(seat);
        }

        // Preflop, someone may already be all in for the blind and nobody else can act.
        if (_toAct.Count == 0 || (_toAct.Count == 1 && _seats[_toAct.First()].Bet >= CurrentBet))
        {
            _toAct.Clear();
            Continue(firstAfter);
            return;
        }

        CurrentSeat = Around(firstAfter).First(_toAct.Contains);
    }

    /// <summary>Ends the hand: the last player standing takes everything, otherwise the best hands share each pot.</summary>
    private void Award()
    {
        Street = Street.Showdown;
        IsOver = true;
        var winnings = new Dictionary<int, int>();
        var live = Enumerable.Range(0, Seats).Where(IsLive).ToArray();
        if (live.Length == 1)
        {
            winnings[live[0]] = Pot;
            _events.Add(new PotWon(live[0], Pot, null));
        }
        else
        {
            var values = live.ToDictionary(s => s, s => HandEvaluator.Evaluate(_seats[s].Hole.Concat(_board)));
            foreach (var seat in Around(Button).Where(IsLive))
            {
                _events.Add(new HandShown(seat, _seats[seat].Hole, values[seat]));
            }

            // Side pots: each level of commitment is a pot for those who put in at least that much.
            var levels = _seats.Select(s => s.Committed).Where(c => c > 0).Distinct().Order().ToArray();
            var previous = 0;
            foreach (var level in levels)
            {
                var slice = _seats.Sum(s => Math.Min(s.Committed, level) - Math.Min(s.Committed, previous));
                previous = level;
                var eligible = live.Where(s => _seats[s].Committed >= level).ToArray();
                if (eligible.Length == 0)
                {
                    // Chips put in by players who later folded, above every live player's stake.
                    eligible = [.. live.Where(s => _seats[s].Committed == live.Max(l => _seats[l].Committed))];
                }

                var best = eligible.Max(s => values[s]);
                var winners = Around(Button).Where(s => eligible.Contains(s) && values[s] == best).ToArray();
                for (var i = 0; i < winners.Length; i++)
                {
                    // Odd chips go to the first winners left of the button.
                    var share = slice / winners.Length + (i < slice % winners.Length ? 1 : 0);
                    winnings[winners[i]] = winnings.GetValueOrDefault(winners[i]) + share;
                }
            }

            foreach (var (seat, amount) in winnings.OrderBy(w => w.Key))
            {
                _events.Add(new PotWon(seat, amount, values[seat]));
            }
        }

        foreach (var (seat, amount) in winnings)
        {
            _seats[seat].Stack += amount;
        }

        Winnings = winnings;
    }

    private void Put(int seat, int amount)
    {
        var state = _seats[seat];
        state.Stack -= amount;
        state.Bet += amount;
        state.Committed += amount;
    }

    /// <summary>Seats in the hand, clockwise, starting after <paramref name="seat"/> and ending with it.</summary>
    private IEnumerable<int> Around(int seat) =>
        Enumerable.Range(1, Seats).Select(i => (seat + i) % Seats).Where(s => _seats[s].InHand);

    private int NextInHand(int seat) => Around(seat).First(s => s != seat);

    private Card Take()
    {
        var card = _deck[^1];
        _deck.RemoveAt(_deck.Count - 1);
        return card;
    }

    private sealed class SeatState
    {
        public int Stack { get; set; }

        public int Bet { get; set; }

        public int Committed { get; set; }

        public bool InHand { get; init; }

        public bool Folded { get; set; }

        public List<Card> Hole { get; } = [];
    }
}
