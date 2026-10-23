using FontStashSharp;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;

namespace Sokoban.Desktop.UI;

internal sealed record MenuItem(string Label, Action Select);

/// <summary>A vertical list of options, navigated with up/down and chosen with confirm.</summary>
internal sealed class Menu
{
    private const int RowPadding = 14;
    private const int PillPadding = 40;

    private readonly MenuItem[] _items;
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
    }

    public int Selected { get; set; }

    public void Update(InputState input, ActionMap<GameAction> actions, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(actions);
        switch (_repeater.Update(input, actions, elapsed))
        {
            case GameAction.Up:
                Selected = (Selected - 1 + _items.Length) % _items.Length;
                _sounds.Play(Sound.Navigate);
                break;
            case GameAction.Down:
                Selected = (Selected + 1) % _items.Length;
                _sounds.Play(Sound.Navigate);
                break;
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
            var selected = i == Selected;
            if (selected)
            {
                batch.FillRectangle(row, Palette.Accent);
            }

            batch.DrawText(font, _items[i].Label, row, selected ? Palette.Ink : Palette.Text, shadow: !selected);
        }
    }

    private static int RowHeight(SpriteFontBase font) => (int)font.LineHeight + 2 * RowPadding;
}
