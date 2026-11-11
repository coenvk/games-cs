using Jentl.Collect;
using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Hot Pan.</summary>
internal static class Entry
{
    public static IGameApp Create() => new HotPanApp();
}

/// <summary>Hot Pan: keep your feet off the hot pan by jumping on chicken that's still cool, for as long as you can.</summary>
internal sealed class HotPanApp : IGameApp
{
    public string Title => "Hot Pan";

    public string SaveName => "HotPan";

    public void Load(GameContext context, GraphicsDevice device) =>
        context.Add(new ScoreBoard(context, "hotpan", ScoreOrder.HigherIsBetter, score => score.ToString("N0", Culture)));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new HotPanScene(context))),
            new(T("Leaderboard"), () => title.Open(new LeaderboardScene(context))),
        ],
        () => T("Keep your feet off the pan"));
}
