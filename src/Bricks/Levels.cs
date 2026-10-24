namespace Bricks;

/// <summary>
/// The brick walls, one string per row, one character per brick: <c>.</c> empty, <c>1</c> one hit, <c>2</c> two
/// hits, <c>#</c> unbreakable. After the last wall they come round again, faster.
/// </summary>
public static class Levels
{
    public const int Columns = 10;

    public static IReadOnlyList<string[]> All { get; } =
    [
        [
            "1111111111",
            "1111111111",
            "1111111111",
            "1111111111",
            "1111111111",
        ],
        [
            "....11....",
            "...1111...",
            "..112211..",
            ".11122111.",
            "1112222111",
            "1111111111",
        ],
        [
            "2.2.2.2.2.",
            ".1.1.1.1.1",
            "2.2.2.2.2.",
            ".1.1.1.1.1",
            "##..11..##",
            "1111111111",
        ],
        [
            "2222222222",
            "2111111112",
            "21######12",
            "2111111112",
            "2222222222",
        ],
    ];

    /// <summary>The wall for level <paramref name="level"/> (0-based), cycling through them.</summary>
    public static string[] For(int level) => All[level % All.Count];
}
