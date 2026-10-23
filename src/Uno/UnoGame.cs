using Jentl.Multiplayer;

namespace Uno;

/// <summary>What a player does on their turn.</summary>
public abstract record UnoMove;

/// <summary>
/// Plays a card. A wild needs <paramref name="Chosen"/>, the colour to play on. <paramref name="CallUno"/>
/// says "Uno!" with the second-to-last card; forgetting costs two cards.
/// </summary>
public sealed record PlayCard(UnoCard Card, UnoColor? Chosen = null, bool CallUno = false) : UnoMove;

/// <summary>Draws a card. If it can be played, the player may play it straight away (or pass); otherwise the turn ends.</summary>
public sealed record DrawCard : UnoMove
{
    public static DrawCard Instance { get; } = new();
}

/// <summary>Keeps the card just drawn and ends the turn.</summary>
public sealed record Pass : UnoMove
{
    public static Pass Instance { get; } = new();
}

/// <summary>Something that happened, for the screen to show.</summary>
public abstract record UnoEvent;

public sealed record CardPlayed(int Seat, UnoCard Card, UnoColor Color) : UnoEvent;

public sealed record CardsDrawn(int Seat, int Count) : UnoEvent;

public sealed record PlayerSkipped(int Seat) : UnoEvent;

public sealed record DirectionReversed(int Direction) : UnoEvent;

public sealed record UnoCalled(int Seat) : UnoEvent;

/// <summary>Down to one card without calling Uno: two cards drawn as a penalty.</summary>
public sealed record UnoForgotten(int Seat) : UnoEvent;

public sealed record DrawPileReshuffled : UnoEvent;

public sealed record RoundWon(int Seat, int Points) : UnoEvent;

/// <summary>
/// One round of Uno under the official basic rules. Seven cards each; match the top of the discard pile by
/// colour or by number or symbol, or play a wild. Skip, Reverse (a Skip with two players), Draw Two, Wild and
/// Wild Draw Four act as usual. No stacking of draw cards. A Wild Draw Four may only be played without a card
/// of the current colour (the rules enforce that instead of offering a challenge). A wild turned up to start
/// is shuffled back and another card turned. The first to empty their hand wins the round and scores the
/// cards left in the other hands.
/// </summary>
public sealed class UnoGame : ITurnBasedGame<UnoMove>
{
    public const int HandSize = 7;

    private readonly List<UnoCard>[] _hands;
    private readonly List<UnoCard> _drawPile;
    private readonly List<UnoCard> _discards = [];
    private readonly List<UnoEvent> _events = [];
    private readonly TurnOrder _order;
    private readonly Random _random;

