using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Model;

namespace Sokoban.Desktop;

/// <summary>Plays through a level pack: movement, undo/redo, restart and level switching.</summary>
internal sealed class PlayScene
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(80);

    private readonly IReadOnlyList<Level> _levels;
    private readonly Tileset _tileset;
    private readonly BoardRenderer _renderer;
    private readonly ActionRepeater _repeater = new(
        RepeatDelay,
        RepeatInterval,
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right,
        GameAction.Undo,
        GameAction.Redo);

    private int _levelIndex;
    private LevelSession _session;
    private Theme _theme;

    public PlayScene(IReadOnlyList<Level> levels, Tileset tileset, int startIndex = 0)
    {
        _levels = levels;
        _tileset = tileset;
        _renderer = new BoardRenderer(tileset);
        (_session, _theme) = Load(startIndex);
    }

    public string Status
    {
        get
        {
            var title = _session.Level.Title ?? $"Level {_levelIndex + 1}";
            var progress = $"{title} ({_levelIndex + 1}/{_levels.Count})";
            return _session.IsSolved
                ? $"{progress} · Solved in {_session.MoveCount} moves, {_session.PushCount} pushes · Enter: next level"
                : $"{progress} · Moves {_session.MoveCount} · Pushes {_session.PushCount}";
        }
    }

    public void Update(InputState input, ActionMap map, TimeSpan elapsed)
    {
        if (map.WasPressed(input, GameAction.Restart))
        {
            _session.Restart();
            _renderer.ResetPlayer();
        }
        else if (map.WasPressed(input, GameAction.NextLevel)
                 || (_session.IsSolved && map.WasPressed(input, GameAction.Confirm)))
        {
            (_session, _theme) = Load(_levelIndex + 1);
        }
        else if (map.WasPressed(input, GameAction.PreviousLevel))
        {
            (_session, _theme) = Load(_levelIndex - 1);
        }

        switch (_repeater.Update(input, map, elapsed))
        {
            case GameAction.Up when !_session.IsSolved:
                Move(Direction.Up);
                break;
            case GameAction.Down when !_session.IsSolved:
                Move(Direction.Down);
                break;
            case GameAction.Left when !_session.IsSolved:
                Move(Direction.Left);
                break;
            case GameAction.Right when !_session.IsSolved:
                Move(Direction.Right);
                break;
            case GameAction.Undo:
                if (_session.LastMove is { } undone && _session.Undo())
                {
                    _renderer.OnPlayerMoved(undone.Direction);
                }

                break;
            case GameAction.Redo:
                if (_session.Redo() && _session.LastMove is { } redone)
                {
                    _renderer.OnPlayerMoved(redone.Direction);
                }

                break;
        }
    }

    public void Draw(SpriteBatch batch, Rectangle area) => _renderer.Draw(batch, _session, _theme, area);

    private void Move(Direction direction)
    {
        if (_session.TryMove(direction) != MoveResult.Blocked)
        {
            _renderer.OnPlayerMoved(direction);
        }
    }

    private (LevelSession, Theme) Load(int index)
    {
        _levelIndex = Math.Clamp(index, 0, _levels.Count - 1);
        _renderer.ResetPlayer();
        return (new LevelSession(_levels[_levelIndex]), _tileset.ThemeFor(_levelIndex + 1));
    }
}
