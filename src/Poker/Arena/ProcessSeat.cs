using Jentl.Net;
using PokerBots;
using Protocol = PokerBots.Protocol;

namespace Poker.Arena;

/// <summary>
/// A seat for a bot in a process of its own, talking the JSON-lines protocol on stdin and stdout. Lines the bot prints
/// that are not protocol messages (debug output) are skipped, not punished. A bot that leaves <see cref="MaxMissed"/>
/// decisions in a row unanswered is stopped, and gone from then on; an answer in time starts the count again.
/// </summary>
public sealed class ProcessSeat(AgentCommand command, string name) : IArenaSeat
{
    /// <summary>How many decisions in a row a bot may leave unanswered; the last of them stops it.</summary>
    public const int MaxMissed = 3;

    // How long one read waits before the cancellation token is looked at again.
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(25);

    private readonly AgentProcess _process = AgentProcess.Start(command);
    private readonly SemaphoreSlim _asking = new(1, 1);
    private string? _problem;
    private int _missed;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string? Problem => _problem ?? (_process.HasExited ? ExitProblem() : null);

    /// <inheritdoc />
    public bool IsGone => Problem is not null;

    /// <inheritdoc />
    public async Task StartAsync(MatchStart start, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(start);
        _process.Send(Protocol.Write(new Hello(Protocol.Version)));
        var deadline = DateTime.UtcNow + limit;
        while (true)
        {
            var line = await _process.ReadAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (line is null)
            {
                _problem ??= _process.HasExited ? ExitProblem() : "did not say hello in time";
                _process.Stop();
                return;
            }

            if (Protocol.Read(line) is not Hello hello)
            {
                continue;
            }

            if (hello.Protocol != Protocol.Version)
            {
                _problem = $"speaks protocol {hello.Protocol}, the app speaks {Protocol.Version}";
                _process.Stop();
                return;
            }

            Notify(start);
            return;
        }
    }

    // Why the process ended, with the last thing it said on stderr (the bot host explains itself there).
    private string ExitProblem()
    {
        var problem = _process.Problem ?? "exited";
        var last = _process.ErrorOutput.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return last is null ? problem : $"{problem}: {(last.Length > 200 ? last[..200] + "..." : last)}";
    }

    /// <inheritdoc />
    public void Notify(Message message) => _process.Send(Protocol.Write(message));

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

        if (cancel.IsCancellationRequested)
        {
            return null;
        }

        var line = await _process.AskAsync(Protocol.Write(decision), Slice).ConfigureAwait(false);
        while (true)
        {
            if (line is null)
            {
                if (_process.HasExited)
                {
                    throw new IOException(Problem);
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

            line = await _process.ReadAsync(Slice).ConfigureAwait(false);
        }
    }

    // The decision went unanswered: a bot that has now missed MaxMissed in a row hangs, and is stopped.
    private BotAction? Missed()
    {
        if (++_missed >= MaxMissed)
        {
            _problem ??= $"stopped: did not answer {MaxMissed} times in a row";
            _process.Stop();
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}
