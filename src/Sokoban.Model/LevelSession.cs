using System.Text;

namespace Sokoban.Model;

public enum MoveResult
{
    Blocked,
    Walked,
    Pushed,
}

/// <param name="Slide">For a push onto ice: how many cells the crate slid beyond the first.</param>
public readonly record struct Move(Direction Direction, bool IsPush, int Slide = 0);

/// <summary>The mutable play state of one level, with undo and redo.</summary>
public sealed class LevelSession
{
    private readonly bool[] _crates;
    private readonly Stack<Move> _undo = new();
    private readonly Stack<Move> _redo = new();
    private int _cratesOnGoals;

    public LevelSession(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Level = level;
        _crates = new bool[level.Board.Size];
        Restart();
    }

    public Level Level { get; }

    public Board Board => Level.Board;

    public int Player { get; private set; }

    public int MoveCount => _undo.Count;

    public int PushCount { get; private set; }

    public bool IsSolved => _cratesOnGoals == Board.Goals.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>The move that <see cref="Undo"/> would revert, if any.</summary>
    public Move? LastMove => _undo.TryPeek(out var move) ? move : null;

    public IEnumerable<int> Crates => Enumerable.Range(0, _crates.Length).Where(i => _crates[i]);

    public bool HasCrate(int index) => _crates[index];

    /// <summary>Moves taken so far, oldest first.</summary>
    public IEnumerable<Move> History => _undo.Reverse();

    public MoveResult TryMove(Direction direction)
    {
        var (result, slide) = Apply(direction);
        if (result != MoveResult.Blocked)
        {
            _undo.Push(new Move(direction, result == MoveResult.Pushed, slide));
            _redo.Clear();
        }

        return result;
    }

    /// <summary>Where the crate pushed by <paramref name="move"/> came to rest, seen from the player after the move.</summary>
    public int CrateTarget(Move move, int player)
    {
        var cell = player;
        for (var i = 0; i <= move.Slide; i++)
        {
            cell = Neighbor(cell, move.Direction);
        }

        return cell;
    }

    /// <summary>
    /// The shortest walk from the player to <paramref name="target"/> around walls and crates, or null if
    /// the player cannot get there without pushing. Empty when the player already stands on it.
    /// </summary>
    public IReadOnlyList<Direction>? FindPath(int target)
    {
        if (target < 0 || target >= Board.Size || Board.IsWall(target) || _crates[target] || !Board.IsInterior(target))
        {
            return null;
        }

        var cameFrom = new Direction?[Board.Size];
        var visited = new bool[Board.Size];
        var queue = new Queue<int>();
        visited[Player] = true;
        queue.Enqueue(Player);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == target)
            {
                var path = new List<Direction>();
                for (var at = target; at != Player;)
                {
                    var step = cameFrom[at]!.Value;
                    path.Add(step);
                    at = Neighbor(at, step.Opposite());
                }

                path.Reverse();
                return path;
            }

            foreach (var direction in (ReadOnlySpan<Direction>)[Direction.Up, Direction.Down, Direction.Left, Direction.Right])
            {
                if (Board.TryGetNeighbor(cell, direction, out var next) && !visited[next] && !Board.IsWall(next) && !_crates[next])
                {
                    visited[next] = true;
                    cameFrom[next] = direction;
                    queue.Enqueue(next);
                }
            }
        }

        return null;
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var move))
        {
            return false;
        }

        var previous = Neighbor(Player, move.Direction.Opposite());
        if (move.IsPush)
        {
            MoveCrate(CrateTarget(move, Player), Player);
            PushCount--;
        }

        Player = previous;
        _redo.Push(move);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var move))
        {
            return false;
        }

        // Redo replays a move that was legal from this exact state, so it cannot be blocked.
        Apply(move.Direction);
        _undo.Push(move);
        return true;
    }

    public void Restart()
    {
        Array.Clear(_crates);
        _cratesOnGoals = 0;
        foreach (var crate in Level.CrateStarts)
        {
            _crates[crate] = true;
            if (Board.IsGoal(crate))
            {
                _cratesOnGoals++;
            }
        }

        Player = Level.PlayerStart;
        PushCount = 0;
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>The history in LURD notation, e.g. <c>"rrUl"</c>.</summary>
    public string ToLurd()
    {
        var sb = new StringBuilder(_undo.Count);
        foreach (var move in History)
        {
            sb.Append(move.Direction.ToLurd(move.IsPush));
        }

        return sb.ToString();
    }

    private (MoveResult Result, int Slide) Apply(Direction direction)
    {
        if (!Board.TryGetNeighbor(Player, direction, out var target) || Board.IsWall(target))
        {
            return (MoveResult.Blocked, 0);
        }

        if (!_crates[target])
        {
            Player = target;
            return (MoveResult.Walked, 0);
        }

        if (!Board.TryGetNeighbor(target, direction, out var beyond) || Board.IsWall(beyond) || _crates[beyond])
        {
            return (MoveResult.Blocked, 0);
        }

        var slide = 0;
        while (Board.IsIce(beyond) && Board.TryGetNeighbor(beyond, direction, out var next) && !Board.IsWall(next) && !_crates[next])
        {
            beyond = next;
            slide++;
        }

        MoveCrate(target, beyond);
        Player = target;
        PushCount++;
        return (MoveResult.Pushed, slide);
    }

    private void MoveCrate(int from, int to)
    {
        _crates[from] = false;
        _crates[to] = true;
        _cratesOnGoals += (Board.IsGoal(to) ? 1 : 0) - (Board.IsGoal(from) ? 1 : 0);
    }

    private int Neighbor(int index, Direction direction) =>
        Board.TryGetNeighbor(index, direction, out var neighbor)
            ? neighbor
            : throw new InvalidOperationException("Move history is inconsistent with the board.");
}
