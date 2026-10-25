namespace PokerBots;

/// <summary>
/// A poker bot. Implement <see cref="Name"/> and <see cref="Decide"/>; the other methods are optional hooks for bots
/// that want to track the match. Run it with <see cref="BotRunner.Run"/>.
/// </summary>
public interface IPokerBot
{
    /// <summary>The name shown at the table.</summary>
    string Name { get; }

    /// <summary>Called once when the match starts, with the seat you were given and the match rules.</summary>
    void OnMatchStart(MatchStart m)
    {
    }

    /// <summary>Called at the start of every hand, with the seats and stacks.</summary>
    void OnHandStart(HandStart h)
    {
    }

    /// <summary>
    /// Called when it is your turn. Return one of the <see cref="Decision.Legal"/> actions within
    /// <see cref="Decision.TimeLimitMs"/> milliseconds, or the app folds for you (or checks, when that is free).
    /// </summary>
    BotAction Decide(Decision d);

    /// <summary>Called at the end of every hand with the board, who showed what and who won.</summary>
    void OnHandEnd(HandEnd h)
    {
    }

    /// <summary>Called once when the match is over, with the final standings.</summary>
    void OnMatchEnd(MatchEnd m)
    {
    }
}
