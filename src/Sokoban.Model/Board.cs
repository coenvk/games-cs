namespace Sokoban.Model;

/// <summary>
/// The static part of a level: walls, goals and which cells lie inside the walls.
/// Cells are addressed by a flat index (<c>y * Width + x</c>).
/// </summary>
public sealed class Board
{
    [Flags]
    internal enum Tile : byte
    {
        None = 0,
        Wall = 1,
        Goal = 2,
        Interior = 4,
        Ice = 8,
    }

    private readonly Tile[] _tiles;

    internal Board(int width, int height, Tile[] tiles)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        Goals = Enumerable.Range(0, tiles.Length).Where(IsGoal).ToArray();
        HasIce = tiles.Any(t => (t & Tile.Ice) != 0);
    }

    /// <summary>True when any cell is ice, so crates can slide.</summary>
    public bool HasIce { get; }

    public int Width { get; }

    public int Height { get; }

    public int Size => _tiles.Length;

    public IReadOnlyList<int> Goals { get; }

    public int IndexOf(int x, int y) => y * Width + x;

    public int XOf(int index) => index % Width;

    public int YOf(int index) => index / Width;

    public bool IsWall(int index) => (_tiles[index] & Tile.Wall) != 0;

    public bool IsGoal(int index) => (_tiles[index] & Tile.Goal) != 0;

    /// <summary>True for floor cells enclosed by walls; false for walls and the void outside.</summary>
    public bool IsInterior(int index) => (_tiles[index] & Tile.Interior) != 0;

    /// <summary>A crate pushed onto ice keeps sliding until it is blocked or reaches a cell that is not ice.</summary>
    public bool IsIce(int index) => (_tiles[index] & Tile.Ice) != 0;

    public bool TryGetNeighbor(int index, Direction direction, out int neighbor)
    {
        int x = XOf(index), y = YOf(index);
        (x, y) = direction switch
        {
            Direction.Up => (x, y - 1),
            Direction.Down => (x, y + 1),
            Direction.Left => (x - 1, y),
            Direction.Right => (x + 1, y),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            neighbor = -1;
            return false;
        }

        neighbor = IndexOf(x, y);
        return true;
    }
}
