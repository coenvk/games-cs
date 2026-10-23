using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Sokoban.Desktop.UI;

internal enum Align
{
    Left,
    Center,
    Right,
}

internal static class TextExtensions
{
    private static readonly Vector2 ShadowOffset = new(0, 3);

    /// <summary>Draws text with its top edge at <paramref name="position"/>, aligned horizontally around it.</summary>
    public static void DrawText(
        this SpriteBatch batch,
        SpriteFontBase font,
        string text,
        Vector2 position,
        Color color,
        Align align = Align.Left,
        bool shadow = true)
    {
        ArgumentNullException.ThrowIfNull(font);
        var width = font.MeasureString(text).X;
        position.X -= align switch
        {
            Align.Center => width / 2,
            Align.Right => width,
            _ => 0,
        };
        position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));

        if (shadow)
        {
            font.DrawText(batch, text, position + ShadowOffset, Palette.Shadow);
        }

        font.DrawText(batch, text, position, color);
    }

    /// <summary>Draws text vertically centred in <paramref name="bounds"/>.</summary>
    public static void DrawText(
        this SpriteBatch batch,
        SpriteFontBase font,
        string text,
        Rectangle bounds,
        Color color,
        Align align = Align.Center,
        bool shadow = true)
    {
        ArgumentNullException.ThrowIfNull(font);
        var x = align switch
        {
            Align.Center => bounds.Center.X,
            Align.Right => bounds.Right,
            _ => bounds.Left,
        };
        var y = bounds.Center.Y - font.LineHeight / 2f;
        batch.DrawText(font, text, new Vector2(x, y), color, align, shadow);
    }
}
