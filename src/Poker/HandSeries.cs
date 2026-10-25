namespace Poker;

/// <summary>
/// A series of independent hands for comparing bots: every hand starts with each seat holding the same number
/// of big blinds, the blinds never change, the button moves one seat each hand, and the cards of hand
/// <c>i</c> depend only on the seed and <c>i</c>, so the same deal can be played again with the bots in other seats.
/// </summary>
public sealed class HandSeries
{
    private readonly int _seats;
    private readonly int _seed;
    private readonly int _stack;
    private readonly int _smallBlind;

    public HandSeries(int seats, int seed, int stackBigBlinds = 100, int smallBlind = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stackBigBlinds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smallBlind);
        _seats = seats;
        _seed = seed;
        _smallBlind = smallBlind;
        _stack = stackBigBlinds * BigBlind;
    }

    public int BigBlind => _smallBlind * 2;

    /// <summary>Hand number <paramref name="index"/> (counting from 0), with the button at seat <c>index % seats</c>.</summary>
    public HoldemHand Deal(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new HoldemHand(
            [.. Enumerable.Repeat(_stack, _seats)],
            index % _seats,
            _smallBlind,
            BigBlind,
            SeedOf(index));
    }

    /// <summary>The seed hand number <paramref name="index"/> is shuffled from.</summary>
    public int SeedOf(int index) => unchecked(_seed + index * 7919);
}
