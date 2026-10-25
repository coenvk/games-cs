using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Scenes;
using Games.Workshop;
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
        {
            List<MenuItem> items =
            [
                new("Uno", () => title.Open(new UnoSetupScene(context), fade: false)),
                new("Texas Hold'em", () => title.Open(new PokerSetupScene(context), fade: false)),
            ];

            // Running the player's programs needs a desktop; the browser has no workshop.
            if (context.Platform.CanRunBots)
            {
                items.Add(new(T("Bot workshop"), () => title.Open(new WorkshopScene(new WorkshopContext(context)), fade: false)));
            }

            return items;
        },
        () => T("Uno and Texas Hold'em, against bots or friends"));
}
