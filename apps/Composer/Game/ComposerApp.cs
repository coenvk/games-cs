using Composer;
using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is the Composer.</summary>
internal static class Entry
{
    public static IGameApp Create() => new ComposerApp();
}

/// <summary>The Composer: songs written on a piano roll and an arrangement, for the instruments of Jentl's synthesizer.</summary>
internal sealed class ComposerApp : IGameApp
{
    public string Title => "Composer";

    public string SaveName => "Composer";

    public void Load(GameContext context, GraphicsDevice device) => context.Add(new SongShelf(context.Store));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("New song"), () => title.Open(new ComposerScene(context, new ComposerSession(ComposerSession.Blank())))),
            new(T("Open a song"), () => title.Open(new SongListScene(context))),
            new(T("Start from the example"), () => title.Open(new ComposerScene(context, new ComposerSession(ComposerSession.Example())))),
        ],
        () => T("Write music for harp, strings, flutes and drums"));
}
