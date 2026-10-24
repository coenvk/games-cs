using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Solitaire.</summary>
internal static class Entry
{
    public static IGameApp Create() => new SolitaireApp();
}

/// <summary>Nine solitaire games on the Jentl card kit, from Klondike to Easthaven.</summary>
internal sealed class SolitaireApp : IGameApp
{
    public string Title => "Solitaire";

    public string SaveName => "Solitaire";

    public void Load(GameContext context, GraphicsDevice device) => CardTableArt.Load(context, device);

    // Game names stay as they are in every language.
    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new("Klondike", () => title.Open(new KlondikeScene(context))),
            new("Easthaven", () => title.Open(new EasthavenScene(context))),
            new("Forty Thieves", () => title.Open(new FortyThievesScene(context))),
            new("FreeCell", () => title.Open(new FreeCellScene(context))),
            new("Golf", () => title.Open(new GolfScene(context))),
            new("Pyramid", () => title.Open(new PyramidScene(context))),
            new("Spider", () => title.Open(new SpiderSuitsScene(context), fade: false)),
            new("TriPeaks", () => title.Open(new TriPeaksScene(context))),
            new("Yukon", () => title.Open(new YukonScene(context))),
        ],
        () => T("Nine solitaire games"));
}
