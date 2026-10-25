namespace Poker.Arena;

/// <summary>How one bot played over a number of hands. The rates are fractions from 0 to 1.</summary>
/// <param name="Hands">The hands it was dealt into.</param>
/// <param name="Vpip">The share of hands it voluntarily put chips in before the flop (beyond a forced blind).</param>
/// <param name="Pfr">The share of hands it raised before the flop.</param>
/// <param name="AggressionFactor">After the flop, its bets and raises divided by its calls; null when it never called, as
/// there is nothing to divide by (a bot that only bets is not passive).</param>
/// <param name="WentToShowdown">Of the hands it saw the flop in, the share it reached showdown in.</param>
/// <param name="WonAtShowdown">Of the hands it reached showdown in, the share it won something in.</param>
public sealed record BotStats(int Hands, double Vpip, double Pfr, double? AggressionFactor, double WentToShowdown, double WonAtShowdown);

/// <summary>Statistics over recorded hands, the numbers poker tools show for a player.</summary>
public static class PokerStats
{
    /// <summary>The statistics of the bot called <paramref name="name"/> over <paramref name="hands"/>; hands it was not dealt into do not count.</summary>
    public static BotStats For(IEnumerable<HandRecord> hands, string name)
    {
        ArgumentNullException.ThrowIfNull(hands);
        ArgumentNullException.ThrowIfNull(name);
        int dealt = 0, vpip = 0, pfr = 0, bets = 0, calls = 0, flops = 0, showdowns = 0, won = 0;
        foreach (var hand in hands)
        {
            var seat = Enumerable.Range(0, hand.Names.Count).FirstOrDefault(s => hand.Names[s] == name && hand.Holes[s].Count > 0, -1);
            if (seat < 0)
            {
                continue;
            }

            dealt++;
            var replay = new BettingReplay(hand);
            var mine = replay.Steps.Where(s => s.Action.Seat == seat).ToArray();
            var preflop = mine.Where(s => s.Action.Street == Street.Preflop).ToArray();
            if (preflop.Any(s => s.Kind is ActionKind.Call or ActionKind.Bet or ActionKind.Raise))
            {
                vpip++;
            }

            if (preflop.Any(s => s.Kind is ActionKind.Bet or ActionKind.Raise))
            {
                pfr++;
            }

            foreach (var step in mine.Where(s => s.Action.Street != Street.Preflop))
            {
                if (step.Kind is ActionKind.Bet or ActionKind.Raise)
                {
                    bets++;
                }
                else if (step.Kind == ActionKind.Call)
                {
                    calls++;
                }
            }

            if (hand.Board.Count >= 3 && !preflop.Any(s => s.Kind == ActionKind.Fold))
            {
                flops++;
                if (hand.Shown.Contains(seat))
                {
                    showdowns++;
                    if (HandHistoryText.Collected(hand, replay, seat) > 0)
                    {
                        won++;
                    }
                }
            }
        }

        return new BotStats(
            dealt,
            Rate(vpip, dealt),
            Rate(pfr, dealt),
            calls == 0 ? null : (double)bets / calls,
            Rate(showdowns, flops),
            Rate(won, showdowns));
    }

    private static double Rate(int part, int whole) => whole == 0 ? 0 : (double)part / whole;
}
