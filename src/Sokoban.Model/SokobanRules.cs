using Jentl.Puzzle;
using PuzzleBoard = Jentl.Puzzle.Board;

namespace Sokoban.Model;

/// <summary>
/// Sokoban expressed in the Jentl puzzle kit: its tiles and pieces, its XSB legend, and its rules.
/// The player walks onto free cells and pushes a single crate; a crate pushed onto ice keeps sliding
/// until it is blocked or reaches a cell that is not ice.
/// </summary>
internal sealed class SokobanRules : IPuzzleRules<Direction>
{
    public const string PushTag = "push";

    public static readonly TileKind Floor = new("floor");
    public static readonly TileKind Wall = new("wall", IsSolid: true);
    public static readonly TileKind Goal = new("goal");
    public static readonly TileKind Ice = new("ice");
    public static readonly PieceKind Player = new("player");
    public static readonly PieceKind Crate = new("crate");

    private SokobanRules()
    {
    }

    public static SokobanRules Instance { get; } = new();

    /// <summary>
    /// XSB: <c>#</c> wall, <c>@</c>/<c>+</c> player (on goal), <c>$</c>/<c>*</c> crate (on goal),
    /// <c>.</c> goal, space floor (<c>-</c> and <c>_</c> accepted too), and the ice extension
    /// <c>~</c> ice, <c>=</c> crate on ice, <c>&amp;</c> player on ice.
    /// </summary>
    public static Legend Legend { get; } = new Legend(Floor)
        .Add('#', Wall)
        .Add('.', Goal)
        .Add('~', Ice)
        .Add('@', Floor, Player)
        .Add('+', Goal, Player)
        .Add('&', Ice, Player)
        .Add('$', Floor, Crate)
        .Add('*', Goal, Crate)
        .Add('=', Ice, Crate)
        .Add('-', Floor)
        .Add('_', Floor);

    public bool Play(TurnBuilder turn, Direction input)
    {
        var board = turn.Board;
        var direction = input.ToGrid();
        var player = board.PiecesOf(Player).Single();
        var target = player.Position.Step(direction);
        if (!board.Contains(target) || board.TileAt(target).IsSolid)
        {
            return false;
        }

        if (board.PieceAt(target) is { } crate)
        {
            var rest = target.Step(direction);
            if (!board.IsFree(rest))
            {
                return false;
            }

            while (board.TileAt(rest) == Ice && board.IsFree(rest.Step(direction)))
            {
                rest = rest.Step(direction);
            }

            turn.Move(crate, rest);
            turn.Tag(PushTag);
        }

        turn.Move(player, target);
        return true;
    }

    public bool IsSolved(PuzzleBoard board) => board.CellsOf(Goal).All(c => board.PieceAt(c)?.Kind == Crate);

    /// <summary>A kit board in the level's starting position.</summary>
    public static PuzzleBoard ToBoard(Level level)
    {
        var source = level.Board;
        var board = new PuzzleBoard(source.Width, source.Height, Floor);
        for (var i = 0; i < source.Size; i++)
        {
            var cell = new Cell(source.XOf(i), source.YOf(i));
            board.SetTile(cell, source.IsWall(i) ? Wall : source.IsGoal(i) ? Goal : source.IsIce(i) ? Ice : Floor);
        }

        board.Place(Player, new Cell(source.XOf(level.PlayerStart), source.YOf(level.PlayerStart)));
        foreach (var crate in level.CrateStarts)
        {
            board.Place(Crate, new Cell(source.XOf(crate), source.YOf(crate)));
        }

        return board;
    }
}

internal static class DirectionMapping
{
    public static GridDirection ToGrid(this Direction direction) => direction switch
    {
        Direction.Up => GridDirection.Up,
        Direction.Down => GridDirection.Down,
        Direction.Left => GridDirection.Left,
        Direction.Right => GridDirection.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };
}
