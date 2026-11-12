using Jentl.Core.Scenes;
using Games.Input;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>Mastermind on a combination lock: find the code from the lamps each try lights.</summary>
internal sealed class CodeLockApp : IGameApp
{
    public string Title => "Code Lock";

    public string SaveName => "CodeLock";

    public IReadOnlyList<GameAction> ControlActions => [GameAction.Up, GameAction.Down, GameAction.Left, GameAction.Right, GameAction.Undo, GameAction.Hint, GameAction.Confirm];

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
