using ConnectWord;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// A Connect Word level: the categories matched so far as coloured rows on top, the grid of word cards below. Drag a
/// card onto another (or tap one, then the other; or the arrow keys and Enter): the same category merges them, any
/// other card or an empty cell trades places. A row holding every word of a category is matched. Cards glide to where
/// they belong, so falls and new cards from the deck show.
/// </summary>
internal sealed class WordPlayScene : Scene
{
    private const float DragThreshold = 12;

    private static readonly Color[] BannerColors =
    [
        new(246, 200, 72), new(122, 196, 120), new(110, 160, 232), new(178, 132, 222), new(240, 140, 90), new(232, 110, 150),
    ];

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly Dictionary<int, Vector2> _drawnAt = [];
    private Rectangle _grid;
    private int _cell;
    private Cell? _picked;
    private Vector2 _pressedAt;
    private bool _dragging;
    private Vector2 _cursor;
    private Cell _keyCursor = new(0, 3);
    private bool _keys;
    private (Cell From, Cell To)? _hint;
    private TimeSpan _hintFor;
    private bool _wasDown;
    private Cell? _pressCell;
    private float _seconds = 1 / 60f;
    private readonly Dictionary<int, SquashStretch> _pops = [];
    private readonly List<Ghost> _ghosts = [];
    private readonly List<Rectangle> _banners = [];

    /// <summary>
    /// A card on its way out: one dropped onto its kind sliding into it (<see cref="Cell"/>), or one of a matched row
    /// flying up into its banner (<see cref="Banner"/>). Drawn over the grid, shrinking as it goes.
    /// </summary>
    private sealed record Ghost(WordCard Card, Vector2 From, Cell? Cell, int Banner, float Length)
    {
        public float Age { get; set; }
    }

    public WordPlayScene(GameContext context, int level)
        : this(context, WordPuzzle.FromList(context, level))
    {
    }

    public WordPlayScene(GameContext context, WordPuzzle puzzle)
    {
        _context = context;
        Puzzle = puzzle;
        Game = new ConnectWordGame(puzzle.Level, puzzle.Seed);
        Game.DrainEvents();
        _buttons = new ButtonBar(
            new Button(T("Hint"), ShowHint),
            new Button(T("Undo"), Undo),
            new Button(T("Restart"), Restart),
            new Button(T("Menu"), () => Scenes.Push(new WordPauseScene(context, this))));
    }

    public WordPuzzle Puzzle { get; }

    /// <summary>The level, from 0 (or the date, or the seed).</summary>
    public int Number => Puzzle.Number;

    public ConnectWordGame Game { get; private set; }

    /// <summary>How long the puzzle has been played (paused while a menu is open).</summary>
    public TimeSpan Time { get; private set; }

    public override IEnumerable<string> DebugInfo =>
        [$"{Puzzle.Kind} {Number}  moves {Game.Moves}  par {Game.Par}  deck {Game.DeckCount}  solved {Game.Solved.Count}/{Game.Level.Categories.Count}"];

    public void Restart() => Scenes.Replace(new WordPlayScene(_context, Puzzle), Navigation.Fade());

    /// <summary>The next level of the list, or another random puzzle.</summary>
    public void NextLevel() => Scenes.Replace(
        new WordPlayScene(_context, Puzzle.Kind == PuzzleKind.Random ? WordPuzzle.Random(_context) : WordPuzzle.FromList(_context, Number + 1)),
        Navigation.Fade());

    /// <summary>Takes the last move back.</summary>
    public void Undo()
    {
        if (Game.IsWon || Game.History.Count == 0)
        {
            _context.Sounds.Play(Sound.Error);
            return;
        }

        Game = Game.Undone();
        _picked = null;
        _hint = null;
        _context.Sounds.Play(Sound.Card);
    }

    protected override void OnEnter() => _context.PlayMood(Music.Words);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        _cursor = input.Cursor.Position;
        _seconds = (float)elapsed.TotalSeconds;
        foreach (var ghost in _ghosts)
        {
            ghost.Age += _seconds;
        }

