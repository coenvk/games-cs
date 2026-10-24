using Sokoban.Model;
using static Games.UI.Tr;

namespace Games.UI;

/// <summary>Sokoban's records in words.</summary>
internal static partial class Format
{
    public static string Pushes(int count) => N(count, "{0} push", "{0} pushes");

    public static string Result(int moves, int pushes, TimeSpan time) => $"{Moves(moves)}, {Pushes(pushes)}, {Duration(time)}";

    public static string Best(LevelRecord record) =>
        T("Best: {0}", Result(record.BestMoves, record.BestPushes, record.BestTime));

    /// <summary>"New best: moves, time", or null when nothing improved on an earlier solve.</summary>
    public static string? Improvement(Improvement improvement)
    {
        var parts = new List<string>(3);
        if (improvement.HasFlag(Sokoban.Model.Improvement.Moves))
        {
            parts.Add(T("moves"));
        }

        if (improvement.HasFlag(Sokoban.Model.Improvement.Pushes))
        {
            parts.Add(T("pushes"));
        }

        if (improvement.HasFlag(Sokoban.Model.Improvement.Time))
        {
            parts.Add(T("time"));
        }

        return parts.Count == 0 ? null : T("New best: {0}", string.Join(", ", parts));
    }
}
