using System.Globalization;
using static Games.UI.Tr;

namespace Games.UI;

/// <summary>Numbers as the games show them; a game adds its own (Sokoban's records) in another part of the class.</summary>
internal static partial class Format
{
    /// <summary><c>m:ss</c>, or <c>h:mm:ss</c> from an hour up.</summary>
    public static string Duration(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    public static string Moves(int count) => N(count, "{0} move", "{0} moves");
}
