using System.Net.WebSockets;
using Jentl.Net;

namespace PokerRunner;

/// <summary>
/// Usage: <c>PokerRunner &lt;bot.json|bot.dll&gt; --token &lt;token&gt; [--server wss://host/bots] [--bot TypeName]</c>.
/// Exit codes: 0 stopped with Ctrl+C, 1 wrong usage, 2 the bot path is no bot, 3 the server refused the token.
/// </summary>
public static class Program
{
    /// <summary>Runs the bot online until Ctrl+C or until the server refuses it.</summary>
    public static async Task<int> Main(string[] args)
    {
        string? path = null, token = null, server = null, bot = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--token" when i + 1 < args.Length:
                    token = args[++i];
                    break;
                case "--server" when i + 1 < args.Length:
                    server = args[++i];
                    break;
                case "--bot" when i + 1 < args.Length:
                    bot = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal) || path is not null)
                    {
                        return Usage($"unexpected '{args[i]}'");
                    }

                    path = args[i];
                    break;
            }
        }

        if (path is null || string.IsNullOrEmpty(token))
        {
            return Usage(path is null ? "no bot given" : "no --token given");
        }

        var command = RunnerCommand.Resolve(path, Path.Combine(AppContext.BaseDirectory, "PokerBotHost.dll"), out var problem, bot);
        if (command is null)
        {
            await Console.Error.WriteLineAsync(problem);
            return 2;
        }

        Uri url;
        try
        {
            url = RunnerCommand.Server(server, Environment.GetEnvironmentVariable("JENTL_SERVER"));
        }
        catch (UriFormatException)
        {
            return Usage($"'{server}' is not a server address");
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        var refused = false;
        using var relay = new AgentRelay(Connect(url), token, command);
        relay.Status += status =>
        {
            refused |= status.StartsWith("refused", StringComparison.Ordinal);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {status}");
        };
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} connecting to {url}; Ctrl+C stops the bot");
        await relay.RunAsync(stop.Token);
        return stop.IsCancellationRequested ? 0 : refused ? 3 : 0;
    }

    private static Func<CancellationToken, Task<WebSocket>> Connect(Uri url) => async cancel =>
    {
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(url, cancel);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    };

    private static int Usage(string problem)
    {
        Console.Error.WriteLine($"{problem}\nusage: PokerRunner <bot.json|bot.dll> --token <token> [--server wss://host/bots] [--bot TypeName]");
        return 1;
    }
}
