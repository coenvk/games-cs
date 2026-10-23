using System.Text;

namespace Sokoban.Model;

public enum MoveResult
{
    Blocked,
    Walked,
    Pushed,
}

public readonly record struct Move(Direction Direction, bool IsPush);

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

    public IEnumerable<int> Crates => Enumerable.Range(0, _crates.Length).Where(i => _crates[i]);

    public bool HasCrate(int index) => _crates[index];

    /// <summary>Moves taken so far, oldest first.</summary>
    public IEnumerable<Move> History => _undo.Reverse();

    public MoveResult TryMove(Direction direction)
    {
        var result = Apply(direction);
        if (result != MoveResult.Blocked)
        {
            _undo.Push(new Move(direction, result == MoveResult.Pushed));
            _redo.Clear();
        }

        return result;
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
            MoveCrate(Neighbor(Player, move.Direction), Player);
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

    private MoveResult Apply(Direction direction)
    {
        if (!Board.TryGetNeighbor(Player, direction, out var target) || Board.IsWall(target))
        {
            return MoveResult.Blocked;
        }

        if (!_crates[target])
        {
            Player = target;
            return MoveResult.Walked;
        }

        if (!Board.TryGetNeighbor(target, direction, out var beyond) || Board.IsWall(beyond) || _crates[beyond])
        {
            return MoveResult.Blocked;
        }

        MoveCrate(target, beyond);
        Player = target;
        PushCount++;
        return MoveResult.Pushed;
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
