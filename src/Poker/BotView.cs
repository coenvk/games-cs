using Jentl.Cards;
using PokerBots;
using BotPot = PokerBots.Pot;

namespace Poker;

/// <summary>Something a player did during a hand, kept so a bot can be shown the hand so far.</summary>
/// <param name="Total">The player's bet on the street afterwards.</param>
public sealed record ActionRecord(Street Street, int Seat, PokerAction Action, int Amount, int Total);

/// <summary>
/// The line between the engine and a bot: turns the engine's state into the messages a bot gets, showing it one
/// seat's view and nothing more, and checks what a bot answers against what the engine allows.
/// </summary>
public static class BotView
{
    private const string RankLetters = "A23456789TJQK";

    /// <summary>The two-character code of a card: a rank from "23456789TJQKA" (an ace is "A") and a suit from "shdc".</summary>
    public static string Code(Card card) =>
        RankLetters[card.Rank - 1].ToString() + card.Suit switch
        {
            Suit.Spades => "s",
            Suit.Hearts => "h",
            Suit.Diamonds => "d",
            Suit.Clubs => "c",
            _ => throw new ArgumentOutOfRangeException(nameof(card), card, "Unknown suit."),
        };

    /// <summary>The card a two-character <paramref name="code"/> stands for; the other way round from <see cref="Code"/>.</summary>
    public static Card Card(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var rank = code.Length == 2 ? RankLetters.IndexOf(code[0], StringComparison.Ordinal) + 1 : 0;
        var suit = code.Length == 2 ? code[1] switch
        {
            's' => Suit.Spades,
            'h' => Suit.Hearts,
            'd' => Suit.Diamonds,
            'c' => Suit.Clubs,
            _ => (Suit?)null,
        } : null;
        return rank == 0 || suit is null
            ? throw new ArgumentException($"Not a card code: \"{code}\".", nameof(code))
            : new Card(rank, suit.Value);
    }

    /// <summary>What <paramref name="seat"/> is shown when it is its turn: its own cards, the board and the betting.</summary>
    /// <param name="decisionId">The id of this decision; the bot's answer must carry it back.</param>
    /// <param name="names">The players' names by seat.</param>
    public static Decision Decision(
        HoldemHand hand,
        int seat,
        int decisionId,
        int handNumber,
        int level,
        IReadOnlyList<string> names,
        IReadOnlyList<ActionRecord> actions,
        TimeSpan limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(names.Count, hand.Seats);
        var moves = hand.LegalMoves(seat);
        var toCall = hand.ToCall(seat);
        var max = hand.MaxRaiseTo(seat);

        // An all-in move beyond a call means raising is possible; when it is only the call, it is not.
        var canRaise = moves.Contains(AllIn.Instance) && hand.Stack(seat) > hand.CurrentBet - hand.Bet(seat);
        var legal = new LegalActions(
            Fold: true,
            Check: toCall == 0,
            Call: toCall > 0 ? toCall : null,
            MinRaiseTo: canRaise ? Math.Min(hand.MinRaiseTo, max) : null,
            MaxRaiseTo: canRaise ? max : null,
            AllIn: moves.Contains(AllIn.Instance));

        return new Decision(
            decisionId,
            handNumber,
            StreetName(hand.Street),
            seat,
            [.. hand.Hole(seat).Select(Code)],
            [.. hand.Board.Select(Code)],
            hand.Button,
            hand.SmallBlindSeat,
            hand.BigBlindSeat,
            hand.SmallBlind,
            hand.BigBlind,
            level,
            [.. Enumerable.Range(0, hand.Seats).Select(s => new TableSeat(s, names[s], hand.Stack(s), hand.Bet(s), State(hand, s)))],
            [.. hand.Pots.Select(p => new BotPot(p.Amount, p.Seats))],
            toCall,
            legal,
            Played(actions),
            (int)limit.TotalMilliseconds);
    }

