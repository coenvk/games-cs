using PokerBots;

namespace Poker.Arena;

/// <summary>A seat for a bot that runs in this process. A bot that throws from <c>Decide</c> is gone for the rest of the match.</summary>
/// <param name="bot">The bot.</param>
/// <param name="name">The name at the table; the bot's own when null.</param>
public sealed class HouseSeat(IPokerBot bot, string? name = null) : IArenaSeat
{
    private volatile string? _problem;

    /// <inheritdoc />
    public string Name => name ?? bot.Name;

    /// <inheritdoc />
    public string? Problem => _problem;

    /// <inheritdoc />
    public bool IsGone => _problem is not null;

    /// <inheritdoc />
    public Task StartAsync(MatchStart start, TimeSpan limit)
    {
        Notify(start);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Notify(Message message)
    {
        try
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
#pragma warning disable CA1031 // A bot's hook may throw anything; it must not stop the match.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The hooks are optional extras: a failing one costs the bot nothing else.
        }
    }

    /// <inheritdoc />
    public Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel)
    {
        if (_problem is not null)
        {
            return Task.FromException<BotAction?>(new IOException(_problem));
        }

        try
        {
            return Task.FromResult<BotAction?>(bot.Decide(decision));
        }
#pragma warning disable CA1031 // A bot may throw anything; it is then out of the match.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _problem = $"threw: {e.Message}";
            return Task.FromException<BotAction?>(new IOException(_problem, e));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