    /// <param name="players">2 to 10.</param>
    /// <param name="dealer">Play starts to the dealer's left (the next seat).</param>
    public UnoGame(int players, int seed, int dealer = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, 10);
        _random = new Random(seed);
        _drawPile = [.. Shuffle(UnoCard.Deck())];
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<UnoCard>())];
        for (var round = 0; round < HandSize; round++)
        {
            foreach (var hand in _hands)
            {
                hand.Add(Take());
            }
        }

        dealer = dealer < 0 ? players - 1 : dealer % players;
        Dealer = dealer;
        _order = new TurnOrder(players, (dealer + 1) % players);

        // Turn up the first card; a wild goes back into the deck.
        var first = Take();
        while (first.IsWild)
        {
            _drawPile.Insert(_random.Next(_drawPile.Count + 1), first);
            first = Take();
        }

        _discards.Add(first);
        Color = first.Color!.Value;
        StartWith(first, dealer);
    }

    public int Players => _hands.Length;

    /// <summary>Who dealt; play starts on their left.</summary>
    public int Dealer { get; }

    public int CurrentSeat => _order.Current;

    /// <summary>1 clockwise, -1 counter-clockwise.</summary>
    public int Direction => _order.Direction;

    public UnoCard Top => _discards[^1];

    /// <summary>The colour to match: the top card's, or the one chosen for a wild.</summary>
    public UnoColor Color { get; private set; }

    public int DrawPileCount => _drawPile.Count;

    /// <summary>The card the current player just drew and may still play.</summary>
    public UnoCard? Drawn { get; private set; }

    public bool IsOver => Winner is not null;

    public int? Winner { get; private set; }

    /// <summary>What happened since <see cref="TakeEvents"/> was last called.</summary>
    public IReadOnlyList<UnoEvent> Events => _events;

    public IReadOnlyList<UnoCard> Hand(int seat) => _hands[seat];

    /// <summary>The events since the last call, emptying the list.</summary>
    public IReadOnlyList<UnoEvent> TakeEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    /// <summary>Whether <paramref name="card"/> may be played now by <paramref name="seat"/>.</summary>
    public bool CanPlay(int seat, UnoCard card)
    {
        if (card.Value == UnoValue.Wild)
        {
            return true;
        }

        if (card.Value == UnoValue.WildDrawFour)
        {
            return !_hands[seat].Any(c => !c.IsWild && c.Color == Color);
        }

        return card.Color == Color || card.Value == Top.Value;
    }

    public IReadOnlyList<UnoMove> LegalMoves(int seat)
    {
        if (IsOver || seat != CurrentSeat)
        {
            return [];
        }

        var calls = _hands[seat].Count == 2;
        IEnumerable<UnoCard> cards = Drawn is { } drawn ? [drawn] : _hands[seat];
        var moves = new List<UnoMove>();
        foreach (var card in cards.Where(c => CanPlay(seat, c)).Distinct())
        {
            if (card.IsWild)
            {
                moves.AddRange(Enum.GetValues<UnoColor>().Select(c => new PlayCard(card, c, calls)));
            }
            else
            {
                moves.Add(new PlayCard(card, null, calls));
            }
        }

        moves.Add(Drawn is null ? DrawCard.Instance : Pass.Instance);
        return moves;
    }

    public void Apply(int seat, UnoMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (IsOver || seat != CurrentSeat)
        {
            throw new InvalidOperationException($"It is not seat {seat}'s turn.");
        }

        switch (move)
        {
            case PlayCard play:
                Play(seat, play);
                break;
            case DrawCard when Drawn is null:
                var card = Draw(seat, 1).FirstOrDefault();
                if (card != default && CanPlay(seat, card))
                {
                    Drawn = card;
                }
                else
                {
                    _order.Advance();
                }

                break;
            case Pass when Drawn is not null:
                Drawn = null;
                _order.Advance();
                break;
            default:
                throw new InvalidOperationException($"{move} is not possible now.");
        }
    }

    /// <summary>What the cards left in a hand are worth.</summary>
    public int HandPoints(int seat) => _hands[seat].Sum(c => c.Points);

    private void Play(int seat, PlayCard play)
    {
        var hand = _hands[seat];
        var card = play.Card;
        if (!hand.Contains(card) || (Drawn is { } drawn && drawn != card) || !CanPlay(seat, card))
        {
            throw new InvalidOperationException($"{card} cannot be played now.");
        }

        if (card.IsWild && play.Chosen is null)
        {
            throw new InvalidOperationException("A wild needs a colour.");
        }

        Drawn = null;
        hand.Remove(card);
        _discards.Add(card);
        Color = card.IsWild ? play.Chosen!.Value : card.Color!.Value;
        _events.Add(new CardPlayed(seat, card, Color));

        if (hand.Count == 1)
        {
            if (play.CallUno)
            {
                _events.Add(new UnoCalled(seat));
            }
            else
            {
                _events.Add(new UnoForgotten(seat));
                Draw(seat, 2);
            }
        }

        // The card's effect happens even when it is the last one (the next player still draws).
        switch (card.Value)
        {
            case UnoValue.Skip:
                SkipNext();
                break;
            case UnoValue.Reverse when Players == 2:
                SkipNext();
                break;
            case UnoValue.Reverse:
                _order.Reverse();
                _events.Add(new DirectionReversed(_order.Direction));
                _order.Advance();
                break;
            case UnoValue.DrawTwo:
                Draw(_order.Next(), 2);
                SkipNext();
                break;
            case UnoValue.WildDrawFour:
                Draw(_order.Next(), 4);
                SkipNext();
                break;
            default:
                _order.Advance();
                break;
        }

        if (hand.Count == 0)
        {
            Winner = seat;
            _events.Add(new RoundWon(seat, Enumerable.Range(0, Players).Where(s => s != seat).Sum(HandPoints)));
        }
    }

    /// <summary>The first card's effect, as if the dealer had played it.</summary>
    private void StartWith(UnoCard first, int dealer)
    {
        switch (first.Value)
        {
            case UnoValue.Skip:
                _events.Add(new PlayerSkipped(_order.Current));
                _order.Advance();
                break;
            case UnoValue.Reverse when Players == 2:
                _events.Add(new PlayerSkipped(_order.Current));
                _order.Advance();
                break;
            case UnoValue.Reverse:
                // The dealer starts, and play goes the other way.
                _order.Reverse();
                _order.GoTo(dealer);
                _events.Add(new DirectionReversed(_order.Direction));
                break;
            case UnoValue.DrawTwo:
                Draw(_order.Current, 2);
                _events.Add(new PlayerSkipped(_order.Current));
                _order.Advance();
                break;
        }
    }

    private void SkipNext()
    {
        _events.Add(new PlayerSkipped(_order.Next()));
        _order.Advance(2);
    }

    private List<UnoCard> Draw(int seat, int count)
    {
        var drawn = new List<UnoCard>();
        for (var i = 0; i < count; i++)
        {
            if (_drawPile.Count == 0)
            {
                Reshuffle();
            }

            if (_drawPile.Count == 0)
            {
                break; // every card is in someone's hand
            }

            drawn.Add(Take());
        }

        _hands[seat].AddRange(drawn);
        if (drawn.Count > 0)
        {
            _events.Add(new CardsDrawn(seat, drawn.Count));
        }

        return drawn;
    }

    private void Reshuffle()
    {
        if (_discards.Count <= 1)
        {
            return;
        }

        var top = _discards[^1];
        _drawPile.AddRange(Shuffle(_discards.Take(_discards.Count - 1)));
        _discards.Clear();
        _discards.Add(top);
        _events.Add(new DrawPileReshuffled());
    }

    private UnoCard Take()
    {
        var card = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        return card;
    }

    private List<UnoCard> Shuffle(IEnumerable<UnoCard> cards)
    {
        var list = cards.ToList();
#pragma warning disable CA5394 // A card game, not security relevant.
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
#pragma warning restore CA5394

        return list;
    }
}
