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
                ? new HoldemHand(record.StartStacks, record.Button, record.SmallBlind, record.BigBlind, RecordedDeck(record))
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
    /// The deck that deals the record's cards in the engine's order: two rounds of one hole card each, starting left of
    /// the button, then the board; a hidden seat gets cards no one else has, and the unused cards follow.
    /// </summary>
    private static List<Card> RecordedDeck(HandRecord record)
    {
        var seats = record.StartStacks.Count;
        if (record.Holes.Count != seats)
        {
            throw new InvalidDataException($"Hand {record.Number}: the hole cards do not match the seats.");
        }

        var known = record.Holes.SelectMany(h => h).Concat(record.Board).ToList();
        if (known.Distinct().Count() != known.Count)
        {
            throw new InvalidDataException($"Hand {record.Number}: a card is dealt twice.");
        }

        var spare = new Queue<Card>(Deck.Standard52().Except(known));
        var holes = new IReadOnlyList<Card>[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var cards = record.Holes[seat];
            holes[seat] = record.StartStacks[seat] <= 0 ? []
                : cards.Count == 0 ? [spare.Dequeue(), spare.Dequeue()]
                : cards.Count == 2 ? cards
                : throw new InvalidDataException($"Hand {record.Number}: seat {seat} has {cards.Count} hole cards.");
        }

        var deck = new List<Card>();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 1; i <= seats; i++)
            {
                var seat = (record.Button + i) % seats;
                if (holes[seat].Count == 2)
                {
                    deck.Add(holes[seat][round]);
                }
            }
        }

        deck.AddRange(record.Board);
        deck.AddRange(spare);
        return deck;
    }
}
