using Games.Input;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;

namespace Games.UI;

/// <summary>
/// A scrolling grid of car cards: drag or use the arrow keys to move through it, tap or Enter to choose. The garage,
/// the fuse picker and the hand builder all use it.
/// </summary>
internal sealed class CardGrid
{
    private readonly GameContext _context;
    private Rectangle _area;
    private int _columns = 1;
    private Point _card;
    private int _gap;
    private float _scroll;
    private Vector2? _dragFrom;
    private float _dragScroll;
    private bool _dragged;

    public CardGrid(GameContext context, IReadOnlyList<CarInstance> cars)
    {
        _context = context;
        Cars = cars;
    }

    public IReadOnlyList<CarInstance> Cars { get; private set; }

    public int Selected { get; set; }

    /// <summary>Cars drawn faded (not allowed here), by id.</summary>
    public Func<CarInstance, bool> Dimmed { get; set; } = _ => false;

    /// <summary>A small label over a card (e.g. its hand slot), or null.</summary>
    public Func<CarInstance, string?> Badge { get; set; } = _ => null;

    public void Show(IReadOnlyList<CarInstance> cars)
    {
        Cars = cars;
        Selected = Math.Clamp(Selected, 0, Math.Max(0, cars.Count - 1));
        _scroll = Math.Min(_scroll, MaxScroll);
    }

    /// <summary>Handles input; returns the car chosen this frame (tapped, or Enter on the selection), if any.</summary>
    public CarInstance? Update(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Cars.Count == 0)
        {
            return null;
        }

        var actions = _context.Actions;
        var before = Selected;
        if (actions.WasPressed(input, GameAction.Left))
        {
            Selected = Math.Max(0, Selected - 1);
        }
        else if (actions.WasPressed(input, GameAction.Right))
        {
            Selected = Math.Min(Cars.Count - 1, Selected + 1);
        }
        else if (actions.WasPressed(input, GameAction.Up))
        {
            Selected = Math.Max(0, Selected - _columns);
        }
        else if (actions.WasPressed(input, GameAction.Down))
        {
            Selected = Math.Min(Cars.Count - 1, Selected + _columns);
        }

        if (Selected != before)
        {
            _context.Sounds.Play(Sound.Navigate);
            KeepInView(Selected);
        }

        // Drag to scroll; a tap that didn't drag chooses the card under it.
        if (input.Cursor.IsDown && _area.Contains(input.Cursor.Position))
        {
            if (_dragFrom is null)
            {
                (_dragFrom, _dragScroll, _dragged) = (input.Cursor.Position, _scroll, false);
            }

            var moved = _dragFrom.Value.Y - input.Cursor.Position.Y;
            _dragged |= Math.Abs(moved) > _context.Fonts.Px(12);
            if (_dragged)
            {
                _scroll = Math.Clamp(_dragScroll + moved, 0, MaxScroll);
            }
        }
        else if (!input.Cursor.IsDown)
        {
            _dragFrom = null;
        }

        if (input.Tap is { } tap && !_dragged)
        {
            for (var i = 0; i < Cars.Count; i++)
            {
                if (CardBounds(i).Contains(tap))
                {
                    Selected = i;
                    return Cars[i];
                }
            }
        }

        return actions.WasPressed(input, GameAction.Confirm) ? Cars[Selected] : null;
    }

    /// <summary>Lays the grid out in <paramref name="area"/> with cards about <paramref name="cardWidth"/> wide, and draws it.</summary>
    public void Draw(SpriteBatch batch, Rectangle area, int cardWidth)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _area = area;
        _gap = _context.Fonts.Px(18);
        _columns = Math.Max(1, (area.Width + _gap) / (cardWidth + _gap));
        var width = Math.Min(cardWidth, (area.Width - (_gap * (_columns - 1))) / _columns);
        _card = CarCard.Size(width);
        _scroll = Math.Min(_scroll, MaxScroll);

        using var clip = batch.Clip(area, SamplerState.PointClamp);
        var data = _context.PaddockData;
        for (var i = 0; i < Cars.Count; i++)
        {
            var bounds = CardBounds(i);
            if (bounds.Bottom < area.Top || bounds.Top > area.Bottom)
            {
                continue;
            }

            var car = Cars[i];
            CarCard.Draw(batch, _context.Fonts, data, car, bounds, highlight: i == Selected, alpha: Dimmed(car) ? 0.35f : 1);
            if (Badge(car) is { } badge)
            {
                var font = _context.Fonts.Small;
                var size = font.MeasureString(badge);
                var box = new Rectangle(bounds.Center.X - ((int)size.X / 2) - 8, bounds.Y - ((int)font.LineHeight / 2), (int)size.X + 16, (int)font.LineHeight);
                batch.FillRectangle(box, Palette.Accent);
                batch.DrawText(font, badge, new Vector2(box.Center.X, box.Y), Palette.Ink, Align.Center, shadow: false);
            }
        }
    }

    private float MaxScroll
    {
        get
        {
            var rows = (Cars.Count + _columns - 1) / _columns;
            return Math.Max(0, (rows * (_card.Y + _gap)) + _gap - _area.Height);
        }
    }

    private Rectangle CardBounds(int index)
    {
        var total = (_columns * _card.X) + ((_columns - 1) * _gap);
        var left = _area.Center.X - (total / 2);
        var (row, column) = (index / _columns, index % _columns);
        return new Rectangle(left + (column * (_card.X + _gap)), _area.Y + _gap + (row * (_card.Y + _gap)) - (int)_scroll, _card.X, _card.Y);
    }

    private void KeepInView(int index)
    {
        var bounds = CardBounds(index);
        if (bounds.Top < _area.Top + _gap)
        {
            _scroll -= _area.Top + _gap - bounds.Top;
        }
        else if (bounds.Bottom > _area.Bottom - _gap)
        {
            _scroll += bounds.Bottom - (_area.Bottom - _gap);
        }

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }
}
