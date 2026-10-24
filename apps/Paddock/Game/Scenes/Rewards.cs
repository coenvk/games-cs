using Jentl.Core.Scenes;
using Paddock;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>After a match: what it paid, and any cars won (shown as cards).</summary>
internal static class Rewards
{
    public static void Show(SceneManager scenes, GameContext context, bool won, long cash, int xp, IReadOnlyList<CarInstance> cars)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.Paddock.Profile;
        var lines = new List<string>
        {
            T("+{0} cash, +{1} XP", cash.ToString("#,0", Culture), xp),
            T("Level {0} · hand RQ limit {1}", profile.Level, Progression.RqLimit(profile.Level)),
        };
        scenes.Push(new MessageScene(context, won ? T("Well raced!") : T("Better luck next time"), [.. lines], []));
        if (cars.Count > 0)
        {
            scenes.Push(new NewCarsScene(context, cars));
        }
    }
}
