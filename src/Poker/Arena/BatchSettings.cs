namespace Poker.Arena;

/// <summary>How a batch of matches is played.</summary>
public enum BatchFormat
{
    /// <summary>Two bots play the same hands twice with their seats swapped, so luck of the cards cancels out.</summary>
    DuplicateHeadsUp,

    /// <summary>Every bot gets every seat for the same hands at a ring table.</summary>
    DuplicateRing,

    /// <summary>A series of sit-and-go tournaments.</summary>
    SitAndGoSeries,
}

/// <summary>The rules of a batch of matches.</summary>
/// <param name="Format">How the matches are played.</param>
/// <param name="Hands">The number of hands (or tournaments) to play.</param>
/// <param name="Seed">The batch seed: every run of a duplicate batch is dealt from it, and each game of a series from
/// a seed derived from it.</param>
/// <param name="TimeLimit">How long a bot gets for each decision.</param>
/// <param name="Parallel">Whether matches may be played at the same time, at most one per processor. Beware: bots
/// that run in their own processes then compete for the processors and may be counted late.</param>
public sealed record BatchSettings(BatchFormat Format, int Hands, DealSeed Seed, TimeSpan TimeLimit, bool Parallel = false);
