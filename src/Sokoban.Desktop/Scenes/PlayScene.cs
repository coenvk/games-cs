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

namespace Sokoban.Desktop.Scenes;

/// <summary>Plays one level: movement, undo/redo and restart. Switching level replaces this scene.</summary>
internal sealed class PlayScene : Scene
{
    private const int Margin = 24;
    private const int HudHeight = 76;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(80);

    // Lets the final push register before the completion screen covers the board.
    private static readonly TimeSpan CompleteDelay = TimeSpan.FromMilliseconds(450);

    private readonly GameContext _context;
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

    private TimeSpan _solvedFor;
    private bool _completeShown;
    private bool _recorded;

    public PlayScene(GameContext context, int levelIndex)
    {
        _context = context;
        LevelIndex = Math.Clamp(levelIndex, 0, context.Levels.Count - 1);
        Session = new LevelSession(context.Levels[LevelIndex]);
        _theme = context.Tileset.ThemeFor(LevelIndex + 1);
        _renderer = new BoardRenderer(context.Tileset);
    }

    public int LevelIndex { get; }

    public LevelSession Session { get; }

    public bool IsLastLevel => LevelIndex == _context.Levels.Count - 1;

    public string Title => Session.Level.Title ?? $"Level {LevelIndex + 1}";

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
        else if (actions.WasPressed(input, GameAction.NextLevel))
        {
            GoToLevel(LevelIndex + 1);
            return;
        }
        else if (actions.WasPressed(input, GameAction.PreviousLevel))
        {
            GoToLevel(LevelIndex - 1);
            return;
        }
        else if (Session.IsSolved && actions.WasPressed(input, GameAction.Confirm))
        {
            ShowComplete();
            return;
        }

        if (!Session.IsSolved)
        {
            Time += elapsed;
        }

        HandleMovement(_repeater.Update(input, actions, elapsed));
        _renderer.Update(elapsed);
        UpdateCompletion(elapsed);
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
        batch.DrawText(
            fonts.Small,
            "Arrows move   Z undo   Y redo   R restart   Esc pause",
            footer,
            Palette.Muted,
            shadow: false);

        var board = new Rectangle(viewport.X, hud.Bottom, viewport.Width, footer.Top - hud.Bottom);
        board.Inflate(-Margin, -Margin);
        _renderer.Draw(batch, Session, _theme, board);
    }

    public void Restart()
    {
        Session.Restart();
        _renderer.Reset();
        Time = TimeSpan.Zero;
        ResetCompletion();
    }

    public void GoToLevel(int index)
    {
        if (index >= 0 && index < _context.Levels.Count)
        {
            Scenes.Replace(
                new PlayScene(_context, index),
                Session.IsSolved && index == LevelIndex + 1 ? Navigation.Wipe() : Navigation.Fade());
        }
    }

    protected override void OnCovered() => _repeater.Reset();

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
            AnimateForward(direction, from, result == MoveResult.Pushed);
        }
    }

    private void Redo()
    {
        var from = Session.Player;
        if (Session.Redo() && Session.LastMove is { } move)
        {
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
