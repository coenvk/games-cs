using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Jentl.Core.UI;
using Games.Scenes;
using Games.Workshop;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Poker.</summary>
internal static class Entry
{
    public static IGameApp Create() => new PokerApp();
}

/// <summary>
/// No-Limit Texas Hold'em: against bots or passing the device round, and on the desktop the bot workshop, where bots
/// people wrote play each other.
/// </summary>
internal sealed class PokerApp : IGameApp
{
    public string Title => "Poker";

    public string SaveName => "Poker";

    public void Load(GameContext context, GraphicsDevice device)
    {
        PokerStyle.Apply();
        CardTableArt.Load(context, device);

        // Hold'em and the workshop used to be part of Card Table: the player's bots and matches come along once.
        if (context.Store is SaveStore store && store.Directory == SaveStore.ForApp(SaveName).Directory)
        {
            store.MoveFolders(SaveStore.ForApp("CardTable"), "bots", "workshop");
        }
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        {
            List<MenuItem> items = [new("Texas Hold'em", () => title.Open(new PokerSetupScene(context), fade: false))];

            // Running the player's programs needs a desktop; the browser has no workshop.
            if (context.Platform.CanRunBots)
            {
                items.Add(new(T("Bot workshop"), () => title.Open(new WorkshopScene(new WorkshopContext(context)), fade: false)));
            }

            return items;
        },
        () => T("No-limit Texas Hold'em against bots or friends"));
}
