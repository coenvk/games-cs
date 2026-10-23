using FontStashSharp;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;

namespace Sokoban.Desktop.UI;

internal sealed record MenuItem(string Label, Action Select);

/// <summary>
/// A vertical list of options: up/down and confirm, or tap/click an item. A mouse hovering over an
/// item selects it. Hit areas are the rows as last drawn, which is the frame the player is looking at.
/// </summary>
internal sealed class Menu
{
    private const int RowPadding = 14;
    private const int PillPadding = 40;

    private readonly MenuItem[] _items;
    private readonly Rectangle[] _rows;
    private readonly SoundPlayer _sounds;
    private readonly ActionRepeater<GameAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(120),
        GameAction.Up,
        GameAction.Down);

    public Menu(SoundPlayer sounds, params MenuItem[] items)
    {
        _sounds = sounds;
        if (items.Length == 0)
        {
            throw new ArgumentException("A menu needs at least one item.", nameof(items));
        }

        _items = items;
        _rows = new Rectangle[items.Length];
    }

    public int Selected { get; set; }

    public void Update(InputState input, ActionMap<GameAction> actions, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actions);
        switch (_repeater.Update(input, actions, elapsed))
        {
            case GameAction.Up:
                Select((Selected - 1 + _items.Length) % _items.Length);
                break;
            case GameAction.Down:
                Select((Selected + 1) % _items.Length);
                break;
        }

        for (var i = 0; i < _rows.Length; i++)
        {
            if (input.Tapped(_rows[i]))
            {
                Selected = i;
                _items[i].Select();
                return;
            }

            if (input.PointerMoved && !input.Cursor.IsDown && _rows[i].Contains(input.Cursor.Position))
            {
                Select(i);
            }
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            _items[Selected].Select();
        }
    }

    public int Height(SpriteFontBase font) => _items.Length * RowHeight(font);

    /// <summary>Draws the menu centred horizontally on <paramref name="centerX"/>, starting at <paramref name="top"/>.</summary>
    public void Draw(SpriteBatch batch, SpriteFontBase font, int centerX, int top)
    {
        ArgumentNullException.ThrowIfNull(font);
        var rowHeight = RowHeight(font);
        var pillWidth = (int)_items.Max(i => font.MeasureString(i.Label).X) + 2 * PillPadding;

        for (var i = 0; i < _items.Length; i++)
        {
            var row = new Rectangle(centerX - pillWidth / 2, top + i * rowHeight, pillWidth, rowHeight - 6);
            _rows[i] = row;
            var selected = i == Selected;
            if (selected)
            {
                batch.FillRectangle(row, Palette.Accent);
            }

            batch.DrawText(font, _items[i].Label, row, selected ? Palette.Ink : Palette.Text, shadow: !selected);
        }
    }

    private static int RowHeight(SpriteFontBase font) => (int)font.LineHeight + 2 * RowPadding;

    private void Select(int index)
    {
        if (index != Selected)
        {
            Selected = index;
            _sounds.Play(Sound.Navigate);
        }
    }
}
