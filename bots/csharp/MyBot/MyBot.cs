using PokerBots;

namespace MyBot;

/// <summary>
/// A small example bot to start from. Before the flop it raises with pairs, with two high cards and with an ace and a
/// suited card, and otherwise only pays a small price to see the flop. After the flop it raises when it has paired
/// the board (or holds a pocket pair) and otherwise calls only when the pot odds are good enough.
/// </summary>
/// <remarks>
/// The app runs one bot per DLL: keep exactly one public class with a public parameterless constructor that
/// implements <see cref="IPokerBot"/>. Only <see cref="Name"/> and <see cref="Decide"/> are required.
/// </remarks>
public sealed class MyBot : IPokerBot
{
    // The ranks from low to high; a card is a rank and a suit, "Ah" is the ace of hearts.
    private const string Ranks = "23456789TJQKA";

    private Random _random = new(0);

    /// <summary>The name shown at the table.</summary>
    public string Name => "My bot";

    /// <summary>
    /// Called once when the match starts. The seed is yours alone: use it for your own randomness, and a match that
    /// is run again plays out the same way.
    /// </summary>
    public void OnMatchStart(MatchStart m)
    {
        _random = new Random(unchecked((int)m.Seed));
    }

    /// <summary>Called when it is your turn. Everything you need is in the decision; return one of the legal actions.</summary>
    public BotAction Decide(Decision d)
    {
        var wantsToRaise = d.Board.Count == 0 ? IsStrongStartingHand(d.Hole) : HasPairWithBoard(d.Hole, d.Board);

        // A raise is "raise to": the total you bet this street. Three big blinds is a normal opening raise.
        if (wantsToRaise && d.Legal is { MinRaiseTo: { } min, MaxRaiseTo: { } max })
        {
            return BotAction.Raise(Math.Clamp(3 * d.BigBlind, min, max));
        }

        if (d.Legal.Check)
        {
            return BotAction.Check;
        }

        // Pot odds: a call that costs a quarter of what is then in the pot needs to win a quarter of the time.
        var pot = d.Pots.Sum(p => p.Amount);
        var price = (double)d.ToCall / (pot + d.ToCall);
        var cheap = d.Board.Count == 0 ? d.ToCall <= d.BigBlind : price < 0.25;

        // Now and then call something a little too expensive, so that the others cannot count on you folding.
        return cheap || _random.Next(10) == 0 ? BotAction.Call : BotAction.Fold;
    }

    private static int RankOf(string card) => Ranks.IndexOf(card[0], StringComparison.Ordinal);

    private static bool IsStrongStartingHand(IReadOnlyList<string> hole)
    {
        var (high, low) = (Math.Max(RankOf(hole[0]), RankOf(hole[1])), Math.Min(RankOf(hole[0]), RankOf(hole[1])));
        var suited = hole[0][1] == hole[1][1];
        var ten = Ranks.IndexOf('T', StringComparison.Ordinal);
        var ace = Ranks.Length - 1;
        return high == low || low >= ten || (high == ace && suited);
    }

    private static bool HasPairWithBoard(IReadOnlyList<string> hole, IReadOnlyList<string> board) =>
        RankOf(hole[0]) == RankOf(hole[1]) || hole.Any(card => board.Any(b => RankOf(b) == RankOf(card)));
}
