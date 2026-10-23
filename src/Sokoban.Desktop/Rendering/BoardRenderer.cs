using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;

namespace Sokoban.Desktop.Rendering;

/// <summary>Draws a level session centred in a target rectangle, scaled to fit.</summary>
internal sealed class BoardRenderer
{
    private const float MaxScale = 2f;

    private readonly Tileset _tileset;
    private Direction _facing = Direction.Down;
    private int _frame;

    public BoardRenderer(Tileset tileset) => _tileset = tileset;

    /// <summary>Advances the walk cycle; call after each move or undo.</summary>
    public void OnPlayerMoved(Direction direction)
    {
        _frame = direction == _facing ? _frame + 1 : 0;
        _facing = direction;
    }

    public void ResetPlayer()
    {
        _facing = Direction.Down;
        _frame = 0;
    }

    public void Draw(SpriteBatch batch, LevelSession session, Theme theme, Rectangle area)
    {
        var board = session.Board;
        var tile = (int)MathF.Min(
            Tileset.TileSize * MaxScale,
            MathF.Min(area.Width / (float)board.Width, area.Height / (float)board.Height));
        var origin = new Point(
            area.X + (area.Width - tile * board.Width) / 2,
            area.Y + (area.Height - tile * board.Height) / 2);

        Rectangle Cell(int index) =>
            new(origin.X + board.XOf(index) * tile, origin.Y + board.YOf(index) * tile, tile, tile);

        for (var i = 0; i < board.Size; i++)
        {
            if (board.IsWall(i))
            {
                batch.Draw(theme.Wall, Cell(i), Color.White);
                continue;
            }

            if (!board.IsInterior(i))
            {
                continue;
            }

            batch.Draw(theme.Floor, Cell(i), Color.White);
            if (session.HasCrate(i))
            {
                batch.Draw(board.IsGoal(i) ? theme.CrateOnGoal : theme.Crate, Cell(i), Color.White);
            }
            else if (board.IsGoal(i))
            {
                batch.Draw(theme.Goal, Cell(i), Color.White);
            }
        }

        var frames = _tileset.PlayerFrames(_facing);
        batch.Draw(frames[_frame % frames.Length], Cell(session.Player), Color.White);
    }
}
