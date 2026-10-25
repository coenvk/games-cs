using Jentl.Net;
using PokerBots;
using Protocol = PokerBots.Protocol;

namespace Poker.Arena;

/// <summary>
/// A seat for a bot in a process of its own, talking the JSON-lines protocol on stdin and stdout. Lines the bot prints
/// that are not protocol messages (debug output) are skipped, not punished.
/// </summary>
public sealed class ProcessSeat(AgentCommand command, string name) : IArenaSeat
{
    // How long one read waits before the cancellation token is looked at again.
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(25);

    private readonly AgentProcess _process = AgentProcess.Start(command);
    private string? _problem;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string? Problem => _problem ?? (_process.HasExited ? _process.Problem ?? "exited" : null);

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
                _problem ??= _process.HasExited ? _process.Problem ?? "exited" : "did not say hello in time";
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

    /// <inheritdoc />
    public void Notify(Message message) => _process.Send(Protocol.Write(message));

    /// <inheritdoc />
    public async Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(decision);
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
                    return null;
                }
            }
            else if (Protocol.Read(line) is BotAction action && (action.Id is null || action.Id == decision.Id))
            {
                return action;
            }

            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            line = await _process.ReadAsync(Slice).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}
