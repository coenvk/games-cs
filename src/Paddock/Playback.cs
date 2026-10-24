namespace Paddock;

/// <summary>
/// How a round is played back on screen: its runs squeezed into 10 to 20 seconds (drag races 6 to 8), both cars on
/// the same clock, so the car first over the line on screen is the one that won.
/// </summary>
public static class Playback
{
    /// <summary>How long the round plays on screen, in seconds.</summary>
    public static double Duration(double winnerTime, TrackKind kind) =>
        kind == TrackKind.Drag ? Math.Clamp(winnerTime / 2, 6, 8) : Math.Clamp(winnerTime / 4, 10, 20);

    /// <summary>The race time shown <paramref name="seconds"/> into the playback.</summary>
    public static double RaceTime(double seconds, double winnerTime, TrackKind kind) =>
        seconds * winnerTime / Duration(winnerTime, kind);
}
