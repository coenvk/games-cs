using Jentl.Core.Scenes;

namespace Games.Scenes;

/// <summary>Sokoban's own jump: back to level select, with a level picked.</summary>
internal static partial class Navigation
{
    public static void ToLevelSelect(SceneManager scenes, GameContext context, LevelRef selected) =>
        scenes.Reset([new MenuScene(context), new LevelSelectScene(context, selected)], Fade());
}
