using Jentl.Cards;

namespace Poker.Arena;

/// <summary>
/// Plays a recorded hand again on the engine: dealt from the record's seed with its stacks, button and blinds, the
/// same cards come out, and its actions are applied one by one. A record without a seed (a match downloaded from the
/// server, see <see cref="OwnerView"/>) is dealt its recorded hole cards and board instead; a seat whose cards were
/// kept from its owner gets stand-in cards, to be shown face down (<see cref="IsHidden"/>).
/// </summary>
public static class HandReplay
{
    /// <summary>The hand as it stood after its first <paramref name="actions"/> actions (0: just dealt, blinds posted).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="actions"/> is negative or more than the hand has.</exception>
    /// <exception cref="InvalidDataException">The record does not play out on the engine: an action out of turn or not allowed.</exception>
    public static HoldemHand Rebuild(HandRecord record, int actions)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfNegative(actions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(actions, record.Actions.Count);
        HoldemHand hand;
        try
        {
            hand = record.Seed == default
                ? Deal(record.Number, record.StartStacks, record.Button, record.SmallBlind, record.BigBlind, record.Holes, record.Board)
                : new HoldemHand(record.StartStacks, record.Button, record.SmallBlind, record.BigBlind, record.Seed);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"Hand {record.Number} cannot be dealt again: {e.Message}", e);
        }

        for (var i = 0; i < actions; i++)
        {
            var action = record.Actions[i];
            if (hand.IsOver || hand.CurrentSeat != action.Seat)
            {
                throw new InvalidDataException($"Hand {record.Number}, action {i + 1}: it is not seat {action.Seat}'s turn.");
            }

            try
            {
                hand.Apply(action.Seat, action.Action);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException($"Hand {record.Number}, action {i + 1} (seat {action.Seat}): {e.Message}", e);
            }
        }

        return hand;
    }

    /// <summary>
    /// Whether <paramref name="seat"/> was dealt in but its cards are not in the record: an opponent's cards that were not
    /// shown, in a match downloaded from the server. A replay deals it stand-in cards, which are to be shown face down.
    /// </summary>
    public static bool IsHidden(HandRecord record, int seat)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.StartStacks[seat] > 0 && record.Holes[seat].Count == 0;
    }

    /// <summary>
    /// A hand dealt the cards that are known: <paramref name="holes"/> by seat (empty for a seat whose cards are not
    /// known, which gets stand-in cards) and the <paramref name="board"/> so far, the rest of the deck being stand-ins.
    /// </summary>
    /// <exception cref="InvalidDataException">The cards make no deal: a card twice, or a seat with one card.</exception>
    /// <exception cref="ArgumentException">The stacks, button or blinds make no hand.</exception>
    internal static HoldemHand Deal(
        int number,
        IReadOnlyList<int> startStacks,
        int button,
        int smallBlind,
        int bigBlind,
        IReadOnlyList<IReadOnlyList<Card>> holes,
        IReadOnlyList<Card> board) =>
        new(startStacks, button, smallBlind, bigBlind, RecordedDeck(number, startStacks, button, holes, board));

    /// <summary>
    /// The deck that deals the known cards in the engine's order: two rounds of one hole card each, starting left of
    /// the button, then the board; a hidden seat gets cards no one else has, and the unused cards follow.
    /// </summary>
    private static List<Card> RecordedDeck(int number, IReadOnlyList<int> startStacks, int button, IReadOnlyList<IReadOnlyList<Card>> known, IReadOnlyList<Card> board)
    {
        var seats = startStacks.Count;
        if (known.Count != seats)
        {
            throw new InvalidDataException($"Hand {number}: the hole cards do not match the seats.");
        }

        var dealt = known.SelectMany(h => h).Concat(board).ToList();
        if (dealt.Distinct().Count() != dealt.Count)
        {
            throw new InvalidDataException($"Hand {number}: a card is dealt twice.");
        }

        var spare = new Queue<Card>(Deck.Standard52().Except(dealt));
        var holes = new IReadOnlyList<Card>[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var cards = known[seat];
            holes[seat] = startStacks[seat] <= 0 ? []
                : cards.Count == 0 ? [spare.Dequeue(), spare.Dequeue()]
                : cards.Count == 2 ? cards
                : throw new InvalidDataException($"Hand {number}: seat {seat} has {cards.Count} hole cards.");
        }

        var deck = new List<Card>();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 1; i <= seats; i++)
            {
                var seat = (button + i) % seats;
                if (holes[seat].Count == 2)
                {
                    deck.Add(holes[seat][round]);
                }
            }
        }

        deck.AddRange(board);
        deck.AddRange(spare);
        return deck;
    }
}
