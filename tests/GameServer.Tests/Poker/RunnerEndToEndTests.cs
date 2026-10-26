using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using GameServer.Poker;
using Jentl.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PokerRunner;

namespace GameServer.Tests.Poker;

/// <summary>
/// The poker runner's job end to end: <see cref="RunnerCommand"/> turns real bots (a Python program and a C# DLL through
/// the bot host) into commands, real <see cref="AgentRelay"/>s put them online as the runner does, and the server plays
/// and ranks them.
/// </summary>
public sealed class RunnerEndToEndTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private static string Host => Path.Combine(AppContext.BaseDirectory, "BotHost", "PokerBotHost.dll");

    private static string SampleDll => Path.Combine(AppContext.BaseDirectory, "SampleBots", "Poker.SampleBots.dll");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-runner-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<AgentRelay> _relays = [];
    private readonly ConcurrentQueue<string> _status = new();
    private WebApplicationFactory<Program> _factory;
    private HttpClient _http;

    public RunnerEndToEndTests() => (_factory, _http) = Start(hands: 10);

    /// <summary>A server whose matches are <paramref name="hands"/> hands long, and a client for it.</summary>
    private (WebApplicationFactory<Program> Factory, HttpClient Http) Start(int hands)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("poker-data", _folder)
            .UseSetting("poker-hands", hands.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("scores", Path.Combine(_folder, "scores.json")));
        return (factory, factory.CreateClient());
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var relay in _relays)
        {
            relay.Dispose();
        }

        _http.Dispose();
        _factory.Dispose();
        _stop.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still closing: the temporary folder stays.
        }
    }

    [Fact]
    public async Task ARealRunner_WithARealBot_PlaysARankedMatch()
    {
        // The Python example is left out when there is no python3 on this machine; a second C# bot takes its place.
        var python = AgentCommand.Find("python3", AgentCommand.SearchFolders()) is not null;
        var first = RunnerCommand.Resolve(SampleDll, Host, out var problem, "GoodBot");
        problem.Should().BeNull();
        var second = python
            ? RunnerCommand.Resolve(Path.Combine(RepositoryRoot(), "bots", "python", "example.bot.json"), Host, out problem)
            : RunnerCommand.Resolve(SampleDll, Host, out problem, "GoodBot");
        problem.Should().BeNull();

        Online(await NewOwnerAndBot("Ann", "Alpha Bot"), first!);
        Online(await NewOwnerAndBot("Bob", "Beta Bot"), second!);

        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var ranking = await _http.GetFromJsonAsync<JsonElement>(new Uri("/api/poker/ranking", UriKind.Relative));
            if (ranking.GetArrayLength() == 2 && ranking.EnumerateArray().All(r => r.GetProperty("matches").GetInt32() == 1))
            {
                break;
            }

            DateTime.UtcNow.Should().BeBefore(deadline, "both bots should have played a ranked match by now");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task ABotThatPlaysLegally_PlaysLegallyOnline()
    {
        var command = RunnerCommand.Resolve(SampleDll, Host, out var problem, "GoodBot");
        problem.Should().BeNull();
        Online(await NewOwnerAndBot("Ann", "Alpha Bot"), command!);
        Online(await NewOwnerAndBot("Bob", "Beta Bot"), command!);

        var store = _factory.Services.GetRequiredService<MatchStore>();
        var deadline = DateTime.UtcNow + Patience;
        while (store.Of("Alpha Bot", 1).Count == 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the bots should have played a ranked match by now");
            await Task.Delay(100);
        }

        // GoodBot checks when that is free and calls otherwise: every decision online is its own, legal, answer in time.
        var match = store.Load(store.Of("Alpha Bot", 1)[0].Id)!;
        match.Hands.Should().HaveCount(10);
        match.Results.Should().AllSatisfy(r =>
        {
            r.Illegal.Should().Be(0, $"{r.Name} answers every decision legally");
            r.Late.Should().Be(0, $"{r.Name} answers at once");
            r.Gone.Should().Be(0);
        });
    }

    [Fact]
    public async Task FastBots_PlayALongMatchAtFullSpeed_WithoutLosingTheirConnection()
    {
        // Two bots that answer at once play a long match in about a second: hundreds of lines a second each way. Neither
        // connection may be cut for that, or the seat is folded while away and its owner sees illegal moves.
        _http.Dispose();
        await _factory.DisposeAsync();
        (_factory, _http) = Start(hands: 200);
        var python = AgentCommand.Find("python3", AgentCommand.SearchFolders()) is not null;
        var first = RunnerCommand.Resolve(SampleDll, Host, out var problem, "GoodBot");
        problem.Should().BeNull();
        var second = python
            ? RunnerCommand.Resolve(Path.Combine(RepositoryRoot(), "bots", "python", "example.bot.json"), Host, out problem)
            : RunnerCommand.Resolve(SampleDll, Host, out problem, "GoodBot");
        problem.Should().BeNull();
        Online(await NewOwnerAndBot("Ann", "Alpha Bot"), first!);
        Online(await NewOwnerAndBot("Bob", "Beta Bot"), second!);

        var store = _factory.Services.GetRequiredService<MatchStore>();
        var deadline = DateTime.UtcNow + Patience;
        while (store.Of("Alpha Bot", 1).Count == 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the bots should have played a ranked match by now");
            await Task.Delay(100);
        }

        _status.Should().NotContain(s => s.StartsWith("reconnecting", StringComparison.Ordinal), "no connection was dropped");
        var match = store.Load(store.Of("Alpha Bot", 1)[0].Id)!;
        match.Hands.Should().HaveCount(200);
        match.Results.Should().AllSatisfy(r =>
        {
            r.Illegal.Should().Be(0, $"{r.Name} answers every decision legally");
            r.Late.Should().Be(0, $"{r.Name} answers at once");
            r.Gone.Should().Be(0);
        });
    }

    [Fact]
    public void Resolve_ExplainsABadPath()
    {
        RunnerCommand.Resolve("nope.txt", Host, out var problem).Should().BeNull();
        problem.Should().Contain("nope.txt");

        RunnerCommand.Resolve("nope.bot.json", Host, out problem).Should().BeNull();
        problem.Should().Contain("nope.bot.json");
    }

    [Fact]
    public void Resolve_ExplainsAnUnreadableDescription_AndAMissingBotHost()
    {
        Directory.CreateDirectory(_folder);
        var broken = Path.Combine(_folder, "broken.bot.json");
        File.WriteAllText(broken, "{ \"name\": \"x\" }");
        RunnerCommand.Resolve(broken, Host, out var problem).Should().BeNull();
        problem.Should().Contain("command");

        RunnerCommand.Resolve(SampleDll, Path.Combine(_folder, "missing", "PokerBotHost.dll"), out problem, "GoodBot").Should().BeNull();
        problem.Should().Contain("bot host");
    }

    [Fact]
    public void Resolve_TurnsADllIntoACommandThroughTheBotHost()
    {
        var command = RunnerCommand.Resolve(SampleDll, Host, out var problem, "GoodBot");

        problem.Should().BeNull();
        command!.Arguments.Should().Contain(SampleDll).And.Contain("GoodBot");
    }

    [Fact]
    public void Server_IsTheOption_ThenTheGameServersHostWithBots_ThenLocalhost()
    {
        RunnerCommand.Server("wss://poker.example/bots", "ws://other:1/play").AbsoluteUri.Should().Be("wss://poker.example/bots");
        RunnerCommand.Server(null, "ws://games.example:8080/play").AbsoluteUri.Should().Be("ws://games.example:8080/bots");
        RunnerCommand.Server(null, null).AbsoluteUri.Should().Be("ws://localhost:5080/bots");
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Games.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Games.slnx not found above the test folder");
    }

    private async Task<string> NewOwnerAndBot(string owner, string bot)
    {
        using var ownerResponse = await _http.PostAsJsonAsync(new Uri("/api/poker/owners", UriKind.Relative), new { name = owner });
        var ownerToken = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("ownerToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/poker/bots", UriKind.Relative)) { Content = JsonContent.Create(new { name = bot }) };
        request.Headers.Add("X-Owner-Token", ownerToken);
        using var response = await _http.SendAsync(request);
        response.IsSuccessStatusCode.Should().BeTrue();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("botToken").GetString()!;
    }

    private void Online(string token, AgentCommand command)
    {
        var relay = new AgentRelay(
            cancel => _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/bots"), cancel),
            token,
            command);
        relay.Status += _status.Enqueue;
        _relays.Add(relay);
        _ = relay.RunAsync(_stop.Token);
    }
}
