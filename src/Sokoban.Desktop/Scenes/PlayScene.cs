using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Desktop.UI;
using Sokoban.Model;
using Sokoban.Solver;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Plays one level, from the pack or generated: movement, undo/redo, restart and hints.
/// Switching level replaces this scene.
/// </summary>
internal sealed class PlayScene : Scene
{
    private const int Margin = 24;
    private const int HudHeight = 76;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HintStepInterval = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);

    // Lets the final push register before the completion screen covers the board.
    private static readonly TimeSpan CompleteDelay = TimeSpan.FromMilliseconds(450);

    private readonly GameContext _context;
    private readonly Theme _theme;
    private readonly BoardRenderer _renderer;
    private readonly Solver.Solver _solver;
    private readonly HintRunner _hints;
    private readonly Queue<Direction> _hintMoves = new();
    private readonly ActionRepeater<GameAction> _repeater = new(
        RepeatDelay,
        RepeatInterval,
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right,
        GameAction.Undo,
        GameAction.Redo);

    private TimeSpan _solvedFor;
    private bool _completeShown;
    private bool _recorded;
    private int _positionVersion;
    private TimeSpan _untilHintStep;
    private string? _message;
    private TimeSpan _messageFor;

    /// <summary>Plays level <paramref name="packIndex"/> of the level pack.</summary>
    public PlayScene(GameContext context, int packIndex)
        : this(context, context.Levels[Math.Clamp(packIndex, 0, context.Levels.Count - 1)], Math.Clamp(packIndex, 0, context.Levels.Count - 1))
    {
    }

    /// <summary>Plays a level that is not part of the pack, such as a generated one.</summary>
    public PlayScene(GameContext context, Level level)
        : this(context, level, packIndex: null)
    {
    }

    private PlayScene(GameContext context, Level level, int? packIndex)
    {
        _context = context;
        PackIndex = packIndex;
        Session = new LevelSession(level);
        _theme = context.Tileset.ThemeFor(packIndex + 1 ?? int.Parse(level.Key[..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        _renderer = new BoardRenderer(context.Tileset);
        _solver = new Solver.Solver(level.Board);
        _hints = new HintRunner(_solver);
    }

    /// <summary>Position in the level pack, or null for a generated level.</summary>
    public int? PackIndex { get; }

    public LevelSession Session { get; }

    /// <summary>The level to highlight when going to level select from here.</summary>
    public int SelectIndex => PackIndex ?? _context.ContinueIndex;

    /// <summary>Whether there is a next level: always for generated levels, which can generate another.</summary>
    public bool HasNextLevel => PackIndex is not { } index || index < _context.Levels.Count - 1;

    public string Title => PackIndex is { } index ? Session.Level.Title ?? $"Level {index + 1}" : "Random level";

    /// <summary>Time spent on the current attempt. Only runs while this scene is on top and unsolved.</summary>
    public TimeSpan Time { get; private set; }

    /// <summary>What the most recent solve improved on.</summary>
    public Improvement LastImprovement { get; private set; }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new PauseScene(_context, this));
            return;
        }

        if (actions.WasPressed(input, GameAction.Restart))
        {
            Restart();
        }
        else if (actions.WasPressed(input, GameAction.NextLevel) && PackIndex is { } next)
        {
            GoToPackLevel(next + 1);
            return;
        }
        else if (actions.WasPressed(input, GameAction.PreviousLevel) && PackIndex is { } previous)
        {
            GoToPackLevel(previous - 1);
            return;
        }
        else if (Session.IsSolved && actions.WasPressed(input, GameAction.Confirm))
        {
            ShowComplete();
            return;
        }
        else if (actions.WasPressed(input, GameAction.Hint))
        {
            RequestHint();
        }

        if (!Session.IsSolved)
        {
            Time += elapsed;
        }

        var action = _repeater.Update(input, actions, elapsed);
        if (action is not null)
        {
            // Taking over the controls stops a hint that is playing.
            _hintMoves.Clear();
            HandleMovement(action);
        }

        UpdateHints(elapsed);
        _renderer.Update(elapsed);
        UpdateCompletion(elapsed);

        _messageFor -= elapsed;
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, HudHeight);
        batch.FillRectangle(hud, Palette.Panel);

        var hudText = hud;
        hudText.Inflate(-Margin, 0);
        batch.DrawText(fonts.Heading, Title, hudText, Palette.Text, Align.Left);
        if (_context.Progress.Progress.Get(Session.Level) is { } record)
        {
            var best = hudText;
            var titleWidth = (int)fonts.Heading.MeasureString(Title).X + Margin;
            best.X += titleWidth;
            best.Width -= titleWidth;
            batch.DrawText(fonts.Small, Format.Best(record), best, Palette.Muted, Align.Left, shadow: false);
        }

        var stats = string.Create(
            CultureInfo.InvariantCulture,
            $"Moves {Session.MoveCount}    Pushes {Session.PushCount}    {Format.Duration(Time)}");
        batch.DrawText(fonts.Body, stats, hudText, Session.IsSolved ? Palette.Accent : Palette.Text, Align.Right);

        var footer = new Rectangle(viewport.X, viewport.Bottom - 44, viewport.Width, 44);
        var (footerText, footerColor) = _hints.IsThinking
            ? ("Thinking...", Palette.Accent)
            : _message is not null && _messageFor > TimeSpan.Zero
                ? (_message, Palette.Accent)
                : ("Arrows move   Z undo   Y redo   R restart   H hint   Esc pause", Palette.Muted);
        batch.DrawText(fonts.Small, footerText, footer, footerColor, shadow: false);

        var board = new Rectangle(viewport.X, hud.Bottom, viewport.Width, footer.Top - hud.Bottom);
        board.Inflate(-Margin, -Margin);
        _renderer.Draw(batch, Session, _theme, board, _solver.Deadlocks.DeadCrates(Session));
    }

    public void Restart()
    {
        Session.Restart();
        _renderer.Reset();
        Time = TimeSpan.Zero;
        PositionChanged();
        ResetCompletion();
    }

    /// <summary>Goes to the next pack level, or generates a new level after a generated one.</summary>
    public void NextLevel()
    {
        if (PackIndex is { } index)
        {
            GoToPackLevel(index + 1);
        }
        else
        {
            Scenes.Replace(new GenerateScene(_context), Navigation.Fade());
        }
    }

    protected override void OnCovered() => _repeater.Reset();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hints.Dispose();
        }

        base.Dispose(disposing);
    }

    private void GoToPackLevel(int index)
    {
        if (index >= 0 && index < _context.Levels.Count)
        {
            Scenes.Replace(
                new PlayScene(_context, index),
                Session.IsSolved && index == PackIndex + 1 ? Navigation.Wipe() : Navigation.Fade());
        }
    }

    private void RequestHint()
    {
        if (Session.IsSolved || _hints.IsThinking)
        {
            return;
        }

        _hintMoves.Clear();

        if (_solver.Deadlocks.DeadCrates(Session).Count > 0)
        {
            ShowMessage("A crate is stuck (shown in red). Undo with Z or restart with R.");
            return;
        }

        _hints.Start(Session, _positionVersion);
    }

    private void UpdateHints(TimeSpan elapsed)
    {
        if (_hints.TakeResult(_positionVersion) is { } result)
        {
            switch (result.Status)
            {
                case SolveStatus.Solved:
                    // Play up to and including the next push; that is a hint, not the whole answer.
                    foreach (var move in result.Moves)
                    {
                        _hintMoves.Enqueue(move.Direction);
                        if (move.IsPush)
                        {
                            break;
                        }
                    }

                    _untilHintStep = TimeSpan.Zero;
                    break;
                case SolveStatus.Unsolvable:
                    ShowMessage("This position can't be solved any more. Undo with Z or restart with R.");
                    break;
                case SolveStatus.LimitReached:
                    ShowMessage("No hint found: this position is too tricky to work out quickly.");
                    break;
            }
        }

        if (_hintMoves.Count == 0)
        {
            return;
        }

        _untilHintStep -= elapsed;
        if (_untilHintStep <= TimeSpan.Zero)
        {
            _untilHintStep += HintStepInterval;
            Move(_hintMoves.Dequeue());
        }
    }

    private void ShowMessage(string message)
    {
        _message = message;
        _messageFor = MessageDuration;
    }

    private void PositionChanged()
    {
        _positionVersion++;
        _hints.Cancel();
    }

    private void HandleMovement(GameAction? action)
    {
        switch (action)
        {
            case GameAction.Up when !Session.IsSolved:
                Move(Direction.Up);
                break;
            case GameAction.Down when !Session.IsSolved:
                Move(Direction.Down);
                break;
            case GameAction.Left when !Session.IsSolved:
                Move(Direction.Left);
                break;
            case GameAction.Right when !Session.IsSolved:
                Move(Direction.Right);
                break;
            case GameAction.Undo:
                Undo();
                break;
            case GameAction.Redo:
                Redo();
                break;
        }
    }

    private void Move(Direction direction)
    {
        var from = Session.Player;
        var result = Session.TryMove(direction);
        if (result != MoveResult.Blocked)
        {
            PositionChanged();
            AnimateForward(direction, from, result == MoveResult.Pushed);
        }
    }

    private void Redo()
    {
        var from = Session.Player;
        if (Session.Redo() && Session.LastMove is { } move)
        {
            PositionChanged();
            AnimateForward(move.Direction, from, move.IsPush);
        }
    }

    private void Undo()
    {
        if (Session.LastMove is not { } move)
        {
            return;
        }

        var from = Session.Player;
        Session.Undo();
        PositionChanged();

        // The player steps back while still facing the original direction; a pushed crate follows it back.
        var crateFrom = move.IsPush ? Neighbor(from, move.Direction) : (int?)null;
        _renderer.Animate(Session.Board, new Step(move.Direction, from, Session.Player, crateFrom, move.IsPush ? from : null));
    }

    private void AnimateForward(Direction direction, int from, bool pushed)
    {
        var to = Session.Player;
        _renderer.Animate(
            Session.Board,
            pushed ? new Step(direction, from, to, to, Neighbor(to, direction)) : new Step(direction, from, to));
    }

    private int Neighbor(int index, Direction direction) =>
        Session.Board.TryGetNeighbor(index, direction, out var neighbor) ? neighbor : index;

    private void UpdateCompletion(TimeSpan elapsed)
    {
        if (!Session.IsSolved)
        {
            ResetCompletion();
            return;
        }

        if (!_recorded)
        {
            _recorded = true;
            _hintMoves.Clear();
            LastImprovement = _context.Progress.Record(Session, Time);
            _context.Sounds.Play(Sound.Completed);
        }

        _solvedFor += elapsed;
        if (!_completeShown && _solvedFor >= CompleteDelay)
        {
            ShowComplete();
        }
    }

    private void ShowComplete()
    {
        _completeShown = true;
        Scenes.Push(new CompleteScene(_context, this));
    }

    private void ResetCompletion()
    {
        _solvedFor = TimeSpan.Zero;
        _completeShown = false;
        _recorded = false;
    }
}
