namespace Games;

/// <summary>This executable is the puzzles: Sokoban and Code Lock.</summary>
internal static class Entry
{
    public static IGameApp Create() => new SuiteApp("Puzzles", "Puzzles", [new SokobanApp(), new CodeLockApp()]);
}
