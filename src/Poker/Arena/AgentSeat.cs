using Jentl.Net;
using PokerBots;
using Protocol = PokerBots.Protocol;

namespace Poker.Arena;

/// <summary>
/// A seat for any agent, a bot in a process of its own or one on the other end of a connection, talking the JSON-lines
/// protocol. Lines the bot prints that are not protocol messages (debug output) are skipped, not punished. A bot that
/// leaves <see cref="MaxMissed"/> decisions in a row unanswered is stopped, and gone from then on; an answer in time
/// starts the count again. An agent that is <see cref="IsAway"/> (a runner that lost its connection) is folded without
/// being asked and without the count going up; it takes the seat back from the next decision once it is back.
/// </summary>
public sealed class AgentSeat(IAgent agent, string name) : IArenaSeat
{
    /// <summary>How many decisions in a row a bot may leave unanswered; the last of them stops it.</summary>
    public const int MaxMissed = 3;

    // How long one read waits before the cancellation token is looked at again.
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(25);

    private readonly SemaphoreSlim _asking = new(1, 1);
    private string? _problem;
    private int _missed;

    /// <inheritdoc />
    public string Name => name;

    /// <summary>True while the agent's connection is down: its decisions are folded, and it may come back.</summary>
    public bool IsAway => agent.IsAway;

    /// <inheritdoc />
    public string? Problem => _problem ?? (agent.HasExited ? ExitProblem() : null);

    /// <inheritdoc />
    public bool IsGone => Problem is not null;

    /// <inheritdoc />
    public async Task StartAsync(MatchStart start, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(start);
        var deadline = DateTime.UtcNow + limit;

        // An agent that is away may come back within the limit; it is greeted once it has.
        await WaitUntilBackAsync(deadline).ConfigureAwait(false);
        agent.Send(Protocol.Write(new Hello(Protocol.Version)));
        while (true)
        {
            var line = await agent.ReadAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (line is null)
            {
                if (agent.IsAway && DateTime.UtcNow < deadline)
                {
                    await WaitUntilBackAsync(deadline).ConfigureAwait(false);
                    agent.Send(Protocol.Write(new Hello(Protocol.Version)));
                    continue;
                }

                _problem ??= agent.HasExited ? ExitProblem() : "did not say hello in time";
                agent.Stop();
                return;
            }

            if (Protocol.Read(line) is not Hello hello)
            {
                continue;
            }

            if (hello.Protocol != Protocol.Version)
            {
                _problem = $"speaks protocol {hello.Protocol}, the app speaks {Protocol.Version}";
                agent.Stop();
                return;
            }

            Notify(start);
            return;
        }
    }

    private async Task WaitUntilBackAsync(DateTime deadline)
    {
        while (agent.IsAway && DateTime.UtcNow < deadline)
        {
            await Task.Delay(Slice).ConfigureAwait(false);
        }
    }

    // Why the process ended, with the last thing it said on stderr (the bot host explains itself there).
    private string ExitProblem()
    {
        var problem = agent.Problem ?? "exited";
        var last = agent.ErrorOutput.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return last is null ? problem : $"{problem}: {(last.Length > 200 ? last[..200] + "..." : last)}";
    }

    /// <inheritdoc />
    public void Notify(Message message) => agent.Send(Protocol.Write(message));

    /// <inheritdoc />
    public async Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(decision);

        // One ask at a time: an abandoned ask (its token cancelled) ends within a slice, and must not take the next answer.
        await _asking.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return await AskOnceAsync(decision, cancel).ConfigureAwait(false);
        }
        finally
        {
            _asking.Release();
        }
    }

    private async Task<BotAction?> AskOnceAsync(Decision decision, CancellationToken cancel)
    {
        if (IsGone)
        {
            throw new IOException(Problem);
        }

        if (cancel.IsCancellationRequested || agent.IsAway)
        {
            return null;
        }

        var line = await agent.AskAsync(Protocol.Write(decision), Slice).ConfigureAwait(false);
        while (true)
        {
            if (line is null)
            {
                if (agent.HasExited)
                {
                    throw new IOException(Problem);
                }

                if (agent.IsAway)
                {
                    return null;
                }

                if (cancel.IsCancellationRequested)
                {
                    return Missed();
                }
            }
            else if (Protocol.Read(line) is BotAction action && (action.Id is null || action.Id == decision.Id))
            {
                _missed = 0;
                return action;
            }

            if (cancel.IsCancellationRequested)
            {
                return Missed();
            }

            line = await agent.ReadAsync(Slice).ConfigureAwait(false);
        }
    }

    // The decision went unanswered: a bot that has now missed MaxMissed in a row hangs, and is stopped. Not hearing from
    // an agent that is away is no fault of the bot, so that does not count.
    private BotAction? Missed()
    {
        if (agent.IsAway)
        {
            return null;
        }

        if (++_missed >= MaxMissed)
        {
            _problem ??= $"stopped: did not answer {MaxMissed} times in a row";
            agent.Stop();
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose() => agent.Dispose();
}
