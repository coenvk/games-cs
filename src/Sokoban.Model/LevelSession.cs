using System.Text;
using Jentl.Puzzle;
using PuzzleBoard = Jentl.Puzzle.Board;

namespace Sokoban.Model;

public enum MoveResult
{
    Blocked,
    Walked,
    Pushed,
}

/// <param name="Slide">For a push onto ice: how many cells the crate slid beyond the first.</param>
public readonly record struct Move(Direction Direction, bool IsPush, int Slide = 0);

/// <summary>
/// The play state of one level, with undo and redo. Runs on the Jentl puzzle kit
/// (<see cref="PuzzleGame{TInput}"/> with <see cref="SokobanRules"/>) and presents it in Sokoban terms:
/// cells by flat index, moves with their push and slide.
/// </summary>
public sealed class LevelSession
{
    private readonly PuzzleGame<Direction> _game;
    private readonly Piece _player;

    public LevelSession(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Level = level;
        PuzzleBoard = SokobanRules.ToBoard(level);
        _player = PuzzleBoard.PiecesOf(SokobanRules.Player).Single();
        _game = new PuzzleGame<Direction>(PuzzleBoard, SokobanRules.Instance);
    }

    public Level Level { get; }

    public Board Board => Level.Board;

    public int Player => IndexOf(_player.Position);

    public int MoveCount => _game.History.Count;

    public int PushCount => _game.CountTagged(SokobanRules.PushTag);

    public bool IsSolved => _game.IsSolved;

    public bool CanUndo => _game.CanUndo;

    public bool CanRedo => _game.CanRedo;

    /// <summary>The move that <see cref="Undo"/> would revert, if any.</summary>
    public Move? LastMove => _game.LastTurn is { } turn ? ToMove(turn) : null;

    public IEnumerable<int> Crates => PuzzleBoard.PiecesOf(SokobanRules.Crate).Select(p => IndexOf(p.Position)).Order();

    /// <summary>Moves taken so far, oldest first.</summary>
    public IEnumerable<Move> History => _game.History.Select(ToMove);

    /// <summary>The kit board the game plays on, in its current position.</summary>
    internal PuzzleBoard PuzzleBoard { get; }

    public bool HasCrate(int index) => PuzzleBoard.PieceAt(CellOf(index))?.Kind == SokobanRules.Crate;

    public MoveResult TryMove(Direction direction) => _game.Play(direction) switch
    {
        null => MoveResult.Blocked,
        var turn when turn.Has(SokobanRules.PushTag) => MoveResult.Pushed,
        _ => MoveResult.Walked,
    };

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
        if (target < 0 || target >= Board.Size || Board.IsWall(target) || HasCrate(target) || !Board.IsInterior(target))
        {
            return null;
        }

        var start = Player;
        var cameFrom = new Direction?[Board.Size];
        var visited = new bool[Board.Size];
        var queue = new Queue<int>();
        visited[start] = true;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == target)
            {
                var path = new List<Direction>();
                for (var at = target; at != start;)
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
                if (Board.TryGetNeighbor(cell, direction, out var next) && !visited[next] && !Board.IsWall(next) && !HasCrate(next))
                {
                    visited[next] = true;
                    cameFrom[next] = direction;
                    queue.Enqueue(next);
                }
            }
        }

        return null;
    }

    public bool Undo() => _game.Undo() is not null;

    public bool Redo() => _game.Redo() is not null;

    public void Restart() => _game.Restart();

    /// <summary>The history in LURD notation, e.g. <c>"rrUl"</c>.</summary>
    public string ToLurd()
    {
        var sb = new StringBuilder(MoveCount);
        foreach (var move in History)
        {
            sb.Append(move.Direction.ToLurd(move.IsPush));
        }

        return sb.ToString();
    }

    private static Move ToMove(Turn<Direction> turn)
    {
        var crate = turn.Moves.FirstOrDefault(m => m.Piece.Kind == SokobanRules.Crate);
        var slide = crate is null ? 0 : Math.Abs(crate.To.X - crate.From.X) + Math.Abs(crate.To.Y - crate.From.Y) - 1;
        return new Move(turn.Input, crate is not null, slide);
    }

    private int IndexOf(Cell cell) => Board.IndexOf(cell.X, cell.Y);

    private Cell CellOf(int index) => new(Board.XOf(index), Board.YOf(index));

    private int Neighbor(int index, Direction direction) =>
        Board.TryGetNeighbor(index, direction, out var neighbor)
            ? neighbor
            : throw new InvalidOperationException("Move history is inconsistent with the board.");
}
