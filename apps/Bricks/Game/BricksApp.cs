using Jentl.Collect;
using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Bricks.</summary>
internal static class Entry
{
    public static IGameApp Create() => new BricksApp();
}

/// <summary>A brick breaker on the Jentl entity component system and physics.</summary>
internal sealed class BricksApp : IGameApp
{
    public string Title => "Bricks";

    public string SaveName => "Bricks";

    public void Load(GameContext context, GraphicsDevice device) =>
        context.Add(new ScoreBoard(context, "bricks", ScoreOrder.HigherIsBetter, score => score.ToString("N0", Culture)));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new BricksScene(context))),
            new(T("Leaderboard"), () => title.Open(new LeaderboardScene(context))),
        ],
        () => T("Break every brick"));
}