    /// <summary>What every bot is told when a hand starts, with stacks as they were before the blinds.</summary>
    public static HandStart HandStart(HoldemHand hand, int handNumber, IReadOnlyList<string> names)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(names.Count, hand.Seats);
        return new(
            handNumber,
            hand.Button,
            hand.SmallBlind,
            hand.BigBlind,
            [.. Enumerable.Range(0, hand.Seats).Select(s =>
                new HandSeat(s, names[s], hand.Stack(s) + hand.Committed(s), hand.InHand(s) ? "active" : "out"))]);
    }

    /// <summary>
    /// What every bot is told when a hand is over. Hole cards appear only for the seats that showed them at
    /// showdown, so a hand won by everyone else folding reveals nothing.
    /// </summary>
    public static HandEnd HandEnd(HoldemHand hand, int handNumber, IReadOnlyList<ActionRecord> actions)
    {
        if (!hand.IsOver)
        {
            throw new InvalidOperationException("The hand is not over: showing it now would reveal hole cards.");
        }

        var live = Enumerable.Range(0, hand.Seats).Where(hand.IsLive).ToArray();
        var showdown = live.Length > 1;
        var values = showdown
            ? live.ToDictionary(s => s, s => HandEvaluator.Evaluate(hand.Hole(s).Concat(hand.Board)))
            : [];

        return new HandEnd(
            handNumber,
            [.. hand.Board.Select(Code)],
            Played(actions),
            [.. live.Where(_ => showdown).Select(s => new ShownHand(s, [.. hand.Hole(s).Select(Code)], RankName(values[s].Category)))],
            [.. hand.Winnings.OrderBy(w => w.Key).Select(w => new Win(w.Key, w.Value))],
            [.. hand.PotResults.Select(p => new PotResult(p.Amount, p.Seats, [.. p.Winners.Select(w => new Win(w.Seat, w.Amount))]))],
            [.. Enumerable.Range(0, hand.Seats).Select(hand.Stack)]);
    }

    /// <summary>
    /// The engine's action for a bot's answer, or null when the answer is not legal now: it answers another
    /// decision, checks facing a bet, calls with nothing to call, raises out of bounds or when raising is not
    /// possible, or is not a known action. A raise below the minimum is only legal as an all-in.
    /// </summary>
    public static PokerAction? ToAction(BotAction answer, HoldemHand hand, int seat, int decisionId)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var moves = hand.LegalMoves(seat);
        if (moves.Count == 0 || (answer.Id is { } id && id != decisionId))
        {
            return null;
        }

        var toCall = hand.CurrentBet - hand.Bet(seat);
        var canRaise = moves.Contains(AllIn.Instance) && hand.Stack(seat) > toCall;
        switch (answer.Action)
        {
            case "fold":
                return Fold.Instance;
            case "check":
                return toCall == 0 ? Check.Instance : null;
            case "call":
                return toCall > 0 ? CallBet.Instance : null;
            case "all-in":
                return moves.Contains(AllIn.Instance) ? AllIn.Instance : null;
            case "raise":
                var max = hand.MaxRaiseTo(seat);
                if (!canRaise || answer.To is not { } to || to <= hand.CurrentBet || to > max || (to < hand.MinRaiseTo && to != max))
                {
                    return null;
                }

                return to == max ? AllIn.Instance : new RaiseTo(to);
            default:
                return null;
        }
    }

    /// <summary>What to do when a bot gives no usable answer: check when it is free, otherwise fold.</summary>
    public static PokerAction Fallback(HoldemHand hand, int seat) =>
        hand.ToCall(seat) == 0 ? Check.Instance : Fold.Instance;

    private static string State(HoldemHand hand, int seat) =>
        !hand.InHand(seat) ? "out" : hand.HasFolded(seat) ? "folded" : hand.IsAllIn(seat) ? "all-in" : "active";

    private static string StreetName(Street street) => street switch
    {
        Street.Preflop => "preflop",
        Street.Flop => "flop",
        Street.Turn => "turn",
        Street.River => "river",
        _ => throw new ArgumentOutOfRangeException(nameof(street), street, "A bot is only asked on a betting street."),
    };

    private static string ActionName(PokerAction action) => action switch
    {
        Fold => "fold",
        Check => "check",
        CallBet => "call",
        RaiseTo => "raise",
        AllIn => "all-in",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };

    private static PlayedAction[] Played(IReadOnlyList<ActionRecord> actions) =>
        [.. actions.Select(a => new PlayedAction(StreetName(a.Street), a.Seat, ActionName(a.Action), a.Amount, a.Total))];

    private static string RankName(HandCategory category) => category switch
    {
        HandCategory.HighCard => "high card",
        HandCategory.Pair => "pair",
        HandCategory.TwoPair => "two pair",
        HandCategory.ThreeOfAKind => "three of a kind",
        HandCategory.Straight => "straight",
        HandCategory.Flush => "flush",
        HandCategory.FullHouse => "full house",
        HandCategory.FourOfAKind => "four of a kind",
        HandCategory.StraightFlush => "straight flush",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown hand category."),
    };
}
