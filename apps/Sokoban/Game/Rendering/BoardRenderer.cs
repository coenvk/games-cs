using Jentl.Core.Graphics;
using Jentl.Core.Puzzle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;

namespace Games.Rendering;

/// <summary>A step to animate: the player moving, and optionally the crate it pushed (or pulled back on undo).</summary>
internal readonly record struct Step(Direction Facing, int PlayerFrom, int PlayerTo, int? CrateFrom = null, int? CrateTo = null);

/// <summary>
/// Draws a level session centred in a target rectangle, scaled to fit. The model moves
/// instantly; the renderer slides the player and pushed crate between cells to catch up.
/// Layout, hit testing and animation come from the puzzle kit's <see cref="GridView"/>.
/// </summary>
internal sealed class BoardRenderer
{
    private const float MaxScale = 2f;

    // Animation ids: only the player and the most recently pushed crate are ever moving.
    private const int PlayerId = 0;
    private const int CrateId = 1;

    // Close to the key-repeat interval, so holding a direction reads as continuous walking.
    private static readonly TimeSpan StepDuration = TimeSpan.FromMilliseconds(85);
    private static readonly TimeSpan SlidePerCell = TimeSpan.FromMilliseconds(55);

    private static readonly Color DeadOverlay = new(235, 40, 40, 205);

    private readonly Tileset _tileset;
    private readonly GridView _view = new() { MaxTile = (int)(Tileset.TileSize * MaxScale) };
    private Board? _board;
    private Direction _facing = Direction.Down;
    private int _frame;

    public BoardRenderer(Tileset tileset) => _tileset = tileset;

    /// <summary>Size of a cell on screen as last drawn, in pixels; 0 before the first draw.</summary>
    public int TileSize => _board is null ? 0 : _view.Tile;

    public void Animate(Board board, Step step)
    {
        ArgumentNullException.ThrowIfNull(board);
        _frame = step.Facing == _facing ? _frame + 1 : 0;
        _facing = step.Facing;
        _view.Animate(PlayerId, GridPosition(board, step.PlayerFrom), GridPosition(board, step.PlayerTo), StepDuration);

        if (step is { CrateFrom: { } from, CrateTo: { } to })
        {
            // A slide across ice takes longer and eases out, so the crate visibly glides to a stop.
            var cells = Vector2.Distance(GridPosition(board, from), GridPosition(board, to));
            if (cells > 1.5f)
            {
                _view.Animate(CrateId, GridPosition(board, from), GridPosition(board, to), StepDuration + SlidePerCell * (cells - 1), Easing.CubicOut);
            }
            else
            {
                _view.Animate(CrateId, GridPosition(board, from), GridPosition(board, to), StepDuration);
            }
        }
    }

    public void Update(TimeSpan elapsed) => _view.Update(elapsed);

    /// <summary>The cell under a screen point as last drawn, or null when the point is off the board.</summary>
    public int? CellAt(Vector2 point) =>
        _board is not null && _view.CellAt(point) is { } cell ? _board.IndexOf(cell.X, cell.Y) : null;

    /// <summary>Stops any animation and faces the player forward, e.g. after a restart.</summary>
    public void Reset()
    {
        _facing = Direction.Down;
        _frame = 0;
        _view.Stop();
    }

    /// <param name="deadCrates">Crates that can no longer be solved; marked red as a warning.</param>
    public void Draw(SpriteBatch batch, LevelSession session, Theme theme, Rectangle area, IReadOnlySet<int>? deadCrates = null)
    {
        var board = session.Board;
        _board = board;
        _view.Fit(area, board.Width, board.Height);

        // Tiles first, so a sliding crate or player is never painted over by a neighbouring floor.
        for (var i = 0; i < board.Size; i++)
        {
            var cell = _view.Bounds(GridPosition(board, i));
            if (board.IsWall(i))
            {
                batch.Draw(theme.Wall, cell, Color.White);
            }
            else if (board.IsInterior(i))
            {
                batch.Draw(board.IsIce(i) ? _tileset.Ice : theme.Floor, cell, Color.White);
                if (board.IsGoal(i))
                {
                    batch.Draw(theme.Goal, cell, Color.White);
                }
            }
        }

        foreach (var crate in session.Crates)
        {
            var bounds = _view.Bounds(_view.PositionOf(CrateId, GridPosition(board, crate)));
            batch.Draw(board.IsGoal(crate) ? theme.CrateOnGoal : theme.Crate, bounds, Color.White);
            if (deadCrates?.Contains(crate) == true)
            {
                // An overlay rather than a tint, so it reads as red whatever colour the crate is.
                bounds.Inflate(-_view.Tile / 10, -_view.Tile / 10);
                batch.FillRectangle(bounds, DeadOverlay);
            }
        }

        var frames = _tileset.PlayerFrames(_facing);
        var player = _view.Bounds(_view.PositionOf(PlayerId, GridPosition(board, session.Player)));
        batch.Draw(frames[_frame % frames.Length], player, Color.White);
    }

    private static Vector2 GridPosition(Board board, int index) => new(board.XOf(index), board.YOf(index));
}
