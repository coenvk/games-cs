using Jentl.Cards;
using PokerBots;

namespace Poker;

/// <summary>A house bot that checks when it can and calls otherwise, whatever the cards.</summary>
public sealed class CallingBot : IPokerBot
{
    public string Name => "Always call";

    public BotAction Decide(Decision d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Legal.Check ? BotAction.Check : BotAction.Call;
    }
}

/// <summary>
/// A house bot that raises the minimum every time it can, goes all in when that is all the raise it has left,
/// and calls (or checks) when it cannot raise.
/// </summary>
public sealed class RaisingBot : IPokerBot
{
    public string Name => "Always raise";

    public BotAction Decide(Decision d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Legal is { MinRaiseTo: { } min, MaxRaiseTo: { } max })
        {
            return min >= max ? BotAction.AllIn : BotAction.Raise(min);
        }

        return d.Legal.Check ? BotAction.Check : BotAction.Call;
    }
}

/// <summary>A house bot that picks uniformly among the kinds of move it may make, and a raise's size uniformly in range.</summary>
public sealed class RandomBot(int seed) : IPokerBot
{
    private readonly Random _random = new(seed);

    public string Name => "Random";

    public BotAction Decide(Decision d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var legal = d.Legal;
        var kinds = new List<string> { "fold" };
        if (legal.Check)
        {
            kinds.Add("check");
        }

        if (legal.Call is not null)
        {
            kinds.Add("call");
        }

        if (legal.MinRaiseTo is not null)
        {
            kinds.Add("raise");
        }

        if (legal.AllIn)
        {
            kinds.Add("all-in");
        }

#pragma warning disable CA5394 // Game play, not security relevant.
        var kind = kinds[_random.Next(kinds.Count)];
        return kind switch
        {
            "fold" => BotAction.Fold,
            "check" => BotAction.Check,
            "call" => BotAction.Call,
            "raise" => BotAction.Raise(_random.Next(legal.MinRaiseTo!.Value, legal.MaxRaiseTo!.Value + 1)),
            _ => BotAction.AllIn,
        };
#pragma warning restore CA5394
    }
}

/// <summary>How an <see cref="EquityBot"/> plays: how often it folds, how hard it raises and how often it bluffs.</summary>
public enum EquityStyle
{
    /// <summary>Folds a lot, raises rarely.</summary>
    Tight,

    /// <summary>The middle way.</summary>
    Balanced,

    /// <summary>Stays in with a lot, raises and bluffs often.</summary>
    Loose,
}

/// <summary>
/// A house bot that estimates its chance of winning by dealing out the unknown cards many times, then weighs that
/// against the price of staying in: <see cref="PokerBot"/>'s way of playing, working from a <see cref="Decision"/>.
/// </summary>
public sealed class EquityBot : IPokerBot
{
    private readonly Random _random;
    private readonly double _tightness;
    private readonly double _aggression;
    private readonly double _bluff;
    private readonly int _samples;

    public EquityBot(EquityStyle style, int seed, int samples = 150)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        Name = style.ToString();
        _random = new Random(seed);
        _samples = samples;
        (_tightness, _aggression, _bluff) = style switch
        {
            EquityStyle.Tight => (0.7, 0.34, 0.04),
            EquityStyle.Balanced => (0.5, 0.5, 0.06),
            EquityStyle.Loose => (0.25, 0.7, 0.1),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown style."),
        };
    }

    public string Name { get; }

    public BotAction Decide(Decision d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var opponents = d.Seats.Count(s => s.Seat != d.You && s.State is "active" or "all-in");
        var equity = PokerBot.Equity(
            [.. d.Hole.Select(BotView.Card)],
            [.. d.Board.Select(BotView.Card)],
            opponents,
            _samples,
            _random);
        var toCall = d.ToCall;
        var pot = d.Pots.Sum(p => p.Amount);
        var potOdds = toCall == 0 ? 0 : toCall / (double)(pot + toCall);

        // How much better than an even share of the pot this hand is: 1 is average at this table.
        var strength = equity * (opponents + 1);
        var raises = Raises(d, pot);
#pragma warning disable CA5394 // Game play, not security relevant.
        var roll = _random.NextDouble();
#pragma warning restore CA5394

        if (raises.Length > 0 && strength > 1.7 - 0.5 * _aggression)
        {
            // Strong: raise, bigger the stronger; shove the monsters.
            if (equity > 0.85 || (strength > 2.4 && roll < _aggression))
            {
                return BotAction.AllIn;
            }

            return Pick(raises, strength > 2 ? 2 : roll < _aggression ? 1 : 0);
        }

        if (toCall == 0)
        {
            return raises.Length > 0 && roll < _bluff ? Pick(raises, 1) : BotAction.Check;
        }

        // Facing a bet: stay in when the odds pay for it.
        if (equity >= potOdds * (1 + _tightness * 0.6))
        {
            return BotAction.Call;
        }

        return raises.Length > 0 && roll < _bluff / 2 ? Pick(raises, 1) : BotAction.Fold;
    }

    /// <summary>The raises worth considering, smallest first: the minimum, half the pot and the pot, then all in.</summary>
    private static BotAction[] Raises(Decision d, int pot)
    {
        if (d.Legal is not { MinRaiseTo: { } min, MaxRaiseTo: { } max })
        {
            return [];
        }

        var me = d.Seats[d.You].Bet;
        var current = me + d.ToCall;
        var total = pot + d.ToCall;
        var sized = new[] { min, current + d.ToCall + total / 2, current + d.ToCall + total }
            .Distinct()
            .Where(to => to >= min && to < max)
            .Select(to => BotAction.Raise(to));
        return [.. sized, BotAction.AllIn];
    }

    /// <summary>The raise at <paramref name="size"/> (0 smallest, 1 middle, 2 biggest), short of all in when it can.</summary>
    private static BotAction Pick(BotAction[] raises, int size)
    {
        var sized = raises.Where(r => r.Action == "raise").ToArray();
        return sized.Length == 0 ? raises[0] : sized[Math.Min(size, sized.Length - 1)];
    }
}

/// <summary>The bots that play at the table without being brought along: each takes a seed for its own randomness.</summary>
public static class HouseBots
{
    /// <summary>Every house bot by name, with a way to make one from a seed.</summary>
    public static IReadOnlyList<(string Name, Func<int, IPokerBot> Make)> All { get; } =
    [
        ("Always call", _ => new CallingBot()),
        ("Always raise", _ => new RaisingBot()),
        ("Random", seed => new RandomBot(seed)),
        ("Tight", seed => new EquityBot(EquityStyle.Tight, seed)),
        ("Balanced", seed => new EquityBot(EquityStyle.Balanced, seed)),
        ("Loose", seed => new EquityBot(EquityStyle.Loose, seed)),
    ];
}
