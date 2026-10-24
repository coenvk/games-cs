using System.Globalization;
using Sokoban.Model;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.UI;

internal static class Format
{
    /// <summary><c>m:ss</c>, or <c>h:mm:ss</c> from an hour up.</summary>
    public static string Duration(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    public static string Moves(int count) => N(count, "{0} move", "{0} moves");

    public static string Pushes(int count) => N(count, "{0} push", "{0} pushes");

    public static string Result(int moves, int pushes, TimeSpan time) => $"{Moves(moves)}, {Pushes(pushes)}, {Duration(time)}";

    public static string Best(LevelRecord record) =>
        T("Best: {0}", Result(record.BestMoves, record.BestPushes, record.BestTime));

    /// <summary>"New best: moves, time", or null when nothing improved on an earlier solve.</summary>
    public static string? Improvement(Improvement improvement)
    {
        var parts = new List<string>(3);
        if (improvement.HasFlag(Model.Improvement.Moves))
        {
            parts.Add(T("moves"));
        }

        if (improvement.HasFlag(Model.Improvement.Pushes))
        {
            parts.Add(T("pushes"));
        }

        if (improvement.HasFlag(Model.Improvement.Time))
        {
            parts.Add(T("time"));
        }

        return parts.Count == 0 ? null : T("New best: {0}", string.Join(", ", parts));
    }
}
