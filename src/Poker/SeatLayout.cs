using System.Numerics;

namespace Poker;

/// <summary>Where the seats go round an oval table, whatever draws it.</summary>
public static class SeatLayout
{
    /// <summary>The fewest seats a table lays out.</summary>
    public const int MinSeats = 2;

    /// <summary>The most seats a table lays out.</summary>
    public const int MaxSeats = 9;

    /// <summary>
    /// The seats evenly spaced on the unit ellipse (x and y from -1 to 1, y growing downwards): seat 0 at the
    /// bottom centre, then clockwise as seen on screen, so seat 1 is to its left.
    /// </summary>
    public static Vector2[] Positions(int seats)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, MinSeats);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seats, MaxSeats);
        return [.. Enumerable.Range(0, seats).Select(k =>
        {
            var angle = (MathF.PI / 2) + (k * 2 * MathF.PI / seats);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        })];
    }
}
