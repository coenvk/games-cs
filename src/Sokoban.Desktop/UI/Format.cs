using System.Globalization;
using Sokoban.Model;

namespace Sokoban.Desktop.UI;

internal static class Format
{
    /// <summary><c>m:ss</c>, or <c>h:mm:ss</c> from an hour up.</summary>
    public static string Duration(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    public static string Result(int moves, int pushes, TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{moves} moves, {pushes} pushes, {Duration(time)}");

    public static string Best(LevelRecord record) =>
        "Best: " + Result(record.BestMoves, record.BestPushes, record.BestTime);

    /// <summary>"New best: moves, time", or null when nothing improved on an earlier solve.</summary>
    public static string? Improvement(Improvement improvement)
    {
        var parts = new List<string>(3);
        if (improvement.HasFlag(Model.Improvement.Moves))
        {
            parts.Add("moves");
        }

        if (improvement.HasFlag(Model.Improvement.Pushes))
        {
            parts.Add("pushes");
        }

        if (improvement.HasFlag(Model.Improvement.Time))
        {
            parts.Add("time");
        }

        return parts.Count == 0 ? null : "New best: " + string.Join(", ", parts);
    }
}
