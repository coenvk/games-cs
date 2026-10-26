using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Net;
using Poker.Online;

namespace Games.Workshop;

/// <summary>
/// What this computer keeps of the player's place in a poker league: the owner token and each bot's token and bot file,
/// per server (the HTTP address). Kept in <c>workshop/online.json</c> in the save folder, never in the settings and
/// never shown, since a token is all it takes to act as the owner.
/// </summary>
internal sealed class OnlineAccounts
{
    public Dictionary<string, OnlineAccount> Servers { get; init; } = [];

    /// <summary>The accounts in <paramref name="path"/>; none when the file is not there or cannot be read.</summary>
    public static OnlineAccounts Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), OnlineAccountsJson.Default.OnlineAccounts) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>
    /// Writes the accounts to <paramref name="path"/>: to a file beside it first, readable by this user only where the
    /// system has such modes, which then takes the old one's place, so a crash leaves the old file whole.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be written to.</exception>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".new";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(temporary, System.Text.Encoding.UTF8, options))
        {
            writer.Write(JsonSerializer.Serialize(this, OnlineAccountsJson.Default.OnlineAccounts));
        }

        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// The player's owner token on one server, and the tokens and bot files of the bots registered there, by the names the
/// server gives the bots.
/// </summary>
internal sealed class OnlineAccount
{
    public string? OwnerToken { get; set; }

    public Dictionary<string, string> BotTokens { get; init; } = [];

    /// <summary>The file each bot is run from when it goes online (a <c>*.bot.json</c> or a DLL in the bots folder).</summary>
    public Dictionary<string, string> BotFiles { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(OnlineAccounts))]
internal sealed partial class OnlineAccountsJson : JsonSerializerContext;

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
        _accounts = OnlineAccounts.Load(file);
    }

    /// <summary>The server's HTTP address.</summary>
    public Uri Api { get; }

    /// <summary>Where the runners connect.</summary>
    public Uri RunnerServer { get; }

    public OnlineClient Client { get; }

    /// <summary>The account on this server (an empty one until an owner is made).</summary>
    public OnlineAccount Account
    {
        get
        {
            var key = Api.ToString();
            if (!_accounts.Servers.TryGetValue(key, out var account))
            {
                account = new OnlineAccount();
                _accounts.Servers[key] = account;
            }

            return account;
        }
    }

    /// <summary>Keeps the accounts on disk; false when they could not be written.</summary>
    public bool Save()
    {
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
