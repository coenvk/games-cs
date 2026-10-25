using Poker;
using static Games.UI.Tr;

namespace Games.Poker;

/// <summary>What happens at a Hold'em table in words, in the player's language, for the log and the seats' labels.</summary>
internal static class HoldemText
{
    /// <summary>A move as a seat's label and in the log: "Fold", "Call 20", "Raise to 60".</summary>
    public static string Action(Acted acted)
    {
        ArgumentNullException.ThrowIfNull(acted);
        return acted.Action switch
        {
            Fold => T("Fold"),
            Check => T("Check"),
            CallBet => T("Call {0}", acted.Amount),
            AllIn => T("All in {0}", acted.Total),
            _ when acted.Opened => T("Bet {0}", acted.Total),
            _ => T("Raise to {0}", acted.Total),
        };
    }

    /// <summary>A new street: the seats' labels go, except folds and all-ins, which say what still holds.</summary>
    public static void NewStreet(Dictionary<int, string> lastAction)
    {
        ArgumentNullException.ThrowIfNull(lastAction);
        foreach (var seat in lastAction.Keys.Where(s => lastAction[s] != T("Fold") && !lastAction[s].StartsWith(T("All in"), StringComparison.Ordinal)).ToArray())
        {
            lastAction.Remove(seat);
        }
    }

    /// <summary>A poker hand in words, in the player's language: "Two pair, kings and fives".</summary>
    public static string Describe(HandValue value)
    {
        var r = value.Ranks;
        return value.Category switch
        {
            HandCategory.StraightFlush when r[0] == 14 => T("Royal flush"),
            HandCategory.StraightFlush => T("Straight flush, {0} high", RankName(r[0])),
            HandCategory.FourOfAKind => T("Four {0}", RankNames(r[0])),
            HandCategory.FullHouse => T("Full house, {0} over {1}", RankNames(r[0]), RankNames(r[1])),
            HandCategory.Flush => T("Flush, {0} high", RankName(r[0])),
            HandCategory.Straight => T("Straight, {0} high", RankName(r[0])),
            HandCategory.ThreeOfAKind => T("Three {0}", RankNames(r[0])),
            HandCategory.TwoPair => T("Two pair, {0} and {1}", RankNames(r[0]), RankNames(r[1])),
            HandCategory.Pair => T("Pair of {0}", RankNames(r[0])),
            _ => T("{0} high", RankName(r[0])),
        };
    }

    /// <summary>The flop, the turn or the river.</summary>
    public static string StreetName(Street street) => street switch
    {
        Street.Flop => T("Flop"),
        Street.Turn => T("Turn"),
        Street.River => T("River"),
        _ => street.ToString(),
    };

    private static string RankName(int rank) => rank switch
    {
        14 => T("ace"),
        13 => T("king"),
        12 => T("queen"),
        11 => T("jack"),
        10 => T("ten"),
        9 => T("nine"),
        8 => T("eight"),
        7 => T("seven"),
        6 => T("six"),
        5 => T("five"),
        4 => T("four"),
        3 => T("three"),
        _ => T("two"),
    };

    private static string RankNames(int rank) => rank switch
    {
        14 => T("aces"),
        13 => T("kings"),
        12 => T("queens"),
        11 => T("jacks"),
        10 => T("tens"),
        9 => T("nines"),
        8 => T("eights"),
        7 => T("sevens"),
        6 => T("sixes"),
        5 => T("fives"),
        4 => T("fours"),
        3 => T("threes"),
        _ => T("twos"),
    };
}
