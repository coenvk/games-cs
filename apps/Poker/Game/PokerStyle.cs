using Jentl.Core.UI;
using Microsoft.Xna.Framework;

namespace Games;

/// <summary>Poker's colours: a card room, dark green and black with cream text and gold, around the table's felt.</summary>
internal static class PokerStyle
{
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
    }
}
