using Jentl.Collect;
using Jentl.Net;

namespace GameServer;

/// <summary>The leaderboards this server keeps, and what a believable score on each is.</summary>
public static class Leaderboards
{
    /// <summary>Bricks: points, higher is better; levels repeat with a growing multiplier, so the cap is generous.</summary>
    public static IReadOnlyDictionary<string, BoardRules> Boards { get; } = new Dictionary<string, BoardRules>
    {
        ["bricks"] = new(ScoreOrder.HigherIsBetter, MaxScore: 10_000_000),
    };
}
