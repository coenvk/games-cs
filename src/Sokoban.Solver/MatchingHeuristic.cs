using Sokoban.Model;

namespace Sokoban.Solver;

/// <summary>
/// Lower bound on pushes: the cheapest way to assign every crate its own goal, where the cost
/// of a pair is the pushes needed for that crate to reach that goal on an otherwise empty board.
/// Solved with the Hungarian algorithm, O(n³) for n crates.
/// </summary>
internal sealed class MatchingHeuristic
{
    /// <summary>Returned when crates cannot all be matched to distinct goals they can reach: a deadlock.</summary>
    public const int Unsolvable = int.MaxValue;

    // Large enough to never be chosen when a finite matching exists, small enough not to overflow sums.
    private const int Infinity = 1_000_000;

    private readonly int[][] _distance; // [goal][cell]
    private readonly int _n;

    // Scratch arrays, reused between calls (one heuristic per search, so no sharing across threads).
    private readonly int[] _u;
    private readonly int[] _v;
    private readonly int[] _match;
    private readonly int[] _way;
    private readonly int[] _minV;
    private readonly bool[] _used;

    public MatchingHeuristic(Board board)
    {
        _n = board.Goals.Count;
        _distance = board.Goals.Select(goal => PullDistances(board, goal)).ToArray();
        _u = new int[_n + 1];
        _v = new int[_n + 1];
        _match = new int[_n + 1];
        _way = new int[_n + 1];
        _minV = new int[_n + 1];
        _used = new bool[_n + 1];
    }

    public int Estimate(int[] crates)
    {
        // Hungarian algorithm (shortest augmenting paths with potentials), 1-based rows = crates, columns = goals.
        Array.Clear(_u);
        Array.Clear(_v);
        Array.Clear(_match);
        for (var row = 1; row <= _n; row++)
        {
            _match[0] = row;
            var column = 0;
            Array.Fill(_minV, int.MaxValue);
            Array.Clear(_used);
            do
            {
                _used[column] = true;
                int matchedRow = _match[column], delta = int.MaxValue, next = 0;
                for (var j = 1; j <= _n; j++)
                {
                    if (_used[j])
                    {
                        continue;
                    }

                    var cost = Cost(matchedRow - 1, j - 1, crates) - _u[matchedRow] - _v[j];
                    if (cost < _minV[j])
                    {
                        _minV[j] = cost;
                        _way[j] = column;
                    }

                    if (_minV[j] < delta)
                    {
                        delta = _minV[j];
                        next = j;
                    }
                }

                for (var j = 0; j <= _n; j++)
                {
                    if (_used[j])
                    {
                        _u[_match[j]] += delta;
                        _v[j] -= delta;
                    }
                    else
                    {
                        _minV[j] -= delta;
                    }
                }

                column = next;
            }
            while (_match[column] != 0);

            do
            {
                var previous = _way[column];
                _match[column] = _match[previous];
                column = previous;
            }
            while (column != 0);
        }

        var total = 0;
        for (var j = 1; j <= _n; j++)
        {
            var cost = Cost(_match[j] - 1, j - 1, crates);
            if (cost >= Infinity)
            {
                return Unsolvable;
            }

            total += cost;
        }

        return total;
    }

    private int Cost(int crateIndex, int goalIndex, int[] crates)
    {
        var distance = _distance[goalIndex][crates[crateIndex]];
        return distance == int.MaxValue ? Infinity : distance;
    }

    /// <summary>Pushes needed to bring a crate from each cell to <paramref name="goal"/>, found by pulling backwards.</summary>
    private static int[] PullDistances(Board board, int goal)
    {
        var distance = new int[board.Size];
        Array.Fill(distance, int.MaxValue);
        distance[goal] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(goal);
        while (queue.TryDequeue(out var p))
        {
            foreach (var d in (ReadOnlySpan<Direction>)[Direction.Up, Direction.Down, Direction.Left, Direction.Right])
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
