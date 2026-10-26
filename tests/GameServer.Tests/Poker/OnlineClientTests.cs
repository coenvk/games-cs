using System.Net;
using System.Net.WebSockets;
using Jentl.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Poker;
using Poker.Online;

namespace GameServer.Tests.Poker;

/// <summary>
/// The workshop's <see cref="OnlineClient"/> against the real server in memory: owners and bots registered, the ranking
/// and the owner's bots read, and a match downloaded, with two echo bots put online through real <see cref="AgentRelay"/>s.
/// </summary>
public sealed class OnlineClientTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-online-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<AgentRelay> _relays = [];
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;
    private readonly OnlineClient _client;

    public OnlineClientTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("poker-data", _folder)
            .UseSetting("poker-hands", "10")
            .UseSetting("poker-time-limit-ms", "300")
            .UseSetting("poker-allowance-ms", "100")
            .UseSetting("scores", Path.Combine(_folder, "scores.json")));
        _http = _factory.CreateClient();
        _client = new OnlineClient(_http, _http.BaseAddress!);
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
    public async Task ANewOwner_RegistersABot_AndSeesIt()
    {
        var owner = await _client.NewOwnerAsync("Ann");
        owner.Should().NotBeNullOrEmpty();

        var (name, token) = await _client.NewBotAsync(owner, "Alpha Bot");
        name.Should().Be("Alpha Bot");
        token.Should().NotBeNullOrEmpty();
        var renewed = await _client.NewBotTokenAsync(owner, "Alpha Bot");
        renewed.Should().NotBe(token);

        var mine = await _client.MineAsync(owner);
        mine.Bots.Should().ContainSingle();
        mine.Bots[0].Name.Should().Be("Alpha Bot");
        mine.Bots[0].Connected.Should().BeFalse();
        mine.Bots[0].Row.Should().BeNull("it has played no ranked match");
        mine.Bots[0].Recent.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusal_IsAnOnlineProblem_WithTheServersText()
    {
        var owner = await _client.NewOwnerAsync("Ann");
        await _client.NewBotAsync(owner, "Alpha Bot");

        var taken = () => _client.NewBotAsync(owner, "alpha bot");
        (await taken.Should().ThrowAsync<OnlineProblem>()).Which.Message.Should().Be("name taken");

        var badName = () => _client.NewOwnerAsync("!");
        (await badName.Should().ThrowAsync<OnlineProblem>()).Which.Status.Should().Be(HttpStatusCode.BadRequest);

        var stranger = () => _client.MineAsync("not a token");
        var problem = (await stranger.Should().ThrowAsync<OnlineProblem>()).Which;
        problem.Status.Should().Be(HttpStatusCode.Unauthorized);
        problem.Message.Should().Be("unknown owner");
        problem.IsUnreachable.Should().BeFalse();

        var practice = () => _client.PractiseAsync(owner, "Alpha Bot", "Always call");
        (await practice.Should().ThrowAsync<OnlineProblem>()).Which.Message.Should().Contain("not connected");
    }

    [Fact]
    public async Task AServerThatIsNotThere_IsAnUnreachableProblem()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var client = new OnlineClient(http, new Uri("http://127.0.0.1:1/"));

        var ranking = () => client.RankingAsync();

        var problem = (await ranking.Should().ThrowAsync<OnlineProblem>()).Which;
        problem.IsUnreachable.Should().BeTrue();
        problem.Status.Should().BeNull();
    }

    [Fact]
    public async Task TheRanking_AndAnOwnedMatch_AreRead()
    {
        var ann = await _client.NewOwnerAsync("Ann");
        Online((await _client.NewBotAsync(ann, "Alpha Bot")).Token);
        var bob = await _client.NewOwnerAsync("Bob");
        Online((await _client.NewBotAsync(bob, "Beta Bot")).Token);

        var deadline = DateTime.UtcNow + Patience;
        while ((await _client.RankingAsync()).Count < 2)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the bots should have played by now");
            await Task.Delay(50);
        }

        var ranking = await _client.RankingAsync();
        ranking.Select(r => r.Bot).Should().BeEquivalentTo("Alpha Bot", "Beta Bot");
        ranking.Select(r => r.Owner).Should().BeEquivalentTo("Ann", "Bob");
        ranking.Should().OnlyContain(r => r.Provisional && r.Place == null && r.Matches >= 1 && r.Range > 0 && r.Won + r.Drawn + r.Lost == r.Matches);

        var mine = await _client.MineAsync(ann);
        var alpha = mine.Bots.Single();
        alpha.Connected.Should().BeTrue();
        alpha.Row!.Bot.Should().Be("Alpha Bot");
        var played = alpha.Recent[0];
        played.Ranked.Should().BeTrue();
        new[] { played.BotA, played.BotB }.Should().BeEquivalentTo("Alpha Bot", "Beta Bot");
        played.Hands.Should().Be(10);

        var match = await _client.MatchAsync(ann, played.Id);
        match.Hands.Should().HaveCount(10);
        match.Bots.Should().BeEquivalentTo("Alpha Bot", "Beta Bot");
        match.Settings.Seed.Should().Be(default(DealSeed), "a downloaded match comes as its owner may see it");

        var carol = await _client.NewOwnerAsync("Carol");
        var notHers = () => _client.MatchAsync(carol, played.Id);
        (await notHers.Should().ThrowAsync<OnlineProblem>()).Which.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("ws://localhost:5080/play", "http://localhost:5080/", "ws://localhost:5080/bots")]
    [InlineData("wss://games.example.org/play", "https://games.example.org/", "wss://games.example.org/bots")]
    [InlineData("https://games.example.org/", "https://games.example.org/", "wss://games.example.org/bots")]
    public void TheGameServersAddress_GivesTheApiAndTheRunnersAddresses(string server, string api, string runner)
    {
        OnlineClient.ApiAddress(new Uri(server)).Should().Be(new Uri(api));
        OnlineClient.RunnerAddress(new Uri(server)).Should().Be(new Uri(runner));
    }

    private static AgentCommand EchoBot() =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "EchoBot", "Poker.Tests.EchoBot.dll")]);

    private void Online(string token)
    {
        var relay = new AgentRelay(Connect, token, EchoBot());
        _relays.Add(relay);
        _ = relay.RunAsync(_stop.Token);
    }

    private Task<WebSocket> Connect(CancellationToken cancel) =>
        _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/bots"), cancel);
}
