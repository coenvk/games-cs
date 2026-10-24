using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Code Lock.</summary>
internal static class Entry
{
    public static IGameApp Create() => new CodeLockApp();
}

/// <summary>Mastermind on a combination lock: find the code from the lamps each try lights.</summary>
internal sealed class CodeLockApp : IGameApp
{
    public string Title => "Code Lock";

    public string SaveName => "CodeLock";

    public void Load(GameContext context, GraphicsDevice device) => context.Add(new CodeLockStore(context.Store));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title => [new(T("Play"), () => title.Open(new CodeLockLevelsScene(context), fade: false))],
        () => T("Crack the code in ten tries."));
}

/// <summary>Code Lock's part of the context.</summary>
internal static class CodeLockContext
{
    extension(GameContext context)
    {
        public CodeLockStore Locks => context.Get<CodeLockStore>();
    }
}
