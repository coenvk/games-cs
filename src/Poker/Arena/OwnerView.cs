using Jentl.Cards;

namespace Poker.Arena;

/// <summary>
/// What the owner of a bot may see of a match it played online: every action, the board, its own bot's hole cards and
/// the cards shown at showdown. Every other hole card stays on the server, and so do the seeds, since the cards could be
/// dealt again from them, and what an opponent's runner said went wrong, which may tell of its owner's computer.
/// </summary>
public static class OwnerView
{
    /// <summary>What an opponent's problem reads in an owner's view.</summary>
    public const string OpponentProblem = "had a problem";

    /// <summary>
    /// The match as <paramref name="bot"/>'s owner sees it: in every hand, the hole cards of the other seats are removed
    /// unless they were shown at showdown, and every seed (the match's, the batch's, each hand's) is zero. An opponent's
    /// problem reads <see cref="OpponentProblem"/>; the bot's own is kept. Everything else is unchanged.
    /// </summary>
    public static MatchResult For(MatchResult match, string bot)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(bot);
        return match with
        {
            Settings = match.Settings with { Seed = default },
            Batch = match.Batch is null ? null : match.Batch with { Seed = default },
            Results = [.. match.Results.Select(r => r.Problem is null || Same(r.Name, bot) ? r : r with { Problem = OpponentProblem })],
            Hands = [.. match.Hands.Select(hand => Hide(hand, bot))],
        };
    }

    private static HandRecord Hide(HandRecord hand, string bot) => hand with
    {
        Seed = default,
        Holes = [.. hand.Holes.Select((cards, seat) =>
            Same(hand.Names[seat], bot) || hand.Shown.Contains(seat)
                ? cards
                : (IReadOnlyList<Card>)[])],
    };

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
