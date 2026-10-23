namespace Sokoban.Model;

public enum Direction
{
    Up,
    Down,
    Left,
    Right,
}

public static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>LURD notation: lowercase for a walk, uppercase for a push.</summary>
    public static char ToLurd(this Direction direction, bool isPush)
    {
        var c = direction switch
        {
            Direction.Up => 'u',
            Direction.Down => 'd',
            Direction.Left => 'l',
            Direction.Right => 'r',
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
        return isPush ? char.ToUpperInvariant(c) : c;
    }
}
