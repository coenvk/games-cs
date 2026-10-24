using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Mossbrook.</summary>
internal static class Entry
{
    public static IGameApp Create() => new MossbrookApp();
}

/// <summary>Mossbrook, a small turn-based adventure on the Jentl RPG kit: a village, a cellar and a lost amulet.</summary>
internal sealed class MossbrookApp : IGameApp
{
    public string Title => "Mossbrook";

    public string SaveName => "Mossbrook";

    public void Load(GameContext context, GraphicsDevice device) => context.Add(new AdventureSaves(context.Store));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(context.Adventures.Exists ? T("Continue") : T("Play"), () =>
            {
                if (context.Adventures.Exists)
                {
                    title.Open(new AdventureMenuScene(context), fade: false);
                }
                else
                {
                    title.Open(AdventureScene.New(context));
                }
            }),
        ],
        () => T("A village, a cellar and a lost amulet"));
}

/// <summary>Mossbrook's part of the context.</summary>
internal static class MossbrookContext
{
    extension(GameContext context)
    {
        public AdventureSaves Adventures => context.Get<AdventureSaves>();
    }
}
