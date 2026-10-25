using Jentl.Cards;
using Jentl.Multiplayer;
using Poker.Arena;

namespace Poker.Tests;

/// <summary>Hand-built hand records for the history, statistics and match file tests.</summary>
internal static class TestHands
{
    public static Card[] Cards(string codes) => [.. codes.Split(' ').Select(BotView.Card)];

    public static ActionRecord Act(Street street, int seat, PokerAction action, int amount, int total) =>
        new(street, seat, action, amount, total);

    public static HandRecord Hand(
        int number,
        int button,
        int[] stacks,
        string[] holes,
        ActionRecord[] actions,
        string board,
        int[] shown,
        Dictionary<int, int> won,
        int smallBlind = 10,
        int bigBlind = 20,
        string[]? names = null,
        int? bigBlindPosted = null,
        DealSeed? seed = null)
    {
        // Heads-up the button posts the small blind; otherwise the small blind is the seat after the button.
        var playing = Enumerable.Range(0, stacks.Length).Where(s => stacks[s] > 0).ToArray();
        var small = playing.Length == 2 ? button : (button + 1) % stacks.Length;
        var big = (small + 1) % stacks.Length;
        return new(
            number,
            seed ?? DealSeed.FromNumber(7),
            button,
            smallBlind,
            bigBlind,
            small,
            big,
            smallBlind,
            bigBlindPosted ?? bigBlind,
            names ?? ["Ann", "Bob", "Cy"],
            stacks,
            [.. holes.Select(h => (IReadOnlyList<Card>)(h.Length == 0 ? [] : Cards(h)))],
            actions,
            board.Length == 0 ? [] : Cards(board),
            [new Pot(won.Values.Sum(), [.. won.Keys])],
            won,
            new HashSet<int>(shown),
            []);
    }

    /// <summary>Three seats, a raise, a bet on the flop and the turn, and a showdown that Bob wins with three kings.</summary>
    public static HandRecord Showdown() => Hand(
        1,
        button: 0,
        [1000, 1000, 1000],
        ["Ac Jc", "Ks Kh", "5s 4s"],
        [
            Act(Street.Preflop, 0, CallBet.Instance, 20, 20),
            Act(Street.Preflop, 1, new RaiseTo(60), 50, 60),
            Act(Street.Preflop, 2, Fold.Instance, 0, 20),
            Act(Street.Preflop, 0, CallBet.Instance, 40, 60),
            Act(Street.Flop, 1, new RaiseTo(100), 100, 100),
            Act(Street.Flop, 0, CallBet.Instance, 100, 100),
            Act(Street.Turn, 1, Check.Instance, 0, 0),
            Act(Street.Turn, 0, new RaiseTo(200), 200, 200),
            Act(Street.Turn, 1, CallBet.Instance, 200, 200),
            Act(Street.River, 1, Check.Instance, 0, 0),
            Act(Street.River, 0, Check.Instance, 0, 0),
        ],
        "Ah Kd 2c 7s 9d",
        [0, 1],
        new() { [1] = 740 });

    /// <summary>Heads-up: the button shoves for more than the big blind has and is called; the rest comes back.</summary>
    public static HandRecord AllInCall() => Hand(
        2,
        button: 0,
        [100, 50],
        ["Ah Ad", "Kc Kd"],
        [
            Act(Street.Preflop, 0, AllIn.Instance, 90, 100),
            Act(Street.Preflop, 1, CallBet.Instance, 30, 50),
        ],
        "Ks 2h 7c 9d 3s",
        [0, 1],
        new() { [1] = 100, [0] = 50 },
        names: ["Dee", "Eli"]);

    /// <summary>Ann raises before the flop and everyone folds.</summary>
    public static HandRecord RaiseTakesIt() => Hand(
        2,
        button: 0,
        [1000, 1000, 1000],
        ["Ah Ad", "7c 2d", "9s 4s"],
        [
            Act(Street.Preflop, 0, new RaiseTo(60), 60, 60),
            Act(Street.Preflop, 1, Fold.Instance, 0, 10),
            Act(Street.Preflop, 2, Fold.Instance, 0, 20),
        ],
        string.Empty,
        [],
        new() { [0] = 90 });

    /// <summary>Bob (the button) raises and Ann (the big blind) folds.</summary>
    public static HandRecord BigBlindFolds() => Hand(
        3,
        button: 1,
        [1000, 1000, 1000],
        ["8h 3d", "Qh Qd", "6s 2s"],
        [
            Act(Street.Preflop, 1, new RaiseTo(60), 60, 60),
            Act(Street.Preflop, 2, Fold.Instance, 0, 10),
            Act(Street.Preflop, 0, Fold.Instance, 0, 20),
        ],
        string.Empty,
        [],
        new() { [1] = 90 });

    /// <summary>Ann (the big blind) checks, bets the flop and wins at showdown against Cy; Bob folds on the flop.</summary>
    public static HandRecord BigBlindWins() => Hand(
        4,
        button: 1,
        [1000, 1000, 1000],
        ["Ah Kh", "9c 9d", "2c 3d"],
        [
            Act(Street.Preflop, 1, CallBet.Instance, 20, 20),
            Act(Street.Preflop, 2, CallBet.Instance, 10, 20),
            Act(Street.Preflop, 0, Check.Instance, 0, 20),
            Act(Street.Flop, 2, Check.Instance, 0, 0),
            Act(Street.Flop, 0, new RaiseTo(20), 20, 20),
            Act(Street.Flop, 1, Fold.Instance, 0, 0),
            Act(Street.Flop, 2, CallBet.Instance, 20, 20),
            Act(Street.Turn, 2, Check.Instance, 0, 0),
            Act(Street.Turn, 0, Check.Instance, 0, 0),
            Act(Street.River, 2, Check.Instance, 0, 0),
            Act(Street.River, 0, Check.Instance, 0, 0),
        ],
        "Qs 7d 4h Jc 8s",
        [0, 2],
        new() { [0] = 100 });
}
