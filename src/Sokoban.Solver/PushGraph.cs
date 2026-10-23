using Sokoban.Model;

namespace Sokoban.Solver;

/// <summary>
/// Where a lone crate can get to by pushing, ignoring other crates except for one thing: on ice
/// another crate might stop a slide anywhere, so every cell along a slide counts as a possible stop.
/// That over-approximates real crate movement, which keeps everything derived from it safe:
/// a cell found dead really is dead, and push distances never overestimate.
/// </summary>
internal static class PushGraph
{
    private static readonly Direction[] Directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

    /// <summary>Fewest pushes from each cell to the nearest of <paramref name="goals"/>; <see cref="int.MaxValue"/> if none can be reached.</summary>
    public static int[] DistancesTo(Board board, IEnumerable<int> goals)
    {
        var predecessors = Predecessors(board);
        var distance = new int[board.Size];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        foreach (var goal in goals)
        {
            distance[goal] = 0;
            queue.Enqueue(goal);
        }

        while (queue.TryDequeue(out var cell))
        {
            foreach (var from in predecessors[cell])
            {
                if (distance[from] == int.MaxValue)
                {
                    distance[from] = distance[cell] + 1;
                    queue.Enqueue(from);
                }
            }
        }

        return distance;
    }

    /// <summary>For each cell, the cells a crate can be pushed from to stop there in one push.</summary>
    private static List<int>[] Predecessors(Board board)
    {
        var predecessors = new List<int>[board.Size];
        for (var i = 0; i < board.Size; i++)
        {
            predecessors[i] = [];
        }

        for (var crate = 0; crate < board.Size; crate++)
        {
            if (!board.IsInterior(crate))
            {
                continue;
            }

            foreach (var direction in Directions)
            {
                if (!board.TryGetNeighbor(crate, direction.Opposite(), out var player) || !board.IsInterior(player)
                    || !board.TryGetNeighbor(crate, direction, out var stop) || !board.IsInterior(stop))
                {
                    continue;
                }

                predecessors[stop].Add(crate);
                while (board.IsIce(stop) && board.TryGetNeighbor(stop, direction, out var next) && board.IsInterior(next))
                {
                    stop = next;
                    predecessors[stop].Add(crate);
                }
            }
        }

        return predecessors;
    }
}