        _ghosts.RemoveAll(g => g.Age >= g.Length);
        _hintFor -= elapsed;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new WordPauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input) || Game.IsWon)
        {
            return;
        }

        Time += elapsed;
        foreach (var pop in _pops.Values)
        {
            pop.Update(elapsed);
        }

        if (actions.WasPressed(input, GameAction.Undo))
        {
            Undo();
        }

        if (actions.WasPressed(input, GameAction.Hint))
        {
            ShowHint();
        }

        if (actions.WasPressed(input, GameAction.Restart))
        {
            Restart();
            return;
        }

        Keys(input);
        Pointer(input);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(64));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, Puzzle.Title, text, Palette.Text, Align.Left);
        if (Puzzle.Kind != PuzzleKind.Level)
        {
            batch.DrawText(fonts.Body, Clock(Time), text, Palette.Muted, Align.Center);
        }

        batch.DrawText(fonts.Body, T("Moves {0}", Game.Moves), text, Palette.Accent, Align.Right);

        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        _buttons.Draw(batch, fonts.Small, buttons);

        // The matched categories as rows on top, then the grid in the room left, square cells.
        var width = Math.Min(viewport.Width - (2 * margin), fonts.Px(720));
        var left = viewport.Center.X - (width / 2);
        var banner = fonts.Px(46);
        var top = hud.Bottom + margin;
        _banners.Clear();
        for (var i = 0; i < Game.Solved.Count; i++)
        {
            var bounds = new Rectangle(left, top, width, banner - fonts.Px(6));
            _banners.Add(bounds);

            // A banner whose cards are still flying up fills in as they arrive.
            var arriving = _ghosts.Where(g => g.Banner == i).Select(g => g.Age / g.Length).DefaultIfEmpty(1).Min();
            if (arriving >= 0.6f)
            {
                DrawBanner(batch, bounds, Game.Solved[i], BannerColors[i % BannerColors.Length] * Math.Clamp((arriving - 0.6f) / 0.4f, 0, 1));
            }

            top += banner;
        }

        var room = new Rectangle(left, top + fonts.Px(4), width, buttons.Top - top - margin);
        _cell = Math.Max(10, Math.Min(room.Width / Game.Columns, room.Height / Game.Rows));
        _grid = new Rectangle(room.Center.X - (_cell * Game.Columns / 2), room.Y, _cell * Game.Columns, _cell * Game.Rows);
        batch.FillRoundedRectangle(_grid, fonts.Px(12), Palette.Ink);

        var elapsed = _seconds;
        foreach (var (cell, card) in Game.Cards.OrderBy(c => _picked == c.Cell && _dragging))
        {
            var target = CellBounds(cell).Location.ToVector2();

            // New cards start above the grid and fall in; the rest glide to their place.
            var at = _drawnAt.TryGetValue(card.Id, out var was) ? was : target with { Y = _grid.Y - _cell };
            at = Vector2.Lerp(at, target, 1 - MathF.Exp(-18 * elapsed));
            _drawnAt[card.Id] = at;
            var bounds = new Rectangle((int)at.X, (int)at.Y, _cell, _cell);
            if (_pops.TryGetValue(card.Id, out var pop) && pop.Scale != Vector2.One)
            {
                // A card just merged swells and wobbles back.
                var size = new Vector2(_cell) * pop.Scale;
                bounds = new Rectangle((int)(bounds.Center.X - (size.X / 2)), (int)(bounds.Center.Y - (size.Y / 2)), (int)size.X, (int)size.Y);
            }

            if (_picked == cell && _dragging)
            {
                bounds.Location = (_cursor - new Vector2(_cell / 2f)).ToPoint();
            }

            DrawCard(batch, card, bounds, cell);
        }

        if (_keys && !Game.IsWon)
        {
            batch.StrokeRectangle(CellBounds(_keyCursor), Palette.Accent, Math.Max(2, fonts.Px(3)));
        }

        DrawGhosts(batch);

    }

    public static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private Rectangle CellBounds(Cell cell) => new(_grid.X + (cell.Column * _cell), _grid.Y + (cell.Row * _cell), _cell, _cell);

    private Cell? CellAt(Vector2 point)
    {
        if (!_grid.Contains(point))
        {
            return null;
        }

        return new Cell((int)((point.X - _grid.X) / _cell), (int)((point.Y - _grid.Y) / _cell));
    }

    // A drag drops where it ends; a tap picks a card up and a second tap puts it down (on itself: put back).
    private void Pointer(InputState input)
    {
        var (down, at) = (input.Cursor.IsDown, input.Cursor.Position);
        if (down && !_wasDown)
        {
            (_pressCell, _pressedAt, _dragging) = (CellAt(at), at, false);
        }
        else if (down && _picked is null && _pressCell is { } from && Game[from] is not null
            && Vector2.Distance(at, _pressedAt) > _context.Fonts.Px(DragThreshold))
        {
            (_picked, _dragging, _keys) = (from, true, false);
        }
        else if (!down && _wasDown)
        {
            if (_dragging && _picked is { } dragged)
            {
                _picked = null;
                _dragging = false;
                if (CellAt(at) is { } to)
                {
                    Drop(dragged, to);
                }
            }
            else if (_pressCell is { } tapped && CellAt(at) == tapped)
            {
                _keys = false;
                if (_picked is not { } picked)
                {
                    _picked = Game[tapped] is null ? null : tapped;
                }
                else
                {
                    _picked = null;
                    Drop(picked, tapped);
                }
            }

            _pressCell = null;
        }

        _wasDown = down;
    }

    private void Keys(InputState input)
    {
        var actions = _context.Actions;
        var (dx, dy) = (actions.WasPressed(input, GameAction.Right) ? 1 : actions.WasPressed(input, GameAction.Left) ? -1 : 0,
            actions.WasPressed(input, GameAction.Down) ? 1 : actions.WasPressed(input, GameAction.Up) ? -1 : 0);
        if (dx != 0 || dy != 0)
        {
            _keys = true;
            _keyCursor = new Cell(Math.Clamp(_keyCursor.Column + dx, 0, Game.Columns - 1), Math.Clamp(_keyCursor.Row + dy, 0, Game.Rows - 1));
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            _keys = true;
            if (_picked is { } picked)
            {
                _picked = null;
                Drop(picked, _keyCursor);
            }
            else if (Game[_keyCursor] is not null)
            {
                _picked = _keyCursor;
            }
        }
    }

    private void Drop(Cell from, Cell to)
    {
        if (from == to)
        {
            return;
        }

        // Where the dropped card was let go, and where every card is drawn: what the ghosts set out from.
        var dropped = Game.Cards.FirstOrDefault(c => c.Cell == from).Card;
        var letGo = _dragging || _picked == from ? _cursor - new Vector2(_cell / 2f) : _drawnAt.GetValueOrDefault(dropped?.Id ?? -1, CellBounds(from).Location.ToVector2());
        var kind = Game.Drop(from, to);
        _hint = null;
        _context.Sounds.Play(kind switch
        {
            MoveKind.Merged => Sound.Pickup,
            MoveKind.None => Sound.Error,
            _ => Sound.Card,
        });
        foreach (var e in Game.DrainEvents())
        {
            switch (e)
            {
                case CardsMerged merged:
                    if (dropped is not null)
                    {
                        _ghosts.Add(new Ghost(dropped, letGo, merged.At, -1, 0.22f));
                    }

                    Scenes.Effects.Emit(CellBounds(merged.At).Center.ToVector2(), ParticleEffects.Sparkles, _cell / 90f);
                    if (!_pops.TryGetValue(merged.Card.Id, out var pop))
                    {
                        _pops[merged.Card.Id] = pop = new SquashStretch();
                    }

                    pop.Trigger(0.18f, TimeSpan.FromMilliseconds(450));
                    break;
                case CategoryMatched matched:
                    _context.Sounds.Play(Sound.LevelUp);
                    var index = Game.Solved.Count - 1;
                    foreach (var (card, i) in matched.Cards.Select((c, i) => (c, i)))
                    {
                        var start = _drawnAt.GetValueOrDefault(card.Id, CellBounds(new Cell(i, matched.Row)).Location.ToVector2());
                        _ghosts.Add(new Ghost(card, start, null, index, 0.45f + (i * 0.06f)));
                    }

                    break;
                case LevelWon won:
                    _context.Sounds.Play(Sound.Fanfare);
                    Celebration.Confetti(Scenes);
                    var best = Puzzle.Kind switch
                    {
                        PuzzleKind.Level => _context.Words.Record(Number, won.Moves, won.Stars),
                        PuzzleKind.Daily => _context.Words.RecordDaily(Number, (int)Time.TotalSeconds, won.Moves),
                        _ => false,
                    };
                    Timers.After(TimeSpan.FromSeconds(0.9), () => Scenes.Push(new WordWonScene(_context, this, best)));
                    break;
            }
        }
    }

    private void ShowHint()
    {
        if (Game.Hint() is { } hint)
        {
            (_hint, _hintFor) = (hint, TimeSpan.FromSeconds(2));
            _context.Sounds.Play(Sound.Navigate);
        }
    }

    private void DrawCard(SpriteBatch batch, WordCard card, Rectangle bounds, Cell cell)
    {
        var fonts = _context.Fonts;
        var pad = Math.Max(2, _cell / 22);
        var face = bounds;
        face.Inflate(-pad, -pad);
        var picked = _picked == cell;
        var hinted = _hint is { } h && _hintFor > TimeSpan.Zero && (h.From == cell || h.To == cell);
        batch.FillRoundedRectangle(face with { Y = face.Y + pad }, fonts.Px(10), Palette.Shadow);
        batch.FillRoundedRectangle(face, fonts.Px(10), picked ? new Color(255, 236, 170) : hinted ? new Color(200, 236, 255) : new Color(250, 246, 236));

        // One word large; a merged card lists its words smaller, with how many it holds in the corner.
        var inner = face;
        inner.Inflate(-fonts.Px(6), -fonts.Px(6));
        var lines = card.Words.Count <= 4 ? card.Words : [.. card.Words.Take(3), $"+{card.Words.Count - 3}"];
        var size = Fit(lines, inner.Width, inner.Height);
        var font = fonts.Sized(size);
        var lineHeight = font.LineHeight;
        var y = inner.Center.Y - (lineHeight * lines.Count / 2f);
        foreach (var line in lines)
        {
            batch.DrawText(font, line, new Rectangle(inner.X, (int)y, inner.Width, (int)lineHeight), Palette.Ink, Align.Center, shadow: false);
            y += lineHeight;
        }

        if (card.IsMerged)
        {
            var badge = fonts.Px(26);
            var at = new Vector2(face.Right - (badge * 0.6f), face.Y + (badge * 0.6f));
            batch.FillCircle(at, badge / 2f, Palette.Accent);
            batch.DrawText(fonts.Sized(badge * 0.7f), card.Words.Count.ToString(Culture), new Rectangle((int)(at.X - badge), (int)(at.Y - (badge / 2f)), badge * 2, badge), Palette.Ink, Align.Center, shadow: false);
        }

        if (picked || hinted)
        {
            batch.StrokeRectangle(face, picked ? Palette.Accent : new Color(110, 160, 232), Math.Max(2, fonts.Px(3)));
        }
    }

    /// <summary>The ghosts: sliding into a card, or flying up into a banner, shrinking on the way.</summary>
    private void DrawGhosts(SpriteBatch batch)
    {
        foreach (var ghost in _ghosts)
        {
            var t = Easing.CubicOut(Math.Clamp(ghost.Age / ghost.Length, 0, 1));
            Rectangle target;
            if (ghost.Cell is { } cell)
            {
                target = CellBounds(cell);
                target.Inflate(-_cell / 4, -_cell / 4);
            }
            else if (ghost.Banner >= 0 && ghost.Banner < _banners.Count)
            {
                // Squeezed to the banner's height, at its middle.
                var banner = _banners[ghost.Banner];
                target = new Rectangle(banner.Center.X - (banner.Height / 2), banner.Y, banner.Height, banner.Height);
            }
            else
            {
                continue;
            }

            var size = MathHelper.Lerp(_cell, target.Width, t);
            var at = Vector2.Lerp(ghost.From, target.Location.ToVector2() + new Vector2((target.Width - size) / 2), t);
            DrawCard(batch, ghost.Card, new Rectangle((int)at.X, (int)at.Y, (int)size, (int)size), new Cell(-1, -1));
        }
    }

    /// <summary>The largest text size at which every line fits the width and all of them the height.</summary>
    private float Fit(IReadOnlyList<string> lines, int width, int height)
    {
        var size = Math.Min(_cell * 0.24f, height / (float)lines.Count * 0.8f);
        var font = _context.Fonts.Sized(size);
        var widest = lines.Max(l => font.MeasureString(l).X);
        return widest > width ? size * width / widest : size;
    }

    private void DrawBanner(SpriteBatch batch, Rectangle bounds, int category, Color color)
    {
        var fonts = _context.Fonts;
        var level = Game.Level.Categories[category];
        batch.FillRoundedRectangle(bounds, fonts.Px(10), color);
        var inner = bounds;
        inner.Inflate(-fonts.Px(12), 0);
        var font = fonts.Sized(bounds.Height * 0.42f);
        batch.DrawText(font, level.Name.ToUpperInvariant(), inner, Palette.Ink, Align.Left, shadow: false);
        var words = string.Join(", ", level.Words);
        var small = fonts.Sized(Math.Min(bounds.Height * 0.36f, bounds.Height * 0.36f * inner.Width * 0.72f / Math.Max(1, fonts.Sized(bounds.Height * 0.36f).MeasureString(words).X)));
        batch.DrawText(small, words, inner, Palette.Ink, Align.Right, shadow: false);
    }
}
