namespace Poker.Online;

/// <summary>
/// A screen's requests to the server, one at a time: the work runs off the UI thread and gives back what to do with its
/// answer, which <see cref="Finish"/> does on the caller's (UI) thread. Every failure, of the work or of what it gave
/// back, is handed back by <see cref="Finish"/> rather than thrown, so no request can bring the app down.
/// </summary>
public sealed class OnlineRequests
{
    private Task<Action>? _pending;

    /// <summary>What the request under way is doing, to tell the player; null when there is none or it goes unsaid.</summary>
    public string? Waiting { get; private set; }

    /// <summary>Whether a request is under way (or done but not yet finished).</summary>
    public bool IsBusy => _pending is not null;

    /// <summary>Whether a request is done and waits for <see cref="Finish"/>.</summary>
    public bool IsDone => _pending is { IsCompleted: true };

    /// <summary>Starts <paramref name="work"/> off the UI thread, saying <paramref name="waiting"/>; false (and nothing started) while another is under way.</summary>
    public bool Start(string? waiting, Func<Task<Action>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_pending is not null)
        {
            return false;
        }

        Waiting = waiting;
        _pending = Task.Run(work);
        return true;
    }

    /// <summary>
    /// When the request is done, does what it gave back and returns null, or returns what went wrong. Null too while it
    /// is still running, or when there is none.
    /// </summary>
    public Exception? Finish()
    {
        if (_pending is not { IsCompleted: true } done)
        {
            return null;
        }

        _pending = null;
        Waiting = null;
        try
        {
            done.GetAwaiter().GetResult()();
            return null;
        }
#pragma warning disable CA1031 // Whatever went wrong is shown to the player; the app goes on.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return e;
        }
    }
}
