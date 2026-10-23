using Sokoban.Model;

namespace Sokoban.Solver;

/// <summary>
/// Static analysis of a board: which cells a crate can still be pushed to a goal from
/// (and in how many pushes, at least), plus a check for crates frozen in a 2x2 block.
/// </summary>
public sealed class DeadlockMap
{
    private static readonly Direction[] Directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

    private readonly Board _board;
    private readonly int[] _distance;

    public DeadlockMap(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        _board = board;
        _distance = ComputeGoalDistances(board);
    }

    /// <summary>True for floor cells from which no sequence of pushes can bring a crate to any goal.</summary>
    public bool IsDeadSquare(int cell) => _distance[cell] == int.MaxValue;

    /// <summary>
    /// A lower bound on pushes needed to bring a crate from <paramref name="cell"/> to the nearest goal,
    /// ignoring other crates; <see cref="int.MaxValue"/> on a dead square.
    /// </summary>
    public int GoalDistance(int cell) => _distance[cell];

    /// <summary>
    /// True when the crate at <paramref name="crate"/> is part of a 2x2 block of walls and crates
    /// that holds a crate off its goal. No crate in such a block can ever move again.
    /// </summary>
    public bool IsInFrozenSquare(int crate, Func<int, bool> hasCrate)
    {
        ArgumentNullException.ThrowIfNull(hasCrate);
        int x = _board.XOf(crate), y = _board.YOf(crate);
        for (var top = y - 1; top <= y; top++)
        {
            for (var left = x - 1; left <= x; left++)
            {
                if (IsFrozenSquare(left, top, hasCrate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Crates off their goal that can never be solved: on a dead square or in a frozen block.</summary>
    public IReadOnlySet<int> DeadCrates(LevelSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var dead = new HashSet<int>();
        foreach (var crate in session.Crates)
        {
            if (!_board.IsGoal(crate) && (IsDeadSquare(crate) || IsInFrozenSquare(crate, session.HasCrate)))
            {
                dead.Add(crate);
            }
        }

        return dead;
    }

    private bool IsFrozenSquare(int left, int top, Func<int, bool> hasCrate)
    {
        var offGoalCrate = false;
        for (var dy = 0; dy < 2; dy++)
        {
            for (var dx = 0; dx < 2; dx++)
            {
                int x = left + dx, y = top + dy;
                if (x < 0 || y < 0 || x >= _board.Width || y >= _board.Height)
                {
                    continue; // Outside the grid counts as wall.
                }

                var cell = _board.IndexOf(x, y);
                if (_board.IsWall(cell))
                {
                    continue;
                }

                if (!hasCrate(cell))
                {
                    return false;
                }

                offGoalCrate |= !_board.IsGoal(cell);
            }
        }

        return offGoalCrate;
    }

    // Breadth-first search backwards from every goal, "pulling" a crate: a crate at q pushed
    // in direction d lands on p = q + d, which needs the player to stand on q - d.
    private static int[] ComputeGoalDistances(Board board)
    {
        var distance = new int[board.Size];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        foreach (var goal in board.Goals)
        {
            distance[goal] = 0;
            queue.Enqueue(goal);
        }

        while (queue.TryDequeue(out var p))
        {
            foreach (var d in Directions)
            {
                var back = d.Opposite();
                if (board.TryGetNeighbor(p, back, out var q)
                    && board.TryGetNeighbor(q, back, out var playerCell)
                    && board.IsInterior(q)
                    && board.IsInterior(playerCell)
                    && distance[q] == int.MaxValue)
                {
                    distance[q] = distance[p] + 1;
                    queue.Enqueue(q);
                }
            }
        }

        return distance;
    }
}
