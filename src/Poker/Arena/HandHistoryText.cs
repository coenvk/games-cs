using System.Globalization;
using System.Text;
using Jentl.Cards;

namespace Poker.Arena;

/// <summary>What kind of betting move an action was.</summary>
internal enum ActionKind
{
    Fold,
    Check,
    Call,
    Bet,
    Raise,
}

/// <summary>One action of a hand read as a betting move.</summary>
/// <param name="Action">The recorded action.</param>
/// <param name="Kind">What it was: an all-in counts as a bet, raise or call by what it did.</param>
/// <param name="Increase">For a bet the amount bet; for a raise the amount over the bet it raised.</param>
/// <param name="AllIn">Whether the seat had no chips left afterwards.</param>
internal sealed record BettingStep(ActionRecord Action, ActionKind Kind, int Increase, bool AllIn);

/// <summary>
/// Replays the betting of a recorded hand: who posted the blinds, what each action amounted to, and what each seat
/// put in. The blinds are not actions in a <see cref="HandRecord"/>; they are worked out as the engine deals them.
/// </summary>
internal sealed class BettingReplay
{
    public BettingReplay(HandRecord hand)
    {
        var seats = hand.Names.Count;
        Committed = new int[seats];
        var left = new int[seats];
        for (var i = 0; i < seats; i++)
        {
            left[i] = hand.StartStacks[i];
        }

        SmallBlindSeat = hand.SmallBlindSeat;
        BigBlindSeat = hand.BigBlindSeat;
        SmallBlindPosted = hand.SmallBlindPosted;
        BigBlindPosted = hand.BigBlindPosted;
        Put(SmallBlindSeat, SmallBlindPosted);
        Put(BigBlindSeat, BigBlindPosted);

        var steps = new List<BettingStep>();
        var street = Street.Preflop;
        var bet = hand.BigBlind;
        foreach (var action in hand.Actions)
        {
            if (action.Street != street)
            {
                street = action.Street;
                bet = 0;
            }

            var raised = action.Total > bet;
            var kind = action.Action switch
            {
                Fold => ActionKind.Fold,
                Check => ActionKind.Check,
                RaiseTo or AllIn when raised => bet == 0 ? ActionKind.Bet : ActionKind.Raise,
                _ => action.Amount > 0 ? ActionKind.Call : ActionKind.Check,
            };
            Put(action.Seat, action.Amount);
            steps.Add(new BettingStep(action, kind, kind == ActionKind.Raise ? action.Total - bet : action.Total, left[action.Seat] == 0));
            bet = Math.Max(bet, action.Total);
        }

        Steps = steps;
        return;

        void Put(int seat, int amount)
        {
            Committed[seat] += amount;
            left[seat] -= amount;
        }
    }

    public int SmallBlindSeat { get; }

    public int BigBlindSeat { get; }

    public int SmallBlindPosted { get; }

    public int BigBlindPosted { get; }

    public IReadOnlyList<BettingStep> Steps { get; }

    /// <summary>What each seat put in over the hand, blinds included.</summary>
    public int[] Committed { get; }

    /// <summary>The chips of a seat's bet that no one called, which went back to it.</summary>
    /// <summary>The street of the bet that no one called: the last one the seat put chips in on (the blinds count as preflop).</summary>
    public Street UncalledStreet(int seat) =>
        Steps.LastOrDefault(s => s.Action.Seat == seat && s.Action.Amount > 0)?.Action.Street ?? Street.Preflop;

    public int Uncalled(int seat)
    {
        var others = Enumerable.Range(0, Committed.Length).Where(s => s != seat).Max(s => Committed[s]);
        return Math.Max(0, Committed[seat] - others);
    }

}

/// <summary>Writes a hand the way PokerStars does in its hand history files, the text poker tools read.</summary>
public static class HandHistoryText
{
    /// <summary>The most hands <see cref="WriteMatch"/> numbers apart within one second's matches.</summary>
    public const int MaxMatchHands = 999_999;

