using System.Collections.Immutable;

namespace Sokoban.Model;

/// <summary>An immutable level definition: the board plus the starting positions.</summary>
public sealed class Level
{
    private Level(string? title, Board board, int playerStart, ImmutableArray<int> crateStarts)
    {
        Title = title;
        Board = board;
        PlayerStart = playerStart;
        CrateStarts = crateStarts;
    }

    public string? Title { get; }

    public Board Board { get; }

    public int PlayerStart { get; }

    public ImmutableArray<int> CrateStarts { get; }

    /// <summary>
    /// Builds and validates a level. The interior is found by flood-filling from the player
    /// through every non-wall cell; it must not reach the edge of the grid.
    /// </summary>
    public static Level Create(
        int width,
        int height,
        IEnumerable<int> walls,
        IEnumerable<int> goals,
        int player,
        IEnumerable<int> crates,
        string? title = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidLevelException($"Level size must be positive, got {width}x{height}.");
        }

        var size = width * height;
        var tiles = new Board.Tile[size];
        foreach (var wall in walls)
        {
            tiles[CheckIndex(wall, size, "Wall")] |= Board.Tile.Wall;
        }

        foreach (var goal in goals)
        {
            tiles[CheckIndex(goal, size, "Goal")] |= Board.Tile.Goal;
        }

        CheckIndex(player, size, "Player");
        var crateStarts = crates.Select(c => CheckIndex(c, size, "Crate")).Order().ToImmutableArray();

        if (crateStarts.Distinct().Count() != crateStarts.Length)
        {
            throw new InvalidLevelException("Two crates share a cell.");
        }

        FloodInterior(width, height, tiles, player);

        foreach (var crate in crateStarts)
        {
            if ((tiles[crate] & Board.Tile.Interior) == 0)
            {
                throw new InvalidLevelException($"Crate at {crate} is on a wall or outside the level.");
            }

            if (crate == player)
            {
                throw new InvalidLevelException("Player and crate share a cell.");
            }
        }

        var board = new Board(width, height, tiles);
        if (board.Goals.Count == 0)
        {
            throw new InvalidLevelException("Level has no goals.");
        }

        if (board.Goals.Any(g => !board.IsInterior(g)))
        {
            throw new InvalidLevelException("A goal is on a wall or outside the level.");
        }

        if (board.Goals.Count != crateStarts.Length)
        {
            throw new InvalidLevelException(
                $"Level has {crateStarts.Length} crates but {board.Goals.Count} goals.");
        }

        return new Level(title, board, player, crateStarts);
    }

    private static int CheckIndex(int index, int size, string what)
    {
        if (index < 0 || index >= size)
        {
            throw new InvalidLevelException($"{what} index {index} is outside the grid.");
        }

        return index;
    }

    private static void FloodInterior(int width, int height, Board.Tile[] tiles, int player)
    {
        if ((tiles[player] & Board.Tile.Wall) != 0)
        {
            throw new InvalidLevelException("Player is on a wall.");
        }

        var pending = new Stack<int>();
        pending.Push(player);
        tiles[player] |= Board.Tile.Interior;
        while (pending.Count > 0)
        {
            var cell = pending.Pop();
            int x = cell % width, y = cell / width;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                throw new InvalidLevelException("Level is not enclosed by walls.");
            }

            foreach (var next in (ReadOnlySpan<int>)[cell - 1, cell + 1, cell - width, cell + width])
            {
                if ((tiles[next] & (Board.Tile.Wall | Board.Tile.Interior)) == 0)
                {
                    tiles[next] |= Board.Tile.Interior;
                    pending.Push(next);
                }
            }
        }
    }
}
