using System.Globalization;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Uno;

namespace Games.Rendering;

/// <summary>
/// Uno cards drawn in code: the card kit's rounded face tinted in the card's colour, a tilted white oval in
/// the middle carrying the number or symbol, and small labels in two corners. Wilds are black with a
/// four-colour oval. Backs reuse the card kit's back.
/// </summary>
internal sealed class UnoArt : IDisposable
{
    public static readonly Color Red = new(214, 40, 52);
    public static readonly Color Yellow = new(240, 186, 30);
    public static readonly Color Green = new(46, 160, 82);
    public static readonly Color Blue = new(32, 108, 204);
    private static readonly Color Black = new(34, 34, 40);
    private const int Size = 128;
    private const int Samples = 3;

    private readonly CardArt _cards;
    private readonly ITextDrawer _text;
    private readonly Texture2D _oval;
    private readonly Texture2D _wildOval;
    private readonly Texture2D _skip;
    private readonly Texture2D _reverse;

    public UnoArt(GraphicsDevice device, CardArt cards, ITextDrawer text)
    {
        _cards = cards;
        _text = text;
        _oval = Shape(device, (x, y) => Oval(x, y) ? Color.White : Color.Transparent);
        _wildOval = Shape(device, (x, y) => !Oval(x, y) ? Color.Transparent
            : x < 0 ? (y < 0 ? Red : Yellow)
            : y < 0 ? Blue : Green);
        _skip = Shape(device, (x, y) =>
        {
            var r = MathF.Sqrt(x * x + y * y);
            var ring = r is > 0.55f and < 0.85f;
            var bar = MathF.Abs(x + y) < 0.2f && r < 0.7f;
            return ring || bar ? Color.White : Color.Transparent;
        });
        _reverse = Shape(device, (x, y) =>
        {
            // Two arrows, one above pointing right, one below pointing left.
            static bool Arrow(float x, float y) => (MathF.Abs(y) < 0.13f && x > -0.75f && x < 0.3f) || (x >= 0.3f && x < 0.75f && MathF.Abs(y) < 0.75f - x);
            return Arrow(x, (y + 0.38f) * 1.4f) || Arrow(-x, (y - 0.38f) * 1.4f) ? Color.White : Color.Transparent;
        });
    }

    public static float AspectRatio => CardArt.AspectRatio;

    public static Color ColorOf(UnoColor color) => color switch
    {
        UnoColor.Red => Red,
        UnoColor.Yellow => Yellow,
        UnoColor.Green => Green,
        _ => Blue,
    };

    public void DrawBack(SpriteBatch batch, Rectangle bounds) => batch.Draw(_cards.Back, bounds, Color.White);

    /// <param name="dim">Greys the card out, e.g. one that cannot be played now.</param>
    public void Draw(SpriteBatch batch, UnoCard card, Rectangle bounds, bool dim = false)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var colour = card.Color is { } c ? ColorOf(c) : Black;
        var shade = dim ? new Color(150, 150, 160) : Color.White;
        batch.Draw(_cards.Face, bounds, Multiply(colour, shade));

        var ovalSize = (int)(bounds.Width * 0.86f);
        var oval = new Rectangle(bounds.Center.X - ovalSize / 2, bounds.Center.Y - ovalSize / 2, ovalSize, ovalSize);
        batch.Draw(card.IsWild ? _wildOval : _oval, oval, shade);

        var ink = card.IsWild ? Color.White : Multiply(colour, shade);
        var symbol = new Rectangle(bounds.Center.X - bounds.Width / 4, bounds.Center.Y - bounds.Width / 4, bounds.Width / 2, bounds.Width / 2);
        switch (card.Value)
        {
            case UnoValue.Skip:
                batch.Draw(_skip, symbol, ink);
                break;
            case UnoValue.Reverse:
                batch.Draw(_reverse, symbol, ink);
                break;
            case UnoValue.Wild:
                break;
            default:
                var label = card.Label;
                var size = bounds.Width * (label.Length > 1 ? 0.36f : 0.5f);
                var measured = _text.Measure(label, size);
                var at = new Vector2(bounds.Center.X - measured.X / 2, bounds.Center.Y - measured.Y / 2);
                if (card.IsWild)
                {
                    _text.Draw(batch, label, at + new Vector2(2, 2), size, Black);
                }

                _text.Draw(batch, label, at, size, ink);
                break;
        }

        var corner = Corner(card);
        var small = bounds.Width * 0.2f;
        _text.Draw(batch, corner, new Vector2(bounds.X + bounds.Width * 0.09f, bounds.Y + bounds.Height * 0.04f), small, shade);
        var width = _text.Measure(corner, small).X;
        _text.Draw(batch, corner, new Vector2(bounds.Right - bounds.Width * 0.09f - width, bounds.Bottom - bounds.Height * 0.04f - small * 1.25f), small, shade);
    }

    public void Dispose()
    {
        _oval.Dispose();
        _wildOval.Dispose();
        _skip.Dispose();
        _reverse.Dispose();
    }

    private static string Corner(UnoCard card) => card.Value switch
    {
        UnoValue.Skip => "S",
        UnoValue.Reverse => "R",
        UnoValue.Wild => "W",
        _ => card.IsNumber ? ((int)card.Value).ToString(CultureInfo.InvariantCulture) : card.Label,
    };

    // A tilted ellipse, as on the real cards.
    private static bool Oval(float x, float y)
    {
        const float Tilt = 0.5f;
        var (cos, sin) = (MathF.Cos(Tilt), MathF.Sin(Tilt));
        var (u, v) = (x * cos + y * sin, -x * sin + y * cos);
        return u * u / 0.55f + v * v / 0.98f <= 1;
    }

    private static Color Multiply(Color a, Color b) => new(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, a.A);

    /// <summary>A square texture painted by <paramref name="paint"/> over [-1, 1]², supersampled for smooth edges.</summary>
    private static Texture2D Shape(GraphicsDevice device, Func<float, float, Color> paint)
    {
        var pixels = new Color[Size * Size];
        for (var py = 0; py < Size; py++)
        {
            for (var px = 0; px < Size; px++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var c = paint((px + (sx + 0.5f) / Samples) / Size * 2 - 1, (py + (sy + 0.5f) / Samples) / Size * 2 - 1);
                        r += c.R * c.A;
                        g += c.G * c.A;
                        b += c.B * c.A;
                        a += c.A;
                    }
                }

                pixels[py * Size + px] = a == 0 ? Color.Transparent : new Color(r / a, g / a, b / a, a / (Samples * Samples));
            }
        }

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(pixels);
        return texture;
    }
}
