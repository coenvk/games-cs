namespace Games;

/// <summary>This executable is the arcade games: Bricks and Hot Pan.</summary>
internal static class Entry
{
    public static IGameApp Create() => new SuiteApp("Arcade", "Arcade", [new BricksApp(), new HotPanApp()]);
}
