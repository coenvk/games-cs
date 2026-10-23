using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

internal readonly record struct PanelLine(string Text, Color Color);

/// <summary>Dims the scene below and draws a centred panel with a title, some lines of text and a menu.</summary>
internal static class OverlayPanel
{
    private const int Padding = 40;
    private const int AccentHeight = 6;
    private const int LineGap = 4;

    public static void Draw(
        SpriteBatch batch,
        Rectangle viewport,
        Fonts fonts,
        string title,
        IReadOnlyList<PanelLine> lines,
        Menu menu)
    {
        batch.FillRectangle(viewport, Palette.Dim);

        var titleHeight = (int)fonts.Heading.LineHeight;
        var lineHeight = (int)fonts.Body.LineHeight + LineGap;
        var menuHeight = menu.Height(fonts.Body);

        // Menu rows carry their own padding, so the bottom needs less than the top.
        var height = Padding + titleHeight + 8 + lines.Count * lineHeight + 24 + menuHeight + Padding / 2;
        var width = Math.Min(viewport.Width - 2 * Padding, 600);
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);

        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, AccentHeight), Palette.Accent);

        var y = panel.Y + Padding;
        batch.DrawText(fonts.Heading, title, new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += titleHeight + 8;
        foreach (var line in lines)
        {
            batch.DrawText(fonts.Body, line.Text, new Vector2(panel.Center.X, y), line.Color, Align.Center, shadow: false);
            y += lineHeight;
        }

        menu.Draw(batch, fonts.Body, panel.Center.X, y + 24);
    }
}
