namespace Games;

/// <summary>
/// This executable is Cheese Farm. Kept apart from the game, so that a suite (an app holding several games) can leave it
/// out and bring its own.
/// </summary>
internal static class Entry
{
    public static IGameApp Create() => new CheeseFarmApp();
}
