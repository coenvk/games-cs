using System.Text.Json;
using Jentl.Net;
using Poker.Online;

namespace Games.Workshop;

/// <summary>
/// The app's link to the poker league on the game server, one for the whole app: the HTTP client, the account kept
/// on this computer, and the runners it started, a child process each (the poker runner next to the app), which keep
/// running while the player does other things and are stopped when the app quits (through <see cref="RunningBots"/>).
/// </summary>
internal sealed class OnlineSession : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, OnlineRunner> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly RunningBots _running;
    private readonly string _file;
    private readonly OnlineAccounts _accounts;

    /// <param name="gameServer">The app's game server (its WebSocket address).</param>
    /// <param name="file">Where the account is kept.</param>
    /// <param name="running">The list that stops the runners when the app quits.</param>
    public OnlineSession(Uri gameServer, string file, RunningBots running)
    {
        Api = OnlineClient.ApiAddress(gameServer);
        RunnerServer = OnlineClient.RunnerAddress(gameServer);
        Client = new OnlineClient(_http, Api);
        _file = file;
        _running = running;
        try
        {
            _accounts = OnlineAccounts.Load(file);
            BrokenCopy = _accounts.BrokenCopy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Saving now could overwrite tokens that are still in the file.
            _accounts = new OnlineAccounts();
            Unreadable = e.Message;
        }
    }

    /// <summary>Where a file of tokens that could not be read was moved aside to, or null.</summary>
    public string? BrokenCopy { get; }

    /// <summary>Why the file of tokens could not be read (nothing is saved then), or null.</summary>
    public string? Unreadable { get; }

    /// <summary>The server's HTTP address.</summary>
    public Uri Api { get; }

    /// <summary>Where the runners connect.</summary>
    public Uri RunnerServer { get; }

    public OnlineClient Client { get; }

    /// <summary>The account on this server (an empty one until an owner is made).</summary>
    public OnlineAccount Account => _accounts.For(Api.ToString());

    /// <summary>Keeps the accounts on disk; false when they could not be written, or the file there could not be read.</summary>
    public bool Save()
    {
        if (Unreadable is not null)
        {
            return false;
        }

        try
        {
            _accounts.Save(_file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The runner started for <paramref name="bot"/>, running or ended, or null when none was.</summary>
    public OnlineRunner? Runner(string bot) => _runners.GetValueOrDefault(bot);

    /// <summary>
    /// Starts <paramref name="command"/> (the poker runner with the bot file) for <paramref name="bot"/> with its token,
    /// in place of a runner started for it before.
    /// </summary>
    public OnlineRunner Start(string bot, AgentCommand command, string token)
    {
        Stop(bot);
        var started = command with { Arguments = [.. command.Arguments, "--token", token, "--server", RunnerServer.ToString()] };
        var runner = new OnlineRunner(AgentProcess.Start(started));
        _runners[bot] = runner;
        runner.Keep(_running.Keep(runner));
        return runner;
    }

    /// <summary>Stops the runner of <paramref name="bot"/>, if there is one.</summary>
    public void Stop(string bot)
    {
        if (_runners.Remove(bot, out var runner))
        {
            runner.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var runner in _runners.Values)
        {
            runner.Dispose();
        }

        _runners.Clear();
        _http.Dispose();
    }
}

/// <summary>
/// A poker runner started by the workshop: a child process whose output is followed, so the workshop can show the last
/// thing it said ("connected", "playing a match against …").
/// </summary>
internal sealed class OnlineRunner : IDisposable
{
    private readonly AgentProcess _process;
    private IDisposable? _kept;
    private volatile string? _status;

    public OnlineRunner(AgentProcess process)
    {
        _process = process;
        _ = Task.Run(FollowAsync);
    }

    /// <summary>The last line the runner wrote, or null before it wrote one.</summary>
    public string? Status => _status;

    public bool IsRunning => !_process.HasExited;

    /// <summary>Why it stopped ("exited with code 3": the server refused its token), or null while it runs or when it was stopped.</summary>
    public string? Problem => _process.Problem;

    /// <summary>What it wrote to its error output last, which tells why it could not start the bot.</summary>
    public string? LastError => _process.ErrorOutput.Count == 0 ? null : _process.ErrorOutput[^1];

    /// <summary>The entry in the app's list of running processes, given up when the runner is disposed.</summary>
    public void Keep(IDisposable kept) => _kept = kept;

    public void Dispose()
    {
        _process.Dispose();
        var kept = Interlocked.Exchange(ref _kept, null);
        kept?.Dispose();
    }

    private async Task FollowAsync()
    {
        // Lines written before it ended are still read; nothing comes once it has ended and they are read.
        while (true)
        {
            if (await _process.ReadAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) is { } line)
            {
                _status = Readable(line);
            }
            else if (_process.HasExited)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A line as the player reads it: the runner writes "12:03:09 connected as Alpha", and passes the server's status
    /// on as it came, JSON with a "text" to show, which is shown in its place.
    /// </summary>
    private static string Readable(string line)
    {
        var json = line.IndexOf('{', StringComparison.Ordinal);
        if (json < 0)
        {
            return line;
        }

        try
        {
            using var status = JsonDocument.Parse(line[json..]);
            return status.RootElement.ValueKind == JsonValueKind.Object && status.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                ? line[..json] + text.GetString()
                : line;
        }
        catch (JsonException)
        {
            return line;
        }
    }
}
