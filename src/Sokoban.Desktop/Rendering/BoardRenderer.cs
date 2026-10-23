using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;

namespace Sokoban.Desktop.Rendering;

/// <summary>A step to animate: the player moving, and optionally the crate it pushed (or pulled back on undo).</summary>
internal readonly record struct Step(Direction Facing, int PlayerFrom, int PlayerTo, int? CrateFrom = null, int? CrateTo = null);

/// <summary>
/// Draws a level session centred in a target rectangle, scaled to fit. The model moves
/// instantly; the renderer slides the player and pushed crate between cells to catch up.
/// </summary>
internal sealed class BoardRenderer
{
    private const float MaxScale = 2f;

    // Close to the key-repeat interval, so holding a direction reads as continuous walking.
    private static readonly TimeSpan StepDuration = TimeSpan.FromMilliseconds(85);
    private static readonly TimeSpan SlidePerCell = TimeSpan.FromMilliseconds(55);

    private static readonly Color DeadOverlay = new(235, 40, 40, 205);

    private readonly Tileset _tileset;
    private Direction _facing = Direction.Down;
    private int _frame;
    private Tween? _player;
    private int _playerTarget = -1;
    private Tween? _crate;
    private int _crateTarget = -1;

    public BoardRenderer(Tileset tileset) => _tileset = tileset;

    public void Animate(Board board, Step step)
    {
        ArgumentNullException.ThrowIfNull(board);
        _frame = step.Facing == _facing ? _frame + 1 : 0;
        _facing = step.Facing;

        // Start from wherever the sprite is drawn now, so quick successive moves chain smoothly.
        var playerStart = _player is not null && _playerTarget == step.PlayerFrom ? _player.Value : GridPosition(board, step.PlayerFrom);
        _player = new Tween(playerStart, GridPosition(board, step.PlayerTo), StepDuration);
        _playerTarget = step.PlayerTo;

        if (step is { CrateFrom: { } from, CrateTo: { } to })
        {
            var crateStart = _crate is not null && _crateTarget == from ? _crate.Value : GridPosition(board, from);
            var crateEnd = GridPosition(board, to);

            // A slide across ice takes longer and eases out, so the crate visibly glides to a stop.
            var cells = Vector2.Distance(GridPosition(board, from), crateEnd);
            _crate = cells > 1.5f
                ? new Tween(crateStart, crateEnd, StepDuration + SlidePerCell * (cells - 1), Easing.CubicOut)
                : new Tween(crateStart, crateEnd, StepDuration);
            _crateTarget = to;
        }
    }

    public void Update(TimeSpan elapsed)
    {
        _player?.Update(elapsed);
        _crate?.Update(elapsed);
    }

    /// <summary>Stops any animation and faces the player forward, e.g. after a restart.</summary>
    public void Reset()
    {
        _facing = Direction.Down;
        _frame = 0;
        _player = null;
        _playerTarget = -1;
        _crate = null;
        _crateTarget = -1;
    }

    /// <param name="deadCrates">Crates that can no longer be solved; marked red as a warning.</param>
    public void Draw(SpriteBatch batch, LevelSession session, Theme theme, Rectangle area, IReadOnlySet<int>? deadCrates = null)
    {
        var board = session.Board;
        var tile = (int)MathF.Min(
            Tileset.TileSize * MaxScale,
            MathF.Min(area.Width / (float)board.Width, area.Height / (float)board.Height));
        var origin = new Vector2(
            area.X + (area.Width - tile * board.Width) / 2,
            area.Y + (area.Height - tile * board.Height) / 2);

        Rectangle At(Vector2 grid) =>
            new((int)MathF.Round(origin.X + grid.X * tile), (int)MathF.Round(origin.Y + grid.Y * tile), tile, tile);

        // Tiles first, so a sliding crate or player is never painted over by a neighbouring floor.
        for (var i = 0; i < board.Size; i++)
        {
            var cell = At(GridPosition(board, i));
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
            var position = crate == _crateTarget && _crate is not null ? _crate.Value : GridPosition(board, crate);
            batch.Draw(board.IsGoal(crate) ? theme.CrateOnGoal : theme.Crate, At(position), Color.White);
            if (deadCrates?.Contains(crate) == true)
            {
                // An overlay rather than a tint, so it reads as red whatever colour the crate is.
                var mark = At(position);
                mark.Inflate(-tile / 10, -tile / 10);
                batch.FillRectangle(mark, DeadOverlay);
            }
        }

        var player = session.Player == _playerTarget && _player is not null ? _player.Value : GridPosition(board, session.Player);
        var frames = _tileset.PlayerFrames(_facing);
        batch.Draw(frames[_frame % frames.Length], At(player), Color.White);
    }

    private static Vector2 GridPosition(Board board, int index) => new(board.XOf(index), board.YOf(index));
}
