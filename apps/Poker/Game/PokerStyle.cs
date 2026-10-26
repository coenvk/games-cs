using Jentl.Core.UI;
using Microsoft.Xna.Framework;

namespace Games;

/// <summary>
/// Poker's look: a card room, dark green and black with cream text and gold around the table's felt; panels with
/// round corners, a thin gold edge and a shadow, and round-ended buttons like the rail of a table.
/// </summary>
internal static class PokerStyle
{
    /// <summary>The gold of a panel's edge: darker than the accent, which marks what is selected or whose turn it is.</summary>
    public static Color Edge { get; } = new(150, 118, 52);

    /// <summary>Restyles the whole UI; call once at start-up, before any scene draws.</summary>
    public static void Apply()
    {
        Palette.Background = new Color(12, 26, 20);
        Palette.Panel = new Color(22, 44, 34);
        Palette.PanelLight = new Color(36, 66, 52);
        Palette.Text = new Color(244, 236, 216);
        Palette.Muted = new Color(158, 178, 160);
        Palette.Accent = new Color(222, 184, 82);
        Palette.Solved = new Color(70, 160, 100);
        Palette.Ink = new Color(18, 22, 16);
        Palette.Dim = new Color(4, 10, 7, 180);
        Theme.Current = new Theme
        {
            Panel = new SurfaceStyle(Radius: 16, Border: 2, BorderColor: Edge, Shadow: 5),
            Control = new SurfaceStyle(Radius: 22, Shadow: 2),
            AccentBar = false,
        };
    }
}
