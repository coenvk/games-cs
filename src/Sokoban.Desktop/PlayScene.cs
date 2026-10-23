using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Model;

namespace Sokoban.Desktop;

/// <summary>Plays one level: movement, undo/redo and restart. Switching level replaces this scene.</summary>
internal sealed class PlayScene : Scene
{
    private const int Margin = 24;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan LevelTransition = TimeSpan.FromMilliseconds(500);

    private readonly GameContext _context;
    private readonly int _levelIndex;
    private readonly LevelSession _session;
    private readonly Theme _theme;
    private readonly BoardRenderer _renderer;
    private readonly ActionRepeater<GameAction> _repeater = new(
        RepeatDelay,
        RepeatInterval,
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right,
        GameAction.Undo,
        GameAction.Redo);

    public PlayScene(GameContext context, int levelIndex)
    {
        _context = context;
        _levelIndex = Math.Clamp(levelIndex, 0, context.Levels.Count - 1);
        _session = new LevelSession(context.Levels[_levelIndex]);
        _theme = context.Tileset.ThemeFor(_levelIndex + 1);
        _renderer = new BoardRenderer(context.Tileset);
    }

    public string Status
    {
        get
        {
            var title = _session.Level.Title ?? $"Level {_levelIndex + 1}";
            var progress = $"{title} ({_levelIndex + 1}/{_context.Levels.Count})";
            return _session.IsSolved
                ? $"{progress} · Solved in {_session.MoveCount} moves, {_session.PushCount} pushes · Enter: next level"
                : $"{progress} · Moves {_session.MoveCount} · Pushes {_session.PushCount}";
        }
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(new FadeTransition(LevelTransition));
            return;
        }

        if (actions.WasPressed(input, GameAction.Restart))
        {
            _session.Restart();
            _renderer.ResetPlayer();
        }
        else if (actions.WasPressed(input, GameAction.NextLevel)
                 || (_session.IsSolved && actions.WasPressed(input, GameAction.Confirm)))
        {
            GoToLevel(_levelIndex + 1, _session.IsSolved
                ? new WipeTransition(LevelTransition)
                : new FadeTransition(LevelTransition));
            return;
        }
        else if (actions.WasPressed(input, GameAction.PreviousLevel))
        {
            GoToLevel(_levelIndex - 1, new FadeTransition(LevelTransition));
            return;
        }

        switch (_repeater.Update(input, actions, elapsed))
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

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        viewport.Inflate(-Margin, -Margin);
        _renderer.Draw(batch, _session, _theme, viewport);
    }

    protected override void OnCovered() => _repeater.Reset();

    private void GoToLevel(int index, Transition transition)
    {
        if (index >= 0 && index < _context.Levels.Count)
        {
            Scenes.Replace(new PlayScene(_context, index), transition);
        }
    }

    private void Move(Direction direction)
    {
        if (_session.TryMove(direction) != MoveResult.Blocked)
        {
            _renderer.OnPlayerMoved(direction);
        }
    }
}
