namespace Games;

/// <summary>
/// This executable is Mossbrook. Kept apart from the game, so that a suite (an app holding several games) can leave it
/// out and bring its own.
/// </summary>
internal static class Entry
{
    public static IGameApp Create() => new MossbrookApp();
}
