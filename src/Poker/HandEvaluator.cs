using System.Numerics;
using Jentl.Cards;

namespace Poker;

public enum HandCategory
{
    HighCard,
    Pair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush,
}

/// <summary>
/// How strong a poker hand is: higher values win, equal values split. The best five cards decide: the
/// category first, then the ranks that matter for it, then kickers.
/// </summary>
public readonly record struct HandValue(HandCategory Category, int Score) : IComparable<HandValue>
{
    public static bool operator <(HandValue a, HandValue b) => a.Score < b.Score;

    public static bool operator >(HandValue a, HandValue b) => a.Score > b.Score;

    public static bool operator <=(HandValue a, HandValue b) => a.Score <= b.Score;

    public static bool operator >=(HandValue a, HandValue b) => a.Score >= b.Score;

    public int CompareTo(HandValue other) => Score.CompareTo(other.Score);

    /// <summary>The ranks that count, best first (ace is 14, or 5 for the top of a five-high straight).</summary>
    public IReadOnlyList<int> Ranks
    {
        get
        {
            var score = Score;
            return [.. Enumerable.Range(0, 5).Select(i => (score >> (16 - 4 * i)) & 0xF)];
        }
    }

    /// <summary>"Full house, kings over fives", "Pair of aces", "Straight, nine high"...</summary>
    public string Describe()
    {
        var r = Ranks;
        return Category switch
        {
            HandCategory.StraightFlush when r[0] == 14 => "Royal flush",
            HandCategory.StraightFlush => $"Straight flush, {Name(r[0])} high",
            HandCategory.FourOfAKind => $"Four {Plural(r[0])}",
            HandCategory.FullHouse => $"Full house, {Plural(r[0])} over {Plural(r[1])}",
            HandCategory.Flush => $"Flush, {Name(r[0])} high",
            HandCategory.Straight => $"Straight, {Name(r[0])} high",
            HandCategory.ThreeOfAKind => $"Three {Plural(r[0])}",
            HandCategory.TwoPair => $"Two pair, {Plural(r[0])} and {Plural(r[1])}",
            HandCategory.Pair => $"Pair of {Plural(r[0])}",
            _ => $"{Capital(Name(r[0]))} high",
        };
    }

    public override string ToString() => Describe();

    private static string Name(int rank) => rank switch
    {
        14 => "ace",
        13 => "king",
        12 => "queen",
        11 => "jack",
        10 => "ten",
        9 => "nine",
        8 => "eight",
        7 => "seven",
        6 => "six",
        5 => "five",
        4 => "four",
        3 => "three",
        _ => "two",
    };

    private static string Plural(int rank) => rank == 6 ? "sixes" : Name(rank) + "s";

    private static string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}

/// <summary>Scores the best five-card poker hand among five to seven cards, without trying every combination.</summary>
public static class HandEvaluator
{
    public static HandValue Evaluate(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        Span<int> counts = stackalloc int[15];
        Span<int> suits = stackalloc int[4];
        var all = 0;
        var total = 0;
        foreach (var card in cards)
        {
            var rank = PokerRank(card);
            counts[rank]++;
            suits[(int)card.Suit] |= 1 << rank;
            all |= 1 << rank;
            total++;
        }

        if (total is < 5 or > 7)
        {
            throw new ArgumentException("A poker hand is judged on five to seven cards.", nameof(cards));
        }

        foreach (var suit in suits)
        {
            if (BitOperations.PopCount((uint)suit) >= 5)
            {
                return StraightHigh(suit) is var high and > 0
                    ? Make(HandCategory.StraightFlush, high)
                    : Make(HandCategory.Flush, Top(suit, 5));
            }
        }

        Span<int> quads = stackalloc int[2];
        Span<int> trips = stackalloc int[3];
        Span<int> pairs = stackalloc int[4];
        int q = 0, t = 0, p = 0;
        for (var rank = 14; rank >= 2; rank--)
        {
            switch (counts[rank])
            {
                case 4:
                    quads[q++] = rank;
                    break;
                case 3:
                    trips[t++] = rank;
                    break;
                case 2:
                    pairs[p++] = rank;
                    break;
            }
        }

        if (q > 0)
        {
            return Make(HandCategory.FourOfAKind, quads[0], Highest(all, quads[0]));
        }

        if (t > 0 && (t > 1 || p > 0))
        {
            var over = t > 1 ? Math.Max(trips[1], p > 0 ? pairs[0] : 0) : pairs[0];
            return Make(HandCategory.FullHouse, trips[0], over);
        }

        if (StraightHigh(all) is var straight and > 0)
        {
            return Make(HandCategory.Straight, straight);
        }

        if (t > 0)
        {
            var kickers = Top(all & ~(1 << trips[0]), 2);
            return Make(HandCategory.ThreeOfAKind, [trips[0], .. kickers]);
        }

        if (p > 1)
        {
            return Make(HandCategory.TwoPair, pairs[0], pairs[1], Highest(all, pairs[0], pairs[1]));
        }

        if (p == 1)
        {
            return Make(HandCategory.Pair, [pairs[0], .. Top(all & ~(1 << pairs[0]), 3)]);
        }

        return Make(HandCategory.HighCard, Top(all, 5));
    }

    /// <summary>Ace counts 14 in poker.</summary>
    public static int PokerRank(Card card) => card.Rank == Card.Ace ? 14 : card.Rank;

    /// <summary>The top card of the best straight in a set of ranks, 5 for ace-to-five, or 0 for none.</summary>
    private static int StraightHigh(int ranks)
    {
        if ((ranks & (1 << 14)) != 0)
        {
            ranks |= 1 << 1; // the ace also plays low
        }

        for (var high = 14; high >= 5; high--)
        {
            var run = 0b11111 << (high - 4);
            if ((ranks & run) == run)
            {
                return high;
            }
        }

        return 0;
    }

    private static int[] Top(int ranks, int count)
    {
        var top = new int[count];
        var n = 0;
        for (var rank = 14; rank >= 2 && n < count; rank--)
        {
            if ((ranks & (1 << rank)) != 0)
            {
                top[n++] = rank;
            }
        }

        return top;
    }

    private static int Highest(int ranks, params int[] except)
    {
        foreach (var rank in except)
        {
            ranks &= ~(1 << rank);
        }

        return Top(ranks, 1)[0];
    }

    private static HandValue Make(HandCategory category, params int[] ranks)
    {
        var score = (int)category << 20;
        for (var i = 0; i < ranks.Length && i < 5; i++)
        {
            score |= ranks[i] << (16 - 4 * i);
        }

        return new HandValue(category, score);
    }
}
