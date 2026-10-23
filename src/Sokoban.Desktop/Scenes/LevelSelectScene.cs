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

/// <summary>A grid of level numbers per pack, with a live preview of the selected level.</summary>
internal sealed class LevelSelectScene : Scene
{
    private const int Columns = 10;
    private const int Margin = 48;
    private const int Gap = 8;
    private const int TabGap = 32;

    private readonly GameContext _context;
    private readonly BoardRenderer _preview;
    private readonly ActionRepeater<GameAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(70),
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right);

    private LevelRef _selected;
    private LevelSession _previewSession;

    public LevelSelectScene(GameContext context, LevelRef selected)
    {
        _context = context;
        _preview = new BoardRenderer(context.Tileset);
        _selected = selected;
        _previewSession = new LevelSession(context.LevelAt(selected));
    }

    private LevelPack Pack => _context.Packs[_selected.Pack];

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

        if (actions.WasPressed(input, GameAction.SwitchPack))
        {
            var pack = (_selected.Pack + 1) % _context.Packs.Count;
            Select(new LevelRef(pack, _context.Progress.Progress.FirstUnsolved(_context.Packs[pack].Levels) ?? 0));
            return;
        }

        var index = _repeater.Update(input, actions, elapsed) switch
        {
            GameAction.Left => _selected.Index - 1,
            GameAction.Right => _selected.Index + 1,
            GameAction.Up => _selected.Index - Columns,
            GameAction.Down => _selected.Index + Columns,
            _ => _selected.Index,
        };
        Select(_selected with { Index = Math.Clamp(index, 0, Pack.Levels.Count - 1) });
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        viewport.Inflate(-Margin, -Margin);
        var progress = _context.Progress.Progress;
        var levels = Pack.Levels;

        // Title, then one tab per pack; the current one highlighted.
        batch.DrawText(fonts.Heading, "Select level", new Vector2(viewport.X, viewport.Y), Palette.Text);
        var headerRow = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight);
        var tabX = viewport.X + (int)fonts.Heading.MeasureString("Select level").X + 2 * TabGap;
        for (var p = 0; p < _context.Packs.Count; p++)
        {
            var name = _context.Packs[p].Name;
            var tab = new Rectangle(tabX, headerRow.Y, (int)fonts.Body.MeasureString(name).X, headerRow.Height);
            batch.DrawText(fonts.Body, name, tab, p == _selected.Pack ? Palette.Accent : Palette.Muted, Align.Left, shadow: false);
            if (p == _selected.Pack)
            {
                batch.FillRectangle(new Rectangle(tab.X, tab.Bottom - 4, tab.Width, 4), Palette.Accent);
            }

            tabX = tab.Right + TabGap;
        }

        batch.DrawText(
            fonts.Body,
            string.Create(CultureInfo.InvariantCulture, $"{progress.SolvedCount(levels)} / {levels.Count} solved"),
            headerRow,
            Palette.Muted,
            Align.Right,
            shadow: false);

        var content = viewport;
        content.Y += (int)fonts.Heading.LineHeight + 24;
        content.Height -= (int)fonts.Heading.LineHeight + 24 + 40;

        // Grid on the left, sized for the biggest pack so switching packs keeps the layout steady.
        var rows = _context.Packs.Max(p => (p.Levels.Count + Columns - 1) / Columns);
        var cell = Math.Min((content.Width / 2 - Gap * Columns) / Columns, (content.Height - Gap * rows) / rows);
        for (var i = 0; i < levels.Count; i++)
        {
            var bounds = new Rectangle(
                content.X + i % Columns * (cell + Gap),
                content.Y + i / Columns * (cell + Gap),
                cell,
                cell);
            var selected = i == _selected.Index;
            var fill = selected ? Palette.Accent : progress.IsSolved(levels[i]) ? Palette.Solved : Palette.Panel;
            batch.FillRectangle(bounds, fill);
            batch.DrawText(fonts.Small, (i + 1).ToString(CultureInfo.InvariantCulture), bounds, selected ? Palette.Ink : Palette.Text, shadow: false);
        }

        // Preview on the right.
        var gridWidth = Columns * (cell + Gap);
        var preview = new Rectangle(content.X + gridWidth + Margin, content.Y, content.Width - gridWidth - Margin, content.Height);
        var level = _context.LevelAt(_selected);
        batch.DrawText(fonts.Body, _context.TitleOf(_selected), new Vector2(preview.Center.X, preview.Y), Palette.Text, Align.Center);
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
        _preview.Draw(batch, _previewSession, _context.Tileset.ThemeFor(GameContext.ThemeSeed(_selected)), board);

        var hint = _context.Packs.Count > 1
            ? "Arrows to choose, Tab to switch pack, Enter to play, Esc to go back"
            : "Arrows to choose, Enter to play, Esc to go back";
        batch.DrawText(
            fonts.Small,
            hint,
            new Vector2(viewport.Center.X, viewport.Bottom - fonts.Small.LineHeight),
            Palette.Muted,
            Align.Center,
            shadow: false);
    }

    private void Select(LevelRef where)
    {
        if (where == _selected)
        {
            return;
        }

        _selected = where;
        _previewSession = new LevelSession(_context.LevelAt(where));
        _context.Sounds.Play(Sound.Navigate);
    }
}
