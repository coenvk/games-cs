using PokerBots;

namespace Poker.Arena;

/// <summary>A seat for a bot that runs in this process.</summary>
public sealed class HouseSeat(IPokerBot bot) : IArenaSeat
{
    /// <inheritdoc />
    public string Name => bot.Name;

    /// <inheritdoc />
    public string? Problem => null;

    /// <inheritdoc />
    public bool IsGone => false;

    /// <inheritdoc />
    public Task StartAsync(MatchStart start, TimeSpan limit)
    {
        Notify(start);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Notify(Message message)
    {
        switch (message)
        {
            case MatchStart m:
                bot.OnMatchStart(m);
                break;
            case HandStart h:
                bot.OnHandStart(h);
                break;
            case HandEnd h:
                bot.OnHandEnd(h);
                break;
            case MatchEnd m:
                bot.OnMatchEnd(m);
                break;
        }
    }

    /// <inheritdoc />
    public Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel) =>
        Task.FromResult<BotAction?>(bot.Decide(decision));

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
