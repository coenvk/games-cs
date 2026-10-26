using System.Net.WebSockets;
using Jentl.Net;
using Poker.Arena;
using Poker.Online;

namespace PokerRunner;

/// <summary>
/// What the runner needs to know before it connects: which command to run for a bot, and where the server is; and the
/// relay that puts the bot online within the server's limits.
/// </summary>
public static class RunnerCommand
{
    /// <summary>Where the runner connects when nothing says otherwise.</summary>
    public const string DefaultServer = "ws://localhost:5080/bots";

    /// <summary>
    /// The runner's relay for the bot that <paramref name="command"/> runs: it keeps to the poker server's limits, dropping
    /// a line longer than <see cref="OnlineLimits.MaxMessage"/> and lines beyond what the server takes besides the answers
    /// (see <see cref="AgentRelay.ExtraPerSecond"/>), so a bot that prints too much loses those lines, not its connection.
    /// </summary>
    public static AgentRelay Relay(Func<CancellationToken, Task<WebSocket>> connect, string token, AgentCommand command) =>
        new(connect, token, command) { MaxLength = OnlineLimits.MaxMessage };

    /// <summary>
    /// The command that runs the bot at <paramref name="path"/>: a <c>*.bot.json</c> as it describes itself, or a DLL
    /// through the bot host at <paramref name="hostDll"/> (naming <paramref name="bot"/> when the DLL has more than one
    /// bot). Null, with <paramref name="problem"/> saying what is wrong, when the path is no bot, the description cannot
    /// be read or the bot host is missing.
    /// </summary>
    public static AgentCommand? Resolve(string path, string hostDll, out string? problem, string? bot = null)
    {
        problem = null;
        var full = Path.GetFullPath(path);
        var isDll = full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        if (!isDll && !full.EndsWith(".bot.json", StringComparison.OrdinalIgnoreCase))
        {
            problem = $"{path} is not a bot: give a *.bot.json file or a C# bot DLL";
            return null;
        }

        if (!File.Exists(full))
        {
            problem = $"{path} does not exist";
            return null;
        }

        if (isDll && !File.Exists(hostDll))
        {
            problem = $"{path} needs the bot host, which is not here: {hostDll}";
            return null;
        }

        var host = AgentCommand.DotNet(hostDll);
        var entry = BotCatalog.Scan(Path.GetDirectoryName(full)!, host).FirstOrDefault(e => string.Equals(e.Source, full, StringComparison.Ordinal) || e.Source.StartsWith(full + ": ", StringComparison.Ordinal));
        if (entry is null)
        {
            problem = $"{path} is not a bot the runner can run (the bot SDK itself, Poker.Bots.dll, is not a bot)";
            return null;
        }

        if (entry.Command is null)
        {
            problem = entry.Source;
            return null;
        }

        // The host takes the bot's type name after the DLL.
        return entry.Kind == BotKind.CSharp && bot is not null
            ? entry.Command with { Arguments = [.. entry.Command.Arguments, bot] }
            : entry.Command;
    }

    /// <summary>
    /// The server to connect to: <paramref name="option"/> as it is, else the game server of <paramref name="environment"/>
    /// (<c>JENTL_SERVER</c>, <c>ws://host:port/play</c>) with <c>/bots</c> in place of its path, else <see cref="DefaultServer"/>.
    /// </summary>
    public static Uri Server(string? option, string? environment)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return new Uri(option);
        }

        if (!string.IsNullOrWhiteSpace(environment) && Uri.TryCreate(environment, UriKind.Absolute, out var server))
        {
            return new UriBuilder(server) { Path = "/bots", Query = "" }.Uri;
        }

        return new Uri(DefaultServer);
    }
}
