using Jentl.Cards;

namespace Poker.Arena;

/// <summary>
/// What the owner of a bot may see of a match it played online: every action, the board, its own bot's hole cards and
/// the cards shown at showdown. Every other hole card stays on the server, and so do the seeds, since the cards could be
/// dealt again from them.
/// </summary>
public static class OwnerView
{
    /// <summary>
    /// The match as <paramref name="bot"/>'s owner sees it: in every hand, the hole cards of the other seats are removed
    /// unless they were shown at showdown, and every seed (the match's, the batch's, each hand's) is zero. Everything else is unchanged.
    /// </summary>
    public static MatchResult For(MatchResult match, string bot)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(bot);
        return match with
        {
            Settings = match.Settings with { Seed = default },
            Batch = match.Batch is null ? null : match.Batch with { Seed = default },
            Hands = [.. match.Hands.Select(hand => Hide(hand, bot))],
        };
    }

    private static HandRecord Hide(HandRecord hand, string bot) => hand with
    {
        Seed = default,
        Holes = [.. hand.Holes.Select((cards, seat) =>
            string.Equals(hand.Names[seat], bot, StringComparison.OrdinalIgnoreCase) || hand.Shown.Contains(seat)
                ? cards
                : (IReadOnlyList<Card>)[])],
    };
}
