using Jentl.Core.Scenes;
using Games.UI;
using Jentl.Core.UI;

namespace Games.Scenes;

/// <summary>Shared transitions and jumps between top-level screens.</summary>
internal static partial class Navigation
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan WipeDuration = TimeSpan.FromMilliseconds(600);

    public static Transition Fade() => new FadeTransition(FadeDuration, Palette.Background);

    public static Transition Wipe() => new WipeTransition(WipeDuration, Palette.Background);

    public static void ToMainMenu(SceneManager scenes, GameContext context) =>
        scenes.Reset(context.App.MainMenu(context), Fade());
}
