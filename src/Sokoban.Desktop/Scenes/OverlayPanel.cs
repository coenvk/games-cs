using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Dims the scene below and draws a centred panel with a title, an optional subtitle and a menu.</summary>
internal static class OverlayPanel
{
    private const int Padding = 40;
    private const int AccentHeight = 6;

    public static void Draw(SpriteBatch batch, Rectangle viewport, Fonts fonts, string title, string? subtitle, Menu menu)
    {
        batch.FillRectangle(viewport, Palette.Dim);

        var titleHeight = (int)fonts.Heading.LineHeight;
        var subtitleHeight = subtitle is null ? 0 : (int)fonts.Body.LineHeight + 8;
        var menuHeight = menu.Height(fonts.Body);
        // Menu rows carry their own padding, so the bottom needs less than the top.
        var height = Padding + titleHeight + subtitleHeight + 24 + menuHeight + Padding / 2;
        var width = Math.Min(viewport.Width - 2 * Padding, 560);
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);

        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, AccentHeight), Palette.Accent);

        var y = panel.Y + Padding;
        batch.DrawText(fonts.Heading, title, new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += titleHeight;
        if (subtitle is not null)
        {
            batch.DrawText(fonts.Body, subtitle, new Vector2(panel.Center.X, y + 8), Palette.Muted, Align.Center, shadow: false);
            y += subtitleHeight;
        }

        menu.Draw(batch, fonts.Body, panel.Center.X, y + 24);
    }
}