    /// <summary>
    /// Every hand of <paramref name="match"/> as one hand history file, a blank line between hands. The hands are
    /// numbered apart: the id is the match's local time as <c>yyMMddHHmmss</c> followed by the hand's place in the match
    /// in six digits, so no two hands of the match share one and matches played at different times do not either. A
    /// batch plays the same hand numbers again in every run, so each run is its own table, "<paramref name="name"/> 1",
    /// "<paramref name="name"/> 2" and so on.
    /// </summary>
    /// <exception cref="ArgumentException">The match has more than <see cref="MaxMatchHands"/> hands.</exception>
    public static string WriteMatch(MatchResult match, string name)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(name);
        if (match.Hands.Count > MaxMatchHands)
        {
            throw new ArgumentException($"A match of more than {MaxMatchHands} hands cannot be numbered apart.", nameof(match));
        }

        var stamp = long.Parse(match.At.ToLocalTime().ToString("yyMMddHHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        var run = 1;
        var last = 0;
        for (var i = 0; i < match.Hands.Count; i++)
        {
            var hand = match.Hands[i];
            if (hand.Number <= last)
            {
                run++;
            }

            last = hand.Number;
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(Write(hand, $"{name} {run}", match.At, (stamp * 1_000_000) + i + 1));
        }

        return text.ToString();
    }

    /// <summary>
    /// The history of <paramref name="hand"/> as text with LF line endings. Amounts are chips; the table is called
    /// <paramref name="table"/> and the hand is dated <paramref name="at"/> (in UTC). Every seat's hole cards are
    /// written, as the record knows them all.
    /// The number in the header is <paramref name="id"/>; without one it is the seed read as unsigned, so it is never
    /// negative, followed by the hand number in six digits. That is the same for the same deal in every run of a
    /// duplicate batch, so a whole match is written with <see cref="WriteMatch"/>, which numbers every hand apart.
    /// </summary>
    public static string Write(HandRecord hand, string table, DateTimeOffset at, long? id = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(table);
        var replay = new BettingReplay(hand);
        var inHand = Enumerable.Range(0, hand.Names.Count).Where(s => hand.Holes[s].Count > 0).ToArray();
        var text = new StringBuilder();
        Line($"PokerStars Hand #{id?.ToString(CultureInfo.InvariantCulture) ?? $"{(uint)hand.Seed}{hand.Number:D6}"}: Hold'em No Limit ({hand.SmallBlind}/{hand.BigBlind}) - {at.UtcDateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        Line($"Table '{table}' 9-max Seat #{hand.Button + 1} is the button");
        foreach (var seat in inHand)
        {
            Line($"Seat {seat + 1}: {hand.Names[seat]} ({hand.StartStacks[seat]} in chips)");
        }

        Line($"{hand.Names[replay.SmallBlindSeat]}: posts small blind {replay.SmallBlindPosted}{AllInSuffix(hand, replay.SmallBlindSeat, replay.SmallBlindPosted)}");
        Line($"{hand.Names[replay.BigBlindSeat]}: posts big blind {replay.BigBlindPosted}{AllInSuffix(hand, replay.BigBlindSeat, replay.BigBlindPosted)}");
        Line("*** HOLE CARDS ***");
        foreach (var seat in inHand)
        {
            Line($"Dealt to {hand.Names[seat]} [{Cards(hand.Holes[seat])}]");
        }

        foreach (var street in new[] { Street.Preflop, Street.Flop, Street.Turn, Street.River })
        {
            var dealt = street switch { Street.Flop => 3, Street.Turn => 4, Street.River => 5, _ => 0 };
            if (hand.Board.Count < dealt)
            {
                break;
            }

            switch (street)
            {
                case Street.Flop:
                    Line($"*** FLOP *** [{Cards(hand.Board.Take(3))}]");
                    break;
                case Street.Turn:
                    Line($"*** TURN *** [{Cards(hand.Board.Take(3))}] [{Cards(hand.Board.Skip(3).Take(1))}]");
                    break;
                case Street.River:
                    Line($"*** RIVER *** [{Cards(hand.Board.Take(4))}] [{Cards(hand.Board.Skip(4))}]");
                    break;
            }

            foreach (var step in replay.Steps.Where(s => s.Action.Street == street))
            {
                Line($"{hand.Names[step.Action.Seat]}: {Describe(step)}{(step.AllIn ? " and is all-in" : string.Empty)}");
            }

            foreach (var seat in inHand.Where(s => replay.Uncalled(s) > 0 && replay.UncalledStreet(s) == street))
            {
                Line($"Uncalled bet ({replay.Uncalled(seat)}) returned to {hand.Names[seat]}");
            }
        }

        var shown = hand.Shown.Order().ToArray();
        if (shown.Length > 0)
        {
            Line("*** SHOW DOWN ***");
            foreach (var seat in shown)
            {
                Line($"{hand.Names[seat]}: shows [{Cards(hand.Holes[seat])}]{Rank(hand, seat)}");
            }
        }

        foreach (var seat in inHand)
        {
            var won = Collected(hand, replay, seat);
            if (won > 0)
            {
                Line($"{hand.Names[seat]} collected {won} from pot");
            }
        }

        var total = replay.Committed.Sum() - inHand.Sum(replay.Uncalled);
        Line("*** SUMMARY ***");
        Line($"Total pot {total} | Rake 0");
        if (hand.Board.Count > 0)
        {
            Line($"Board [{Cards(hand.Board)}]");
        }

        foreach (var seat in inHand)
        {
            Line($"Seat {seat + 1}: {hand.Names[seat]}{Position(hand, replay, seat)} {Outcome(hand, replay, seat)}");
        }

        return text.ToString();

        void Line(string line) => text.Append(line).Append('\n');
    }

    internal static int Collected(HandRecord hand, BettingReplay replay, int seat) =>
        hand.Won.GetValueOrDefault(seat) - replay.Uncalled(seat);

    private static string Cards(IEnumerable<Card> cards) => string.Join(' ', cards.Select(BotView.Code));

    private static string AllInSuffix(HandRecord hand, int seat, int posted) =>
        posted == hand.StartStacks[seat] ? " and is all-in" : string.Empty;

    private static string Describe(BettingStep step) => step.Kind switch
    {
        ActionKind.Fold => "folds",
        ActionKind.Check => "checks",
        ActionKind.Call => $"calls {step.Action.Amount}",
        ActionKind.Bet => $"bets {step.Action.Total}",
        _ => $"raises {step.Increase} to {step.Action.Total}",
    };

    private static string Rank(HandRecord hand, int seat)
    {
        var cards = hand.Holes[seat].Concat(hand.Board).ToArray();
        return cards.Length >= 5 ? $" ({HandEvaluator.Evaluate(cards).Describe()})" : string.Empty;
    }

    private static string Position(HandRecord hand, BettingReplay replay, int seat) =>
        seat == hand.Button ? " (button)"
        : seat == replay.SmallBlindSeat ? " (small blind)"
        : seat == replay.BigBlindSeat ? " (big blind)"
        : string.Empty;

    private static string Outcome(HandRecord hand, BettingReplay replay, int seat)
    {
        var won = Collected(hand, replay, seat);
        var shown = hand.Shown.Contains(seat);
        var cards = shown ? $"showed [{Cards(hand.Holes[seat])}] and " : string.Empty;
        if (won > 0)
        {
            return shown ? $"{cards}won ({won})" : $"collected ({won})";
        }

        if (shown)
        {
            return $"{cards}lost";
        }

        return replay.Steps.Any(s => s.Action.Seat == seat && s.Kind == ActionKind.Fold) ? "folded" : "mucked";
    }
}
