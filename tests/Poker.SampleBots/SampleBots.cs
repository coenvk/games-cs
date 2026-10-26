using PokerBots;

namespace Poker.SampleBots;

/// <summary>Checks when that is free and calls otherwise: always a legal answer (a call with nothing to call would count as a check).</summary>
public sealed class GoodBot : IPokerBot
{
    /// <inheritdoc />
    public string Name => "GoodBot";

    /// <inheritdoc />
    public BotAction Decide(Decision d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Legal.Check ? BotAction.Check : BotAction.Call;
    }
}

/// <summary>Takes five seconds to call.</summary>
public sealed class SlowBot : IPokerBot
{
    /// <inheritdoc />
    public string Name => "SlowBot";

    /// <inheritdoc />
    public BotAction Decide(Decision d)
    {
        Thread.Sleep(TimeSpan.FromSeconds(5));
        return BotAction.Call;
    }
}

/// <summary>Throws on every decision.</summary>
public sealed class ThrowingBot : IPokerBot
{
    /// <inheritdoc />
    public string Name => "ThrowingBot";

    /// <inheritdoc />
    public BotAction Decide(Decision d) => throw new InvalidOperationException("boom");
}

/// <summary>Ends its whole process on the first decision.</summary>
public sealed class ExitingBot : IPokerBot
{
    /// <inheritdoc />
    public string Name => "ExitingBot";

    /// <inheritdoc />
    public BotAction Decide(Decision d)
    {
        Environment.Exit(4);
        return BotAction.Call;
    }
}

/// <summary>Prints half a line to the console before it calls.</summary>
public sealed class ChattyBot : IPokerBot
{
    /// <inheritdoc />
    public string Name => "ChattyBot";

    /// <inheritdoc />
    public BotAction Decide(Decision d)
    {
        Console.Write("thinking ");
        return BotAction.Call;
    }
}
