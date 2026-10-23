using Sokoban.Model;

namespace Sokoban.Solver;

public enum SolveStatus
{
    /// <summary><see cref="SolveResult.Moves"/> solves the position.</summary>
    Solved,

    /// <summary>The whole reachable state space was searched without finding a solution.</summary>
    Unsolvable,

    /// <summary>The node budget ran out first; the position may or may not be solvable.</summary>
    LimitReached,

    Cancelled,
}

public sealed record SolveResult(SolveStatus Status, IReadOnlyList<Move> Moves, int Pushes, int Explored)
{
    internal static SolveResult Failed(SolveStatus status, int explored) => new(status, [], 0, explored);
}

/// <summary>
/// A* search over push states: each step is one crate push, positions are normalised to the
/// player's reachable area, and dead squares and frozen 2x2 blocks are pruned. The heuristic
/// (cheapest matching of crates to distinct goals by push distance) never overestimates, so solutions
/// use few pushes, though not necessarily the fewest moves.
/// </summary>
public sealed class Solver
{
    private static readonly Direction[] Directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

    private readonly Board _board;
    private readonly ulong[] _crateKeys;
    private readonly ulong[] _playerKeys;

    public Solver(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        _board = board;
        Deadlocks = new DeadlockMap(board);

        // Zobrist hashing: a position's key is the XOR of one random value per crate cell
        // and one for the player's (normalised) cell. Collisions at 64 bits are negligible here.
        var random = new Random(0x5_0C0B4A);
        _crateKeys = Enumerable.Range(0, board.Size).Select(_ => NextKey(random)).ToArray();
        _playerKeys = Enumerable.Range(0, board.Size).Select(_ => NextKey(random)).ToArray();
    }

    public DeadlockMap Deadlocks { get; }

    public SolveResult Solve(LevelSession session, SolverOptions? options = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Solve(session.Player, session.Crates.ToArray(), options, cancellation);
    }

    public SolveResult Solve(int player, IReadOnlyCollection<int> crates, SolverOptions? options = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(crates);
        options ??= SolverOptions.Default;
        var startCrates = crates.Order().ToArray();
        var occupied = new bool[_board.Size];
        foreach (var crate in startCrates)
        {
            occupied[crate] = true;
        }

        foreach (var crate in startCrates)
        {
            if (!_board.IsGoal(crate) && (Deadlocks.IsDeadSquare(crate) || Deadlocks.IsInFrozenSquare(crate, c => occupied[c])))
            {
                return SolveResult.Failed(SolveStatus.Unsolvable, 0);
            }
        }

        var heuristic = new MatchingHeuristic(_board);
        var startH = heuristic.Estimate(startCrates);
        if (startH == MatchingHeuristic.Unsolvable)
        {
            return SolveResult.Failed(SolveStatus.Unsolvable, 0);
        }

        var start = new Node(startCrates, HashCrates(startCrates), player, 0, startH, null, -1, default);
        var open = new PriorityQueue<Node, (int F, int H)>();
        open.Enqueue(start, (start.H, start.H));
        var closed = new HashSet<ulong>();
        var reach = new Reachability(_board.Size);
        var explored = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (open.TryDequeue(out var node, out _))
        {
            if (cancellation.IsCancellationRequested)
            {
                return SolveResult.Failed(SolveStatus.Cancelled, explored);
            }

            Array.Clear(occupied);
            foreach (var crate in node.Crates)
            {
                occupied[crate] = true;
            }

            var normalised = reach.Flood(_board, node.Player, occupied);
            if (!closed.Add(node.CratesHash ^ _playerKeys[normalised]))
            {
                continue;
            }

            if (node.H == 0)
            {
                return Reconstruct(node, player, startCrates, explored);
            }

            // Checking the clock every node would cost more than it saves.
            if (++explored > options.MaxNodes || (explored % 512 == 0 && clock.Elapsed > options.TimeBudget))
            {
                return SolveResult.Failed(SolveStatus.LimitReached, explored);
            }

            Expand(node, occupied, reach, open, heuristic, options.Weight);
        }

        return SolveResult.Failed(SolveStatus.Unsolvable, explored);
    }

