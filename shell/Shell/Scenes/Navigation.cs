using Jentl.Core.Scenes;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Shared transitions and jumps between top-level screens.</summary>
internal static partial class Navigation
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan WipeDuration = TimeSpan.FromMilliseconds(600);

    public static Transition Fade() => new FadeTransition(FadeDuration, Palette.Background);

    public static Transition Wipe() => new WipeTransition(WipeDuration, Palette.Background);

    public static void ToMainMenu(SceneManager scenes, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(context);
        scenes.Reset(context.App.MainMenu(context), Fade());
    }

    /// <summary>
    /// A title screen's way out, its last item: inside a suite, back to the suite's games; on its own, Quit where the
    /// platform can quit; otherwise none.
    /// </summary>
    public static MenuItem? Leave(GameContext context, Func<SceneManager> scenes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scenes);
        return context.App is SuiteApp { Current: not null } suite ? new MenuItem(T("All games"), () => suite.Leave(scenes(), context))
            : context.Platform.CanQuit ? new MenuItem(T("Quit"), () => scenes().Pop(Fade()))
            : null;
    }
}
