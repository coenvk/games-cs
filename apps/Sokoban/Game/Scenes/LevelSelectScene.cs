using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.Rendering;
using Games.UI;
using Sokoban.Model;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// A grid of level numbers per pack, with a live preview of the selected level. Tap a level to select
/// it and again (or Play) to start it; tap a pack name to switch packs.
/// </summary>
internal sealed class LevelSelectScene : Scene
{
    private const int Columns = 10;
    private const int Margin = 48;
    private const int Gap = 8;
    private const int TabGap = 32;

    private readonly GameContext _context;
    private readonly BoardRenderer _preview;
    private readonly ButtonBar _buttons;
    private readonly ActionRepeater<GameAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(70),
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right);

    private LevelRef _selected;
    private LevelSession _previewSession;
    private Rectangle[] _tiles = [];
    private Rectangle[] _tabs = [];

    public LevelSelectScene(GameContext context, LevelRef selected)
    {
        _context = context;
        _preview = new BoardRenderer(context.Tileset);
        _selected = selected;
        _previewSession = new LevelSession(context.LevelAt(selected));
        _buttons = new ButtonBar(
            new Button(T("Back"), Back),
            new Button(T("Play"), Play, Highlight: true));
    }

    private LevelPack Pack => _context.Packs[_selected.Pack];

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Puzzle);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Back();
            return;
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Play();
            return;
        }

        if (actions.WasPressed(input, GameAction.SwitchPack))
        {
            SwitchPack((_selected.Pack + 1) % _context.Packs.Count);
            return;
        }

        if (_buttons.Update(input) || HandleTaps(input))
        {
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
        // Phones get thin margins so the level grid can be as wide, and its tiles as easy to tap, as possible.
        var margin = fonts.IsPortrait ? fonts.Px(Margin / 3) : fonts.Px(Margin);
        var gap = Math.Max(4, fonts.Px(Gap));
        viewport.Inflate(-margin, -margin);
        var progress = _context.Progress.Progress;
        var levels = Pack.Levels;

        // Header: title, then one tab per pack (the current one underlined), then the solved count.
        // In portrait the tabs and count get their own row under the title.
        var headingHeight = (int)fonts.Heading.LineHeight;
        batch.DrawText(fonts.Heading, T("Select level"), new Vector2(viewport.X, viewport.Y), Palette.Text);
        var tabRow = fonts.IsPortrait
            ? new Rectangle(viewport.X, viewport.Y + headingHeight, viewport.Width, (int)fonts.Body.LineHeight + fonts.Px(12))
            : new Rectangle(viewport.X, viewport.Y, viewport.Width, headingHeight);
        var tabX = fonts.IsPortrait ? tabRow.X : tabRow.X + (int)fonts.Heading.MeasureString(T("Select level")).X + 2 * fonts.Px(TabGap);
        _tabs = new Rectangle[_context.Packs.Count];
        for (var p = 0; p < _context.Packs.Count; p++)
        {
            var name = T(_context.Packs[p].Name);
            var tab = new Rectangle(tabX, tabRow.Y, (int)fonts.Body.MeasureString(name).X, tabRow.Height);
            _tabs[p] = tab;
            batch.DrawText(fonts.Body, name, tab, p == _selected.Pack ? Palette.Accent : Palette.Muted, Align.Left, shadow: false);
            if (p == _selected.Pack)
            {
                batch.FillRectangle(new Rectangle(tab.X, tab.Bottom - 4, tab.Width, 4), Palette.Accent);
            }

            tabX = tab.Right + fonts.Px(TabGap);
        }

        batch.DrawText(
            fonts.Body,
            T("{0} / {1} solved", progress.SolvedCount(levels), levels.Count),
            tabRow,
            Palette.Muted,
            Align.Right,
            shadow: false);

        var buttonsHeight = ButtonBar.Height(fonts.Body) + fonts.Px(16);
        var content = viewport;
        content.Y = tabRow.Bottom + fonts.Px(20);
        content.Height = viewport.Bottom - buttonsHeight - content.Y;
        _buttons.Draw(batch, fonts.Body, new Rectangle(viewport.X, viewport.Bottom - buttonsHeight, viewport.Width, buttonsHeight));

        // Grid sized for the biggest pack so switching packs keeps the layout steady. Landscape: grid
        // on the left, preview on the right. Portrait: grid across the top, preview underneath.
        var rows = _context.Packs.Max(p => (p.Levels.Count + Columns - 1) / Columns);
        var gridArea = fonts.IsPortrait
            ? new Rectangle(content.X, content.Y, content.Width, content.Height * 62 / 100)
            : new Rectangle(content.X, content.Y, content.Width / 2, content.Height);
        var cell = Math.Max(8, Math.Min((gridArea.Width - gap * (Columns - 1)) / Columns, (gridArea.Height - gap * (rows - 1)) / rows));
        var gridWidth = Columns * cell + (Columns - 1) * gap;
        var gridLeft = fonts.IsPortrait ? gridArea.X + (gridArea.Width - gridWidth) / 2 : gridArea.X;
        _tiles = new Rectangle[levels.Count];
        for (var i = 0; i < levels.Count; i++)
        {
            var bounds = new Rectangle(gridLeft + i % Columns * (cell + gap), gridArea.Y + i / Columns * (cell + gap), cell, cell);
            _tiles[i] = bounds;
            var selected = i == _selected.Index;
            var fill = selected ? Palette.Accent : progress.IsSolved(levels[i]) ? Palette.Solved : Palette.Panel;
            batch.FillRectangle(bounds, fill);
            batch.DrawText(fonts.Small, (i + 1).ToString(CultureInfo.InvariantCulture), bounds, selected ? Palette.Ink : Palette.Text, shadow: false);
        }

        var gridHeight = rows * cell + (rows - 1) * gap;
        var previewTop = gridArea.Y + gridHeight + fonts.Px(24);
        var preview = fonts.IsPortrait
            ? new Rectangle(content.X, previewTop, content.Width, content.Bottom - previewTop)
            : new Rectangle(gridLeft + gridWidth + margin, content.Y, content.Right - (gridLeft + gridWidth + margin), content.Height);
        var level = _context.LevelAt(_selected);
        batch.DrawText(fonts.Body, _context.TitleOf(_selected), new Vector2(preview.Center.X, preview.Y), Palette.Text, Align.Center);
        var record = progress.Get(level);
        batch.DrawText(
            fonts.Small,
            record is null ? N(level.Board.Goals.Count, "{0} crate, not solved yet", "{0} crates, not solved yet") : Format.Best(record),
            new Vector2(preview.Center.X, preview.Y + fonts.Body.LineHeight + 4),
            record is null ? Palette.Muted : Palette.Accent,
            Align.Center,
            shadow: false);

        var board = preview;
        var header = (int)(fonts.Body.LineHeight + fonts.Small.LineHeight) + fonts.Px(24);
        board.Y += header;
        board.Height -= header;
        _preview.Draw(batch, _previewSession, _context.Tileset.ThemeFor(GameContext.ThemeSeed(_selected)), board);
    }

    /// <summary>Tabs switch packs; a tile selects its level, or plays it if it was already selected.</summary>
    private bool HandleTaps(InputState input)
    {
        for (var p = 0; p < _tabs.Length; p++)
        {
            if (input.Tapped(_tabs[p]))
            {
                SwitchPack(p);
                return true;
            }
        }

        for (var i = 0; i < _tiles.Length; i++)
        {
            if (input.Tapped(_tiles[i]))
            {
                if (i == _selected.Index)
                {
                    Play();
                }
                else
                {
                    Select(_selected with { Index = i });
                }

                return true;
            }
        }

        return false;
    }

    private void Back() => Scenes.Pop(Navigation.Fade());

    private void Play() => Scenes.Push(new PlayScene(_context, _selected), Navigation.Fade());

    private void SwitchPack(int pack)
    {
        if (pack != _selected.Pack)
        {
            Select(new LevelRef(pack, _context.Progress.Progress.FirstUnsolved(_context.Packs[pack].Levels) ?? 0));
        }
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