    private void Expand(Node node, bool[] occupied, Reachability reach, PriorityQueue<Node, (int F, int H)> open, MatchingHeuristic heuristic, int weight)
    {
        for (var i = 0; i < node.Crates.Length; i++)
        {
            var crate = node.Crates[i];
            foreach (var direction in Directions)
            {
                if (!_board.TryGetNeighbor(crate, direction.Opposite(), out var behind) || !reach.Contains(behind)
                    || !_board.TryGetNeighbor(crate, direction, out var target)
                    || _board.IsWall(target) || occupied[target] || Deadlocks.IsDeadSquare(target))
                {
                    continue;
                }

                occupied[crate] = false;
                occupied[target] = true;
                var frozen = !_board.IsGoal(target) && Deadlocks.IsInFrozenSquare(target, c => occupied[c]);
                occupied[target] = false;
                occupied[crate] = true;
                if (frozen)
                {
                    continue;
                }

                var crates = (int[])node.Crates.Clone();
                crates[i] = target;
                Array.Sort(crates);
                var h = heuristic.Estimate(crates);
                if (h == MatchingHeuristic.Unsolvable)
                {
                    continue;
                }

                var hash = node.CratesHash ^ _crateKeys[crate] ^ _crateKeys[target];
                var child = new Node(crates, hash, crate, node.G + 1, h, node, crate, direction);

                // Ties on f go to the node closer to the goal, which keeps the search moving forward.
                open.Enqueue(child, (child.G + weight * h, h));
            }
        }
    }

    /// <summary>Turns the chain of pushes into full moves, walking the player between pushes.</summary>
    private SolveResult Reconstruct(Node goal, int startPlayer, int[] startCrates, int explored)
    {
        var pushes = new List<(int From, Direction Direction)>();
        for (var node = goal; node.Parent is not null; node = node.Parent)
        {
            pushes.Add((node.PushedFrom, node.PushDirection));
        }

        pushes.Reverse();

        var occupied = new bool[_board.Size];
        foreach (var crate in startCrates)
        {
            occupied[crate] = true;
        }

        var moves = new List<Move>();
        var player = startPlayer;
        foreach (var (from, direction) in pushes)
        {
            _board.TryGetNeighbor(from, direction.Opposite(), out var behind);
            moves.AddRange(Walk(player, behind, occupied).Select(d => new Move(d, IsPush: false)));
            moves.Add(new Move(direction, IsPush: true));
            _board.TryGetNeighbor(from, direction, out var target);
            occupied[from] = false;
            occupied[target] = true;
            player = from;
        }

        return new SolveResult(SolveStatus.Solved, moves, pushes.Count, explored);
    }

    /// <summary>Shortest walk between two cells around crates (breadth-first).</summary>
    private List<Direction> Walk(int from, int to, bool[] occupied)
    {
        var cameFrom = new Direction?[_board.Size];
        var visited = new bool[_board.Size];
        var queue = new Queue<int>();
        visited[from] = true;
        queue.Enqueue(from);
        while (queue.TryDequeue(out var cell) && cell != to)
        {
            foreach (var direction in Directions)
            {
                if (_board.TryGetNeighbor(cell, direction, out var next) && !visited[next] && !_board.IsWall(next) && !occupied[next])
                {
                    visited[next] = true;
                    cameFrom[next] = direction;
                    queue.Enqueue(next);
                }
            }
        }

        var path = new List<Direction>();
        for (var cell = to; cell != from;)
        {
            var direction = cameFrom[cell] ?? throw new InvalidOperationException("Solver produced an unreachable push.");
            path.Add(direction);
            _board.TryGetNeighbor(cell, direction.Opposite(), out cell);
        }

        path.Reverse();
        return path;
    }

    private ulong HashCrates(int[] crates)
    {
        var hash = 0UL;
        foreach (var crate in crates)
        {
            hash ^= _crateKeys[crate];
        }

        return hash;
    }

#pragma warning disable CA5394 // Hash keys, not security relevant.
    private static ulong NextKey(Random random) => (ulong)random.NextInt64() ^ ((ulong)random.Next() << 62);
#pragma warning restore CA5394

    /// <param name="Player">Where the player stands after the push that led here.</param>
    /// <param name="PushedFrom">The cell the crate was pushed from (-1 for the start node).</param>
    private sealed record Node(
        int[] Crates,
        ulong CratesHash,
        int Player,
        int G,
        int H,
        Node? Parent,
        int PushedFrom,
        Direction PushDirection);

    /// <summary>Flood fill of the player's reachable area, reused between nodes to avoid allocation.</summary>
    private sealed class Reachability(int size)
    {
        private readonly int[] _stamp = new int[size];
        private readonly Stack<int> _pending = new();
        private int _current;

        public bool Contains(int cell) => _stamp[cell] == _current;

        /// <summary>Marks every cell the player can reach and returns the smallest, as a canonical position.</summary>
        public int Flood(Board board, int player, bool[] occupied)
        {
            _current++;
            _stamp[player] = _current;
            _pending.Push(player);
            var min = player;
            while (_pending.TryPop(out var cell))
            {
                min = Math.Min(min, cell);
                foreach (var direction in Directions)
                {
                    if (board.TryGetNeighbor(cell, direction, out var next)
                        && _stamp[next] != _current && !board.IsWall(next) && !occupied[next])
                    {
                        _stamp[next] = _current;
                        _pending.Push(next);
                    }
                }
            }

            return min;
        }
    }
}
