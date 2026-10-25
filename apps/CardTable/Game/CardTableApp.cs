using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Card Table.</summary>
internal static class Entry
{
    public static IGameApp Create() => new CardTableApp();
}

/// <summary>Uno at one table: against bots, passing the device round, or online.</summary>
internal sealed class CardTableApp : IGameApp
{
    public string Title => "Card Table";

    public string SaveName => "CardTable";

    public void Load(GameContext context, GraphicsDevice device) => CardTableArt.Load(context, device);

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title => [new("Uno", () => title.Open(new UnoSetupScene(context), fade: false))],
        () => T("Uno against bots or friends, here or online"));
}
