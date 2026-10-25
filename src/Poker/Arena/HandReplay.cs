namespace Poker.Arena;

/// <summary>
/// Plays a recorded hand again on the engine: dealt from the record's seed with its stacks, button and blinds, the
/// same cards come out, and its actions are applied one by one.
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
            hand = new HoldemHand(record.StartStacks, record.Button, record.SmallBlind, record.BigBlind, record.Seed);
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
}
