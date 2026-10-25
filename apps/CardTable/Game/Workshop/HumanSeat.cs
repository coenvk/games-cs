using Poker.Arena;
using PokerBots;

namespace Games.Workshop;

/// <summary>
/// The person's own seat at a workshop table. A decision waits, with no time limit, until the person acts with the
/// table's controls (<see cref="Act"/>); leaving the table (disposing the seat) ends the wait as a seat that is gone.
/// </summary>
internal sealed class HumanSeat(string name) : IArenaSeat
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<BotAction?>? _asked;
    private bool _left;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string? Problem
    {
        get
        {
            lock (_lock)
            {
                return _left ? "left the table" : null;
            }
        }
    }

    /// <inheritdoc />
    public bool IsGone => Problem is not null;

    /// <inheritdoc />
    public bool HasTimeLimit => false;

    /// <summary>The seat the person has, once the match has started; -1 before.</summary>
    public int Seat { get; private set; } = -1;

    /// <summary>Whether the table is waiting for the person to act.</summary>
    public bool IsAsked
    {
        get
        {
            lock (_lock)
            {
                return _asked is not null;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(MatchStart start, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(start);
        Seat = start.Seat;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Notify(Message message)
    {
        // The person sees the table itself.
    }

    /// <inheritdoc />
    public Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel)
    {
        TaskCompletionSource<BotAction?> asked;
        lock (_lock)
        {
            if (_left)
            {
                return Task.FromException<BotAction?>(new IOException("left the table"));
            }

            // The answer is played on by the table off the UI thread, not inside the person's button press.
            asked = _asked = new TaskCompletionSource<BotAction?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var registration = cancel.Register(() => Answer(asked, null));
        asked.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return asked.Task;
    }

    /// <summary>Answers the decision the table is waiting for; does nothing when it is not waiting.</summary>
    public void Act(BotAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TaskCompletionSource<BotAction?>? asked;
        lock (_lock)
        {
            asked = _asked;
        }

        if (asked is not null)
        {
            Answer(asked, action);
        }
    }

    /// <summary>Leaves the table: a decision being waited for ends, and the seat is gone.</summary>
    public void Dispose()
    {
        TaskCompletionSource<BotAction?>? asked;
        lock (_lock)
        {
            _left = true;
            asked = _asked;
            _asked = null;
        }

        asked?.TrySetException(new IOException("left the table"));
    }

    private void Answer(TaskCompletionSource<BotAction?> asked, BotAction? action)
    {
        lock (_lock)
        {
            if (_asked == asked)
            {
                _asked = null;
            }
        }

        asked.TrySetResult(action);
    }
}
