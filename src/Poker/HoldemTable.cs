namespace Poker;

/// <summary>
/// A sit-and-go table: everyone starts with the same chips, the button moves one seat each hand, the blinds
/// double every <see cref="HandsPerLevel"/> hands, players with no chips are out, and the last one with chips
/// wins.
/// </summary>
public sealed class HoldemTable
{
    private readonly int[] _stacks;
    private readonly DealSeed _seed;
    private readonly int _startingBlind;

    /// <param name="seed">The seed every hand's own is derived from (see <see cref="DealSeed.Hand"/>).</param>
    public HoldemTable(int players, DealSeed seed, int startingStack = 1000, int smallBlind = 10, int handsPerLevel = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startingStack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smallBlind);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(handsPerLevel);
        _stacks = [.. Enumerable.Repeat(startingStack, players)];
        _seed = seed;
        _startingBlind = smallBlind;
        HandsPerLevel = handsPerLevel;
        Button = players - 1;
    }

    public int Players => _stacks.Length;

    public int HandsPerLevel { get; }

    public int HandNumber { get; private set; }

    public int Button { get; private set; }

    /// <summary>The seed the current hand (number <see cref="HandNumber"/>) is shuffled from.</summary>
    public DealSeed HandSeed => _seed.Hand(HandNumber);

    /// <summary>The current hand, once <see cref="DealHand"/> has been called.</summary>
    public HoldemHand? Hand { get; private set; }

    public int SmallBlind => _startingBlind << Level;

    public int BigBlind => SmallBlind * 2;

    /// <summary>0 for the first <see cref="HandsPerLevel"/> hands, then 1, 2...</summary>
    public int Level => Math.Max(0, HandNumber - 1) / HandsPerLevel;

    /// <summary>Chips per seat between hands (during a hand, see the hand).</summary>
    public IReadOnlyList<int> Stacks => _stacks;

    public bool IsOut(int seat) => _stacks[seat] == 0 && (Hand is null || Hand.IsOver || !Hand.InHand(seat));

    /// <summary>The last player with chips, once only one is left.</summary>
    public int? Champion => _stacks.Count(s => s > 0) == 1 ? Array.FindIndex(_stacks, s => s > 0) : null;

    /// <summary>Settles the last hand and deals the next one, the button moving to the next player with chips.</summary>
    public HoldemHand DealHand()
    {
        if (Hand is { IsOver: false })
        {
            throw new InvalidOperationException("The current hand is still being played.");
        }

        Settle();
        if (Champion is not null)
        {
            throw new InvalidOperationException("The game is over.");
        }

        HandNumber++;
        do
        {
            Button = (Button + 1) % Players;
        }
        while (_stacks[Button] == 0);

        Hand = new HoldemHand(_stacks, Button, SmallBlind, BigBlind, HandSeed);
        return Hand;
    }

    /// <summary>Takes the finished hand's stacks as the table's.</summary>
    public void Settle()
    {
        if (Hand is { IsOver: true } hand)
        {
            for (var seat = 0; seat < Players; seat++)
            {
                _stacks[seat] = hand.Stack(seat);
            }
        }
    }
}
