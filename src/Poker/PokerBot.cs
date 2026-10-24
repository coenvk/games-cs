using Jentl.Cards;
using Jentl.Multiplayer;

namespace Poker;

/// <summary>
/// A poker opponent that estimates its chance of winning by dealing out the unknown cards many times
/// (Monte Carlo), then weighs that against the price of staying in. <paramref name="tightness"/> (0–1) makes it
/// fold more, <paramref name="aggression"/> (0–1) raise more and bigger, and <paramref name="bluff"/> is how
/// often it bets with nothing.
/// </summary>
public sealed class PokerBot(double tightness = 0.5, double aggression = 0.5, double bluff = 0.06, int samples = 150) : IBot<HoldemHand, PokerAction>
{
    public double Tightness { get; } = tightness;

    public double Aggression { get; } = aggression;

    public double Bluff { get; } = bluff;

    public PokerAction Choose(HoldemHand game, int seat, Random random)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(random);
        var moves = game.LegalMoves(seat);
        var opponents = Enumerable.Range(0, game.Seats).Count(s => s != seat && game.IsLive(s));
        var equity = Equity(game.Hole(seat), game.Board, opponents, samples, random);
        var toCall = game.ToCall(seat);
        var pot = game.Pot;
        var potOdds = toCall == 0 ? 0 : toCall / (double)(pot + toCall);

        // How much better than an even share of the pot this hand is: 1 is average at this table.
        var strength = equity * (opponents + 1);
        var raises = moves.Where(m => m is RaiseTo or AllIn).ToArray();
#pragma warning disable CA5394 // Game play, not security relevant.
        var roll = random.NextDouble();
#pragma warning restore CA5394

        if (raises.Length > 0 && strength > 1.7 - 0.5 * Aggression)
        {
            // Strong: raise, bigger the stronger; shove the monsters.
            if (equity > 0.85 || (strength > 2.4 && roll < Aggression))
            {
                return AllIn.Instance;
            }

            return Pick(raises, strength > 2 ? 2 : roll < Aggression ? 1 : 0);
        }

        if (toCall == 0)
        {
            return raises.Length > 0 && roll < Bluff ? Pick(raises, 1) : Check.Instance;
        }

        // Facing a bet: stay in when the odds pay for it.
        if (equity >= potOdds * (1 + Tightness * 0.6))
        {
            return moves.First(m => m is CallBet or AllIn);
        }

        return raises.Length > 0 && roll < Bluff / 2 ? Pick(raises, 1) : Fold.Instance;
    }

    /// <summary>
    /// The share of the pot the hole cards win on average against <paramref name="opponents"/> random hands,
    /// given the board so far, from <paramref name="samples"/> random deals of the unknown cards.
    /// </summary>
    public static double Equity(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, int opponents, int samples, Random random)
    {
        ArgumentNullException.ThrowIfNull(hole);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(random);
        if (opponents == 0)
        {
            return 1;
        }

        var known = hole.Concat(board).ToHashSet();
        var unknown = Deck.Standard52().Where(c => !known.Contains(c)).ToArray();
        var needed = 5 - board.Count + 2 * opponents;
        var mine = new Card[7];
        var theirs = new Card[7];
        var total = 0.0;
        for (var sample = 0; sample < samples; sample++)
        {
            // A partial shuffle: only the cards this deal needs.
#pragma warning disable CA5394 // Game play, not security relevant.
            for (var i = 0; i < needed; i++)
            {
                var j = random.Next(i, unknown.Length);
                (unknown[i], unknown[j]) = (unknown[j], unknown[i]);
            }
#pragma warning restore CA5394

            var next = 0;
            var fullBoard = board.Concat(unknown.Skip(next).Take(5 - board.Count)).ToArray();
            next += 5 - board.Count;
            mine[0] = hole[0];
            mine[1] = hole[1];
            fullBoard.CopyTo(mine, 2);
            var me = HandEvaluator.Evaluate(mine);
            var best = true;
            var ties = 1;
            for (var o = 0; o < opponents && best; o++)
            {
                theirs[0] = unknown[next++];
                theirs[1] = unknown[next++];
                fullBoard.CopyTo(theirs, 2);
                var them = HandEvaluator.Evaluate(theirs);
                if (them > me)
                {
                    best = false;
                }
                else if (them == me)
                {
                    ties++;
                }
            }

            total += best ? 1.0 / ties : 0;
        }

        return total / samples;
    }

    /// <summary>The raise at <paramref name="size"/> (0 smallest, 1 middle, 2 biggest), short of all in when it can.</summary>
    private static PokerAction Pick(PokerAction[] raises, int size)
    {
        var sized = raises.OfType<RaiseTo>().ToArray();
        return sized.Length == 0 ? raises[0] : sized[Math.Min(size, sized.Length - 1)];
    }
}
