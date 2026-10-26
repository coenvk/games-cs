namespace Poker.Online;

/// <summary>
/// The poker server's limit on what a runner sends, which the runner keeps to itself so that a bot that prints too
/// much loses lines, not its connection (see docs/poker-online.md).
/// </summary>
public static class OnlineLimits
{
    /// <summary>
    /// The longest message a runner may send, in bytes: one line of its bot's, in the relay's JSON. A bot's own messages
    /// are short (an action is under 100 bytes, a hello with a long name a few hundred), so 4 KiB leaves room to spare,
    /// while lines nobody asked for cannot make the server hold much. The server closes a connection that sends a longer
    /// message; the runner drops such a line instead of sending it.
    /// </summary>
    public const int MaxMessage = 4096;
}
