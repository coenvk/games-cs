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
using Jentl.Core.UI;

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

    // Tap-to-walk plays at about the pace of a held arrow key.
    private static readonly TimeSpan WalkStepInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);

    // Lets the final push register before the completion screen covers the board.
    private static readonly TimeSpan CompleteDelay = TimeSpan.FromMilliseconds(450);

    private readonly GameContext _context;
    private readonly Theme _theme;
    private readonly BoardRenderer _renderer;
    private readonly Solver.Solver _solver;
    private readonly HintRunner _hints;
    private readonly ButtonBar _buttons;

    // Moves played automatically, one per step interval: a hint, or a walk to a tapped cell.
    private readonly Queue<Direction> _queuedMoves = new();
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
    private TimeSpan _untilQueuedStep;
    private TimeSpan _queuedStepInterval;
    private string? _message;
    private TimeSpan _messageFor;

    /// <summary>Plays a level from one of the packs.</summary>
    public PlayScene(GameContext context, LevelRef where)
        : this(context, context.LevelAt(where), where)
    {
    }

    /// <summary>Plays a level that is not part of any pack, such as a generated one.</summary>
    public PlayScene(GameContext context, Level level)
        : this(context, level, where: null)
    {
    }

    private PlayScene(GameContext context, Level level, LevelRef? where)
    {
        _context = context;
        Where = where;
        Session = new LevelSession(level);
        _theme = context.Tileset.ThemeFor(where is { } w
            ? GameContext.ThemeSeed(w)
            : int.Parse(level.Key[..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        _renderer = new BoardRenderer(context.Tileset);
        _solver = new Solver.Solver(level.Board);
        _hints = new HintRunner(_solver, context.Platform.HintTime);
        _buttons = new ButtonBar(
            new Button("Undo", () => TakeOver(Undo)),
            new Button("Redo", () => TakeOver(Redo)),
            new Button("Restart", Restart),
            new Button("Hint", RequestHint),
            new Button("Menu", () => Scenes.Push(new PauseScene(_context, this))));
    }

    /// <summary>Where the level sits in the packs, or null for a generated level.</summary>
    public LevelRef? Where { get; }

    public LevelSession Session { get; }

    /// <summary>The level to highlight when going to level select from here.</summary>
    public LevelRef SelectTarget => Where ?? _context.Continue;

    /// <summary>Whether there is a next level: always for generated levels, which can generate another.</summary>
    public bool HasNextLevel => Where is not { } w || w.Index < _context.Packs[w.Pack].Levels.Count - 1;

    public string Title => Where is { } w ? _context.TitleOf(w) : "Random level";

    /// <summary>Time spent on the current attempt. Only runs while this scene is on top and unsolved.</summary>
    public TimeSpan Time { get; private set; }

    /// <summary>What the most recent solve improved on.</summary>
    public Improvement LastImprovement { get; private set; }

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Puzzle);

    public override IEnumerable<string> DebugInfo =>
    [
        Where is { } where ? string.Create(CultureInfo.InvariantCulture, $"Pack {where.Pack}, level {where.Index + 1}") : "Generated level",
        string.Create(CultureInfo.InvariantCulture, $"Moves {Session.MoveCount}  pushes {Session.PushCount}  {(Session.IsSolved ? "solved" : "unsolved")}"),
        string.Create(CultureInfo.InvariantCulture, $"Player at {Session.Player}  queued steps {_queuedMoves.Count}"),
    ];

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
        else if (actions.WasPressed(input, GameAction.NextLevel) && Where is { } next)
        {
            GoToPackLevel(next.Index + 1);
            return;
        }
        else if (actions.WasPressed(input, GameAction.PreviousLevel) && Where is { } previous)
        {
            GoToPackLevel(previous.Index - 1);
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
            TakeOver(() => HandleMovement(action));
        }

        HandlePointer(input);
        UpdateHints();
        PlayQueuedMoves(elapsed);
        _renderer.Update(elapsed);
        UpdateCompletion(elapsed);

        _messageFor -= elapsed;
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(Margin);
        var record = _context.Progress.Progress.Get(Session.Level);
        var stats = string.Create(
            CultureInfo.InvariantCulture,
            $"Moves {Session.MoveCount}    Pushes {Session.PushCount}    {Format.Duration(Time)}");
        var statsColor = Session.IsSolved ? Palette.Accent : Palette.Text;

        // Landscape: title and best on the left, stats on the right. Portrait: stacked, stats below.
        Rectangle hud;
        if (fonts.IsPortrait)
        {
            var row = (int)fonts.Heading.LineHeight + fonts.Px(12);
            hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, row + (int)fonts.Body.LineHeight + fonts.Px(16));
            batch.FillRectangle(hud, Palette.Panel);
            var text = hud;
            text.Inflate(-margin, 0);
            batch.DrawText(fonts.Heading, Title, new Rectangle(text.X, text.Y, text.Width, row), Palette.Text, Align.Left);
            var second = new Rectangle(text.X, text.Y + row, text.Width, hud.Height - row - fonts.Px(10));
            batch.DrawText(fonts.Body, stats, second, statsColor, Align.Left);
        }
        else
        {
            hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
            batch.FillRectangle(hud, Palette.Panel);
            var text = hud;
            text.Inflate(-margin, 0);
            batch.DrawText(fonts.Heading, Title, text, Palette.Text, Align.Left);
            if (record is not null)
            {
                var best = text;
                var titleWidth = (int)fonts.Heading.MeasureString(Title).X + margin;
                best.X += titleWidth;
                best.Width -= titleWidth;
                batch.DrawText(fonts.Small, Format.Best(record), best, Palette.Muted, Align.Left, shadow: false);
            }

            batch.DrawText(fonts.Body, stats, text, statsColor, Align.Right);
        }

        // Footer: a status line (thinking, messages, best result) above the buttons.
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var statusHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var status = new Rectangle(viewport.X, buttons.Top - statusHeight, viewport.Width, statusHeight);
        var (statusText, statusColor) = _hints.IsThinking
            ? ("Thinking...", Palette.Accent)
            : _message is not null && _messageFor > TimeSpan.Zero
                ? (_message, Palette.Accent)
                : fonts.IsPortrait && record is not null
                    ? (Format.Best(record), Palette.Muted)
                    : ("Tap a cell to walk there, swipe to step", Palette.Muted);
        batch.DrawText(fonts.Small, statusText, status, statusColor, shadow: false);
        _buttons.Draw(batch, buttonFont, buttons);

        var board = new Rectangle(viewport.X, hud.Bottom, viewport.Width, status.Top - hud.Bottom);
        board.Inflate(-margin, -margin);
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
        if (Where is { } where)
        {
            GoToPackLevel(where.Index + 1);
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

    /// <summary>Goes to another level in the same pack, if it exists.</summary>
    private void GoToPackLevel(int index)
    {
        if (Where is { } where && index >= 0 && index < _context.Packs[where.Pack].Levels.Count)
        {
            Scenes.Replace(
                new PlayScene(_context, where with { Index = index }),
                Session.IsSolved && index == where.Index + 1 ? Navigation.Wipe() : Navigation.Fade());
        }
    }

    private void RequestHint()
    {
        if (Session.IsSolved || _hints.IsThinking)
        {
            return;
        }

        _queuedMoves.Clear();

        if (_solver.Deadlocks.DeadCrates(Session).Count > 0)
        {
            ShowMessage("A crate is stuck (shown in red). Undo or restart.");
            return;
        }

        _hints.Start(Session, _positionVersion);
    }

    private void UpdateHints()
    {
        if (_hints.TakeResult(_positionVersion) is not { } result)
        {
            return;
        }

        switch (result.Status)
        {
            case SolveStatus.Solved:
                // Play up to and including the next push; that is a hint, not the whole answer.
                Queue(result.Moves.TakeWhile((_, i) => i == 0 || !result.Moves[i - 1].IsPush).Select(m => m.Direction), HintStepInterval);
                break;
            case SolveStatus.Unsolvable:
                ShowMessage("This position can't be solved any more. Undo or restart.");
                break;
            case SolveStatus.LimitReached:
                ShowMessage("No hint found: this position is too tricky to work out quickly.");
                break;
        }
    }

    /// <summary>Buttons first; then a tap on the board walks or pushes, and a swipe steps.</summary>
    private void HandlePointer(InputState input)
    {
        // A drag of about half a cell is one step, so a finger can walk the player along.
        input.Gestures.SwipeDistance = Math.Max(24, _renderer.TileSize * 0.6f);

        if (_buttons.Update(input))
        {
            return;
        }

        if (input.Tap is { } tap)
        {
            if (Session.IsSolved)
            {
                ShowComplete();
            }
            else if (_renderer.CellAt(tap) is { } cell)
            {
                TakeOver(() => TapCell(cell));
            }
        }
        else if (input.Swipe is { } swipe && !Session.IsSolved)
        {
            TakeOver(() => Move(swipe switch
            {
                SwipeDirection.Up => Direction.Up,
                SwipeDirection.Down => Direction.Down,
                SwipeDirection.Left => Direction.Left,
                _ => Direction.Right,
            }));
        }
    }

    /// <summary>A crate next to the player gets pushed; any other reachable cell is walked to.</summary>
    private void TapCell(int cell)
    {
        foreach (var direction in (ReadOnlySpan<Direction>)[Direction.Up, Direction.Down, Direction.Left, Direction.Right])
        {
            if (Session.Board.TryGetNeighbor(Session.Player, direction, out var next) && next == cell && Session.HasCrate(cell))
            {
                Move(direction);
                return;
            }
        }

        if (Session.FindPath(cell) is { Count: > 0 } path)
        {
            Queue(path, WalkStepInterval);
        }
    }

    /// <summary>Direct input from the player stops anything playing on its own, then runs.</summary>
    private void TakeOver(Action action)
    {
        _queuedMoves.Clear();
        action();
    }

    private void Queue(IEnumerable<Direction> moves, TimeSpan interval)
    {
        _queuedMoves.Clear();
        foreach (var move in moves)
        {
            _queuedMoves.Enqueue(move);
        }

        _queuedStepInterval = interval;
        _untilQueuedStep = TimeSpan.Zero;
    }

    private void PlayQueuedMoves(TimeSpan elapsed)
    {
        if (_queuedMoves.Count == 0)
        {
            return;
        }

        _untilQueuedStep -= elapsed;
        if (_untilQueuedStep <= TimeSpan.Zero)
        {
            _untilQueuedStep += _queuedStepInterval;
            Move(_queuedMoves.Dequeue());
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
        var crateFrom = move.IsPush ? Session.CrateTarget(move, from) : (int?)null;
        _renderer.Animate(Session.Board, new Step(move.Direction, from, Session.Player, crateFrom, move.IsPush ? from : null));
    }

    private void AnimateForward(Direction direction, int from, bool pushed)
    {
        var to = Session.Player;
        _renderer.Animate(
            Session.Board,
            pushed && Session.LastMove is { } move
                ? new Step(direction, from, to, to, Session.CrateTarget(move, to))
                : new Step(direction, from, to));
    }

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
            _queuedMoves.Clear();
            LastImprovement = _context.Progress.Record(Session, Time);
            _context.Sounds.Play(Sound.Completed);
            Celebration.Confetti(Scenes);
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
