using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Card Table.</summary>
internal static class Entry
{
    public static IGameApp Create() => new CardTableApp();
}

/// <summary>Uno and Texas Hold'em at one table: against bots, passing the device round, or online (Uno).</summary>
internal sealed class CardTableApp : IGameApp
{
    public string Title => "Card Table";

    public string SaveName => "CardTable";

    public void Load(GameContext context, GraphicsDevice device) => CardTableArt.Load(context, device);

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new("Uno", () => title.Open(new UnoSetupScene(context), fade: false)),
            new("Texas Hold'em", () => title.Open(new PokerSetupScene(context), fade: false)),
        ],
        () => T("Uno and Texas Hold'em, against bots or friends"));
}
