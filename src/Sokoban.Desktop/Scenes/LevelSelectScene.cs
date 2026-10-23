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

/// <summary>A grid of level numbers with a live preview of the selected level.</summary>
internal sealed class LevelSelectScene : Scene
{
    private const int Columns = 10;
    private const int Margin = 48;
    private const int Gap = 8;

    private readonly GameContext _context;
    private readonly BoardRenderer _preview;
    private readonly ActionRepeater<GameAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(70),
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right);

    private int _selected;
    private LevelSession _previewSession;

    public LevelSelectScene(GameContext context, int selected)
    {
        _context = context;
        _preview = new BoardRenderer(context.Tileset);
        _selected = Math.Clamp(selected, 0, context.Levels.Count - 1);
        _previewSession = new LevelSession(context.Levels[_selected]);
    }

    private int Count => _context.Levels.Count;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Scenes.Push(new PlayScene(_context, _selected), Navigation.Fade());
            return;
        }

        var target = _repeater.Update(input, actions, elapsed) switch
        {
            GameAction.Left => _selected - 1,
            GameAction.Right => _selected + 1,
            GameAction.Up => _selected - Columns,
            GameAction.Down => _selected + Columns,
            _ => _selected,
        };
        Select(Math.Clamp(target, 0, Count - 1));
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        viewport.Inflate(-Margin, -Margin);
        var progress = _context.Progress.Progress;
        batch.DrawText(fonts.Heading, "Select level", new Vector2(viewport.X, viewport.Y), Palette.Text);
        var headerRow = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight);
        batch.DrawText(
            fonts.Body,
            string.Create(CultureInfo.InvariantCulture, $"{progress.SolvedCount(_context.Levels)} / {Count} solved"),
            headerRow,
            Palette.Muted,
            Align.Right,
            shadow: false);

        var content = viewport;
        content.Y += (int)fonts.Heading.LineHeight + 24;
        content.Height -= (int)fonts.Heading.LineHeight + 24 + 40;

        // Grid on the left, preview on the right.
        var rows = (Count + Columns - 1) / Columns;
        var cell = Math.Min((content.Width / 2 - Gap * Columns) / Columns, (content.Height - Gap * rows) / rows);
        for (var i = 0; i < Count; i++)
        {
            var bounds = new Rectangle(
                content.X + i % Columns * (cell + Gap),
                content.Y + i / Columns * (cell + Gap),
                cell,
                cell);
            var selected = i == _selected;
            var fill = selected ? Palette.Accent : progress.IsSolved(_context.Levels[i]) ? Palette.Solved : Palette.Panel;
            batch.FillRectangle(bounds, fill);
            batch.DrawText(fonts.Small, (i + 1).ToString(CultureInfo.InvariantCulture), bounds, selected ? Palette.Ink : Palette.Text, shadow: false);
        }

        var gridWidth = Columns * (cell + Gap);
        var preview = new Rectangle(content.X + gridWidth + Margin, content.Y, content.Width - gridWidth - Margin, content.Height);
        var level = _context.Levels[_selected];
        batch.DrawText(fonts.Body, level.Title ?? $"Level {_selected + 1}", new Vector2(preview.Center.X, preview.Y), Palette.Text, Align.Center);
        var record = progress.Get(level);
        batch.DrawText(
            fonts.Small,
            record is null
                ? string.Create(CultureInfo.InvariantCulture, $"{level.Board.Goals.Count} crates, not solved yet")
                : Format.Best(record),
            new Vector2(preview.Center.X, preview.Y + fonts.Body.LineHeight + 4),
            record is null ? Palette.Muted : Palette.Accent,
            Align.Center,
            shadow: false);

        var board = preview;
        var header = (int)(fonts.Body.LineHeight + fonts.Small.LineHeight) + 24;
        board.Y += header;
        board.Height -= header;
        _preview.Draw(batch, _previewSession, _context.Tileset.ThemeFor(_selected + 1), board);

        batch.DrawText(
            fonts.Small,
            "Arrows to choose, Enter to play, Esc to go back",
            new Vector2(viewport.Center.X, viewport.Bottom - fonts.Small.LineHeight),
            Palette.Muted,
            Align.Center,
            shadow: false);
    }

    private void Select(int index)
    {
        if (index == _selected)
        {
            return;
        }

        _selected = index;
        _previewSession = new LevelSession(_context.Levels[index]);
        _context.Sounds.Play(Sound.Navigate);
    }
}
