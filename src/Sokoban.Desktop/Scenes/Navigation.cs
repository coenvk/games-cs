using Jentl.Core.Scenes;
using Sokoban.Desktop.UI;
using Jentl.Core.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Shared transitions and jumps between top-level screens.</summary>
internal static class Navigation
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan WipeDuration = TimeSpan.FromMilliseconds(600);

    public static Transition Fade() => new FadeTransition(FadeDuration, Palette.Background);

    public static Transition Wipe() => new WipeTransition(WipeDuration, Palette.Background);

    public static void ToMainMenu(SceneManager scenes, GameContext context) =>
        scenes.Reset(new MenuScene(context), Fade());

    public static void ToLevelSelect(SceneManager scenes, GameContext context, LevelRef selected) =>
        scenes.Reset([new MenuScene(context), new LevelSelectScene(context, selected)], Fade());
}
