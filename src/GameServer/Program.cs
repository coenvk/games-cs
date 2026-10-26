using System.Globalization;
using GameServer;
using GameServer.Poker;
using Jentl.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Poker;
using Poker.Online;
using Uno;

// The Jentl game server: players connect to /play with a WebSocket, then create or join rooms, or post and read
// leaderboards. Listens on http://0.0.0.0:5080 unless ASPNETCORE_URLS (or --urls) says otherwise; keeps the
// leaderboards in scores.json in the working directory unless JENTL_SCORES (or --scores) names another file.
//
// The poker bot league: runners connect their owners' bots to /bots; owners and bots register at /api/poker, and the
// ranking is at /poker (and /api/poker/ranking). Its settings, each an option (--name value) or an environment variable:
//   poker-data                  JENTL_POKER_DATA                  the data folder (poker-data in the working directory)
//   poker-hands                 JENTL_POKER_HANDS                 hands in a match (1000)
//   poker-time-limit-ms         JENTL_POKER_TIME_LIMIT_MS         a bot's time for a decision (1000)
//   poker-allowance-ms          JENTL_POKER_ALLOWANCE_MS          added to it for the network (500)
//   poker-concurrent            JENTL_POKER_CONCURRENT            matches at once (8)
//   poker-bots-per-owner        JENTL_POKER_BOTS_PER_OWNER        (5)
//   poker-registrations-per-day JENTL_POKER_REGISTRATIONS_PER_DAY registrations from one address in a day (10)
//   trust-proxy                 JENTL_TRUST_PROXY                 true behind a TLS proxy: the client's address is
//                                                                 read from its X-Forwarded-For header (false)
// `ban <bot-or-owner-id>` as the first arguments bans a bot (by name) or an owner (by id) in the data folder and exits;
// stop the server first, as a running server keeps its own copy of the owners and bots.
if (args.Length >= 2 && args[0] == "ban")
{
    return Ban(args[1], new ConfigurationBuilder().AddCommandLine(args[2..]).Build());
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.WebHost.UseUrls(config["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5080");
var scoresFile = config["scores"] ?? Environment.GetEnvironmentVariable("JENTL_SCORES") ?? "scores.json";
builder.Services.AddSingleton(_ => new RoomServer(
    new Dictionary<string, Func<IRoomGame>>
    {
        [UnoOnline.Game] = () => new UnoRoomGame(),
        [HoldemOnline.Game] = () => new HoldemRoomGame(),
    },
    scores: new ScoreServer(Leaderboards.Boards, TimeProvider.System, scoresFile)));
builder.Services.AddHostedService<RoomTicker>();

var league = new LeagueSettings(
    Number(config, "poker-hands", 1000),
    TimeSpan.FromMilliseconds(Number(config, "poker-time-limit-ms", 1000)),
    TimeSpan.FromMilliseconds(Number(config, "poker-allowance-ms", 500)),
    Number(config, "poker-concurrent", 8));
var limits = new RegistrationLimits(Number(config, "poker-bots-per-owner", 5), Number(config, "poker-registrations-per-day", 10));
var trustProxy = bool.Parse(Setting(config, "trust-proxy") ?? "false");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new PokerData(DataFolder(config)));
builder.Services.AddSingleton<Registry>();
builder.Services.AddSingleton(s => new Registration(s.GetRequiredService<Registry>(), limits, s.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<Ratings>();
builder.Services.AddSingleton<MatchStore>();
builder.Services.AddSingleton(s =>
{
    // A runner's messages are its bot's short lines (see OnlineLimits); a ban cuts the bot's runner off at once.
    var registry = s.GetRequiredService<Registry>();
    var relays = new RelayHost(token => registry.BotOfToken(token)?.Name) { MaxLength = OnlineLimits.MaxMessage };
    registry.BotsBanned += bots =>
    {
        foreach (var bot in bots)
        {
            relays.Drop(bot, "banned");
        }
    };
    return relays;
});
builder.Services.AddSingleton(s =>
{
    var registry = s.GetRequiredService<Registry>();
    var ratings = s.GetRequiredService<Ratings>();
    return new Matchmaker(
        bot => (registry.Bot(bot)?.OwnerId ?? throw new KeyNotFoundException(bot), ratings.Of(bot).Rating.Rating),
        s.GetRequiredService<TimeProvider>(),
        new MatchmakerSettings());
});

// A singleton the host made, so the host disposes it on shutdown: the running matches are dropped, not rated.
builder.Services.AddSingleton(s => new PokerLeague(
    s.GetRequiredService<RelayHost>(),
    s.GetRequiredService<Registry>(),
    s.GetRequiredService<Ratings>(),
    s.GetRequiredService<Matchmaker>(),
    s.GetRequiredService<MatchStore>(),
    league,
    s.GetRequiredService<TimeProvider>(),
    s.GetRequiredService<ILogger<PokerLeague>>()));
builder.Services.AddHostedService<LeagueTicker>();
if (trustProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear(); // the proxy is whatever is in front of the server: told so by trust-proxy
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();
if (trustProxy)
{
    app.UseForwardedHeaders();
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/", (RoomServer rooms) => $"Jentl game server: {rooms.RoomCount} rooms open.");
app.Map("/play", async (HttpContext context, RoomServer rooms) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect with a WebSocket.").ConfigureAwait(false);
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    using var connection = new SocketConnection(socket, rooms);
    await connection.RunAsync(context.RequestAborted).ConfigureAwait(false);
});
app.MapPoker();

await app.RunAsync().ConfigureAwait(false);
return 0;

// An option, or else its environment variable: JENTL_ and the name in capitals with underscores.
static string? Setting(IConfiguration config, string name) =>
    config[name] ?? Environment.GetEnvironmentVariable("JENTL_" + name.Replace('-', '_').ToUpperInvariant());

static int Number(IConfiguration config, string name, int fallback) =>
    Setting(config, name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

static string DataFolder(IConfiguration config) => Setting(config, "poker-data") ?? "poker-data";

static int Ban(string botOrOwner, IConfiguration config)
{
    var registry = new Registry(new PokerData(DataFolder(config)), TimeProvider.System);
    var what = registry.Bot(botOrOwner) is { } bot ? $"bot {bot.Name}"
        : registry.OwnerById(botOrOwner) is { } owner ? $"owner {owner.Name} ({owner.Id}) and their bots"
        : null;
    if (what is null)
    {
        Console.Error.WriteLine($"There is no bot named {botOrOwner}, nor an owner with that id.");
        return 1;
    }

    registry.Ban(botOrOwner);
    Console.WriteLine($"Banned {what}.");
    return 0;
}

/// <summary>The entry point, visible to the integration tests.</summary>
public partial class Program;
