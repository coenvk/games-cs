namespace Games;

/// <summary>This executable is the word games: Connect Word and Associate Words.</summary>
internal static class Entry
{
    public static IGameApp Create() => new SuiteApp("Word games", "Words", [new ConnectWordApp(), new AssociateWordsApp()]);
}
