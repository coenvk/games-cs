using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GameServer.Poker;
using Jentl.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Poker;
using Poker.Arena;
using Uno;

namespace GameServer.Tests.Poker;

/// <summary>
/// The real server in memory with its poker endpoints: owners and bots registered over HTTP, bots online through real
/// <see cref="AgentRelay"/>s running the echo bot over the server's <c>/bots</c> WebSocket, each test on its own
/// temporary data folder and with 10-hand matches.
/// </summary>
public sealed class PokerEndpointTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-server-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<AgentRelay> _runners = [];
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public PokerEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("poker-data", _folder)
            .UseSetting("poker-hands", "10")
            .UseSetting("poker-time-limit-ms", "300")
            .UseSetting("poker-allowance-ms", "100")
            .UseSetting("scores", Path.Combine(_folder, "scores.json")));
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var runner in _runners)
        {
            runner.Dispose();
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
    public async Task RegisteringAnOwnerAndABot_GivesTokens()
    {
        var owner = await NewOwner("Ann");
        owner.Should().NotBeNullOrEmpty();

        using var response = await Post("/api/poker/bots", new { name = "Botty" }, owner);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bot = await Json(response);
        bot.GetProperty("name").GetString().Should().Be("Botty");
        var token = bot.GetProperty("botToken").GetString();
        token.Should().NotBeNullOrEmpty();

        using var again = await Post("/api/poker/bots", new { name = "botty" }, owner);
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(again)).GetProperty("error").GetString().Should().Be("name taken");
        using var stranger = await Post("/api/poker/bots", new { name = "Other" }, "not a token");
        (await Json(stranger)).GetProperty("error").GetString().Should().Be("unknown owner");
        using var badName = await Post("/api/poker/owners", new { name = "!" }, null);
        badName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(badName)).GetProperty("error").GetString().Should().Be("not a name");

        using var renewed = await Post("/api/poker/bots/Botty/token", new { }, owner);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = (await Json(renewed)).GetProperty("botToken").GetString();
        newToken.Should().NotBe(token);

        var mine = await Get("/api/poker/mine", owner);
        var bots = mine.GetProperty("bots").EnumerateArray().ToList();
        bots.Should().ContainSingle();
        bots[0].GetProperty("name").GetString().Should().Be("Botty");
        bots[0].GetProperty("connected").GetBoolean().Should().BeFalse();
        mine.GetRawText().Should().NotContain(newToken!, "a token is shown only once");
        using var nobody = await Send(HttpMethod.Get, "/api/poker/mine", null, "not a token");
        nobody.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheRanking_ListsRatedBots_AndThePageShowsThem()
    {
        var a = Online(await NewOwnerAndBot("Ann", "Alpha Bot"));
        Online(await NewOwnerAndBot("Bob", "Beta Bot"));

        await Until(async () => (await Get("/api/poker/ranking")).GetArrayLength() == 2);

        var ranking = (await Get("/api/poker/ranking")).EnumerateArray().ToList();
        ranking.Select(r => r.GetProperty("bot").GetString()).Should().BeEquivalentTo("Alpha Bot", "Beta Bot");
        ranking.Should().OnlyContain(r => r.GetProperty("matches").GetInt32() >= 1 && r.GetProperty("provisional").GetBoolean());
        ranking.Select(r => r.GetProperty("owner").GetString()).Should().BeEquivalentTo("Ann", "Bob");

        var bot = await Get("/api/poker/bots/Alpha%20Bot");
        bot.GetProperty("record").GetProperty("matches").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        bot.GetProperty("recent").EnumerateArray().Should().NotBeEmpty().And.OnlyContain(m => m.GetProperty("ranked").GetBoolean());
        using var unknown = await Send(HttpMethod.Get, "/api/poker/bots/Nobody", null, null);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var page = await _http.GetAsync(new Uri("/poker", UriKind.Relative));
        page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("Alpha Bot").And.Contain("Beta Bot").And.Contain("provisional");

        a.Statuses.Should().Contain(s => s.Contains("\"rating\":1500", StringComparison.Ordinal), "the runner hears its rating on welcome");
    }

    [Fact]
    public async Task AMatchDownload_NeedsItsOwner_AndHidesTheOpponentsFoldedCards()
    {
        var ann = await NewOwner("Ann");
        var annBot = await NewBot(ann, "Alpha Bot");
        var bob = await NewOwner("Bob");

        // Skips its 5th and 9th decisions: one of them is the small blind's first move of a hand, folded for it.
        Online(await NewBot(bob, "Beta Bot"), EchoBot("skip:5,9"));
        Online(annBot);
        await Until(async () => (await Get("/api/poker/bots/Alpha%20Bot")).GetProperty("recent").GetArrayLength() >= 1);
        var id = (await Get("/api/poker/bots/Alpha%20Bot")).GetProperty("recent")[0].GetProperty("id").GetString()!;
        var carol = await NewOwner("Carol");

        (await Download(id, carol)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Download(id, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Download("../owners", ann)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Download("20261026-120000-0000000000000000", ann)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var response = await Download(id, ann);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = MatchFile.Read(await response.Content.ReadAsStringAsync());
        var full = _factory.Services.GetRequiredService<MatchStore>().Load(id)!;
        match.Hands.Should().HaveCount(10);
        match.Settings.Seed.Should().Be(default(DealSeed), "the seed would deal the cards again");
        var folded = match.Hands.Where(h => h.Shown.Count == 0).ToList();
        folded.Should().NotBeEmpty("the slow bot folded at least once");
        foreach (var hand in folded)
        {
            var own = hand.Names.ToList().IndexOf("Alpha Bot");
            hand.Holes[own].Should().HaveCount(2);
            hand.Holes[1 - own].Should().BeEmpty("the opponent's folded cards stay hidden");
            full.Hands[hand.Number - 1].Holes[1 - own].Should().HaveCount(2, "the server keeps every card");
        }

        var bobs = await Get("/api/poker/mine", bob);
        bobs.GetProperty("bots")[0].GetProperty("connected").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AFloodingRunner_DoesNotStopAnotherMatch()
    {
        var league = _factory.Services.GetRequiredService<PokerLeague>();
        var store = _factory.Services.GetRequiredService<MatchStore>();
        var flooder = await NewOwnerAndBot("Eve", "Echo Flood");
        Online(await NewOwnerAndBot("Carol", "Charlie Bot"));
        Online(await NewOwnerAndBot("Dave", "Delta Bot"));
        await Until(() => Task.FromResult(league.Playing.Contains("Charlie Bot")));

        using var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/bots"), _stop.Token);
        await SendText(socket, Relay.Write(new RelayHello(flooder, Relay.Version)));
        var closed = false;
        try
        {
            for (var i = 0; i < 1000 && socket.State == WebSocketState.Open; i++)
            {
                await SendText(socket, Relay.Write(new RelayLine("{}", 1)));
            }

            var buffer = new byte[4096];
            var deadline = DateTime.UtcNow + Patience;
            while (!closed && DateTime.UtcNow < deadline)
            {
                closed = (await socket.ReceiveAsync(buffer, _stop.Token)).MessageType == WebSocketMessageType.Close;
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException)
        {
            closed = true; // the server closed it while we were still sending (the test host says so with an IOException)
        }

        closed.Should().BeTrue("a runner that floods the server loses its connection");
        // Once it is over, Charlie may meet the flooding bot while that is still connected: look for the match with Delta.
        bool AgainstDelta(StoredMatch m) => m.BotA == "Delta Bot" || m.BotB == "Delta Bot";
        await Until(() => Task.FromResult(store.Of("Charlie Bot", 10).Any(AgainstDelta)));
        store.Of("Charlie Bot", 10).First(AgainstDelta).Hands.Should().Be(10);
    }

    [Fact]
    public async Task TheUnoRooms_StillWork()
    {
        using var player = new NetClient(async cancel =>
            await _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/play"), cancel));

        player.Send(new CreateRoom(UnoOnline.Game, 2));

        var deadline = DateTime.UtcNow + Patience;
        RoomJoined? joined = null;
        while (joined is null && DateTime.UtcNow < deadline)
        {
            joined = player.TryReceive(out var message) ? message as RoomJoined : null;
            await Task.Delay(10);
        }

        joined.Should().NotBeNull();
        joined!.Seat.Should().Be(0);
    }

    [Fact]
    public async Task APracticeMatch_IsTheOwnersOnly()
    {
        var ann = await NewOwner("Ann");
        Online(await NewBot(ann, "Alpha Bot"));
        await Until(async () => (await Get("/api/poker/mine", ann)).GetProperty("bots")[0].GetProperty("connected").GetBoolean());

        using var refused = await Post("/api/poker/bots/Alpha%20Bot/practice", new { houseBot = "No such bot" }, ann);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var practice = await Post("/api/poker/bots/Alpha%20Bot/practice", new { houseBot = "Always call" }, ann);
        practice.IsSuccessStatusCode.Should().BeTrue();

        await Until(async () => (await Get("/api/poker/mine", ann)).GetProperty("bots")[0].GetProperty("recent").GetArrayLength() == 1);
        (await Get("/api/poker/bots/Alpha%20Bot")).GetProperty("recent").GetArrayLength().Should().Be(0, "practice is for the owner only");
        (await Get("/api/poker/ranking")).GetArrayLength().Should().Be(0);
    }

    private static AgentCommand EchoBot(params string[] arguments) =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "EchoBot", "Poker.Tests.EchoBot.dll"), .. arguments]);

    private async Task<string> NewOwner(string name)
    {
        using var response = await Post("/api/poker/owners", new { name }, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("ownerToken").GetString()!;
    }

    private async Task<string> NewOwnerAndBot(string owner, string bot) => await NewBot(await NewOwner(owner), bot);

    private async Task<string> NewBot(string owner, string bot)
    {
        using var response = await Post("/api/poker/bots", new { name = bot }, owner);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("botToken").GetString()!;
    }

    private Runner Online(string token, AgentCommand? command = null)
    {
        var runner = new Runner(
            cancel => _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/bots"), cancel),
            token,
            command ?? EchoBot(),
            _stop.Token);
        _runners.Add(runner.Relay);
        return runner;
    }

    private Task<HttpResponseMessage> Post(string path, object body, string? owner) => Send(HttpMethod.Post, path, JsonContent.Create(body), owner);

    private Task<HttpResponseMessage> Download(string id, string? owner) => Send(HttpMethod.Get, "/api/poker/matches/" + Uri.EscapeDataString(id), null, owner);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content, string? owner)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        if (owner is not null)
        {
            request.Headers.Add("X-Owner-Token", owner);
        }

        return await _http.SendAsync(request);
    }

    private async Task<JsonElement> Get(string path, string? owner = null)
    {
        using var response = await Send(HttpMethod.Get, path, null, owner);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await Json(response);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task SendText(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>Waits until <paramref name="done"/> holds; the server's own ticker runs the league meanwhile.</summary>
    private static async Task Until(Func<Task<bool>> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!await done())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the server should have got there by now");
            await Task.Delay(50);
        }
    }

    /// <summary>A runner on an owner's computer, connected to the test server's <c>/bots</c>.</summary>
    private sealed class Runner
    {
        public Runner(Func<CancellationToken, Task<WebSocket>> connect, string token, AgentCommand command, CancellationToken stop)
        {
            Relay = new AgentRelay(connect, token, command);
            Relay.Status += Statuses.Enqueue;
            _ = Relay.RunAsync(stop);
        }

        public AgentRelay Relay { get; }

        /// <summary>What the runner showed its owner.</summary>
        public ConcurrentQueue<string> Statuses { get; } = new();
    }
}
