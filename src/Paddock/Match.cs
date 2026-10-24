namespace Paddock;

/// <summary>A thing about a car that can decide a round.</summary>
public enum Factor { TopSpeed, Acceleration, Handling, Weight, Tyres, Drive, Clearance }

/// <summary>One round: who won, by how much, both runs, and what decided it.</summary>
/// <param name="Winner">0 the player, 1 the opponent.</param>
/// <param name="Margin">The winner's lead at the line, in seconds.</param>
/// <param name="Player">The player's car's run.</param>
/// <param name="Opponent">The opponent's car's run.</param>
/// <param name="Deciding">Up to three factors that won it, most important first.</param>
public sealed record RoundResult(int Winner, double Margin, RaceRun Player, RaceRun Opponent, IReadOnlyList<Factor> Deciding);

/// <summary>Racing one car against another on one track.</summary>
public static class Rounds
{
    /// <summary>
    /// Races the two and explains the result. A round never draws: equal times go to the better handling plus a tenth
    /// of the top speed, then to the player.
    /// </summary>
    public static RoundResult Decide(Stats player, Stats opponent, TrackDef track, Condition condition)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(opponent);
        var mine = RaceSimulator.Run(player, track, condition);
        var theirs = RaceSimulator.Run(opponent, track, condition);
        var winner = mine.Time < theirs.Time ? 0
            : theirs.Time < mine.Time ? 1
            : Score(opponent) > Score(player) ? 1 : 0;
        var (won, lost, time) = winner == 0 ? (player, opponent, mine.Time) : (opponent, player, theirs.Time);
        return new RoundResult(winner, Math.Abs(mine.Time - theirs.Time), mine, theirs, Explain(won, lost, time, track, condition));

        static double Score(Stats s) => s.Handling + (s.TopSpeedKmh / 10);
    }

    // What the winner would lose by trading one thing for the loser's: the biggest losses decided the round.
    private static IReadOnlyList<Factor> Explain(Stats winner, Stats loser, double time, TrackDef track, Condition condition)
    {
        var costs = new List<(Factor Factor, double Cost)>();
        foreach (var factor in Enum.GetValues<Factor>())
        {
            var swapped = factor switch
            {
                Factor.TopSpeed => winner with { TopSpeedKmh = loser.TopSpeedKmh },
                Factor.Acceleration => winner with { ZeroTo100 = loser.ZeroTo100 },
                Factor.Handling => winner with { Handling = loser.Handling },
                Factor.Weight => winner with { WeightKg = loser.WeightKg },
                Factor.Tyres => winner with { Tyres = loser.Tyres },
                Factor.Drive => winner with { Drive = loser.Drive },
                _ => winner with { Clearance = loser.Clearance },
            };
            var cost = RaceSimulator.Run(swapped, track, condition).Time - time;
            if (cost > 1e-9)
            {
                costs.Add((factor, cost));
            }
        }

        return [.. costs.OrderByDescending(c => c.Cost).Take(3).Select(c => c.Factor)];
    }
}

/// <summary>A match about to be raced: the rounds' tracks and conditions, and the opponent's cars in order.</summary>
/// <param name="Rounds">Track and condition of each round.</param>
/// <param name="Opponent">The opponent's car for each round.</param>
public sealed record MatchSetup(IReadOnlyList<(TrackDef Track, Condition Condition)> Rounds, IReadOnlyList<CarInstance> Opponent);

/// <summary>A raced match.</summary>
/// <param name="Rounds">Every round's result.</param>
/// <param name="PlayerWon">Whether the player won the match.</param>
public sealed record MatchResult(IReadOnlyList<RoundResult> Rounds, bool PlayerWon);

/// <summary>Racing hands against each other.</summary>
public static class Matches
{
    /// <summary>Races the player's cars, in order, against the opponent's: most rounds won wins, then the larger total winning margin.</summary>
    public static MatchResult Play(MatchSetup setup, IReadOnlyList<CarInstance> playerOrder, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(playerOrder);
        ArgumentNullException.ThrowIfNull(roster);
        var rounds = new List<RoundResult>();
        for (var i = 0; i < setup.Rounds.Count; i++)
        {
            var (track, condition) = setup.Rounds[i];
            rounds.Add(Rounds.Decide(StatsOf(playerOrder[i], roster), StatsOf(setup.Opponent[i], roster), track, condition));
        }

        double Margins(int side) => rounds.Where(r => r.Winner == side).Sum(r => r.Margin);
        var wins = rounds.Count(r => r.Winner == 0);
        return new MatchResult(rounds, Compare(wins, Margins(0), rounds.Count - wins, Margins(1)) > 0);
    }

    /// <summary>Positive when the first side won the match: more rounds, or as many with a larger total margin. Equal margins go to the first side (the player).</summary>
    public static int Compare(int wins, double margins, int otherWins, double otherMargins) =>
        wins != otherWins ? wins.CompareTo(otherWins) : margins >= otherMargins ? 1 : -1;

    /// <summary>A car's stats with its upgrades.</summary>
    public static Stats StatsOf(CarInstance car, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(roster);
        var model = roster.Find(car.ModelId) ?? throw new ArgumentException($"There is no car '{car.ModelId}'.", nameof(car));
        return Upgrades.Apply(model.Stats, car.Tune);
    }
}

/// <summary>The rules for a hand of five.</summary>
public static class Hands
{
    /// <summary>The number of cars in a hand.</summary>
    public const int Size = 5;

    /// <summary>Whether the cars make a legal hand: five different cars that exist, every one allowed, their RQ within the limit.</summary>
    public static bool Fits(IReadOnlyList<CarInstance> hand, Roster roster, int rqLimit, Func<CarModel, bool>? rule = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(roster);
        if (hand.Count != Size || hand.Select(c => c.Id).Distinct().Count() != Size)
        {
            return false;
        }

        var models = hand.Select(c => roster.Find(c.ModelId)).ToList();
        return models.All(m => m is not null && (rule?.Invoke(m) ?? true))
            && hand.Sum(c => roster.Rq(c.ModelId)) <= rqLimit;
    }
}
