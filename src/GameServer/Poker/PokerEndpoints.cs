using System.Text.Json;
using System.Net.WebSockets;
using System.Text.Json.Serialization.Metadata;
using Jentl.Net;
using Microsoft.AspNetCore.Http.Features;
using Poker;
using Poker.Arena;

namespace GameServer.Poker;

/// <summary>
/// The poker league's endpoints: the runners' WebSocket at <c>/bots</c>, the JSON API under <c>/api/poker</c> and the
/// ranking page at <c>/poker</c>. An owner is known by the <c>X-Owner-Token</c> header. Tokens appear only in the
/// response that issues them; public lists show ranked matches only, and a match is served only to an owner of one of
/// its bots, as that bot's owner may see it (<see cref="OwnerView"/>).
/// </summary>
public static partial class PokerEndpoints
{
    /// <summary>The header that carries an owner's token.</summary>
    public const string OwnerHeader = "X-Owner-Token";

    /// <summary>The largest request body the JSON API reads, in bytes: its requests carry a name or two.</summary>
    public const int MaxBody = 16 * 1024;

    /// <summary>How many recent matches a bot's page and the owner's list show.</summary>
    private const int Recent = 20;

    /// <summary>The most characters of a runner's close reason that are logged: the most a WebSocket close reason has.</summary>
    private const int MaxReason = 123;

    /// <summary>Maps every poker endpoint on <paramref name="app"/>.</summary>
    public static void MapPoker(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Map("/bots", ServeRunnerAsync);
        app.MapGet("/poker", (Ratings ratings) => Results.Content(RankingPage.Html(ratings.Ranking()), "text/html; charset=utf-8"));

        var api = app.MapGroup("/api/poker").AddEndpointFilter(LimitBodyAsync);
        api.MapPost("/owners", NewOwnerAsync);
        api.MapPost("/bots", NewBotAsync);
        api.MapPost("/bots/{name}/token", RenewToken);
        api.MapPost("/bots/{name}/practice", PractiseAsync);
        api.MapGet("/ranking", (Ratings ratings) => Results.Json(ratings.Ranking(), PokerJson.Default.IReadOnlyListRankingRow));
        api.MapGet("/bots/{name}", BotPageOf);
        api.MapGet("/mine", Mine);
        api.MapGet("/matches/{id}", Download);
    }

    /// <summary>
    /// Serves one runner's connection until it ends (see <see cref="RelayHost.RunAsync"/>), and logs why it ended when
    /// that was not an ordinary close: the limit the runner broke, or what the runner said when it closed for a problem.
    /// What it said comes from anyone who connects, before any hello, so it is logged as <see cref="Printable"/> makes it.
    /// </summary>
    private static async Task ServeRunnerAsync(HttpContext context, RelayHost relays, ILogger<RelayHost> log)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Connect with a WebSocket.").ConfigureAwait(false);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var problem = await relays.RunAsync(socket, context.RequestAborted).ConfigureAwait(false);
        if (problem is not null)
        {
            LogLimitBroken(log, AddressOf(context), problem);
        }
        else if (socket.CloseStatus is { } status and not WebSocketCloseStatus.NormalClosure and not WebSocketCloseStatus.EndpointUnavailable)
        {
            LogRunnerClosed(log, AddressOf(context), status, Printable(socket.CloseStatusDescription));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closed a bot connection from {Address}: {Problem}")]
    private static partial void LogLimitBroken(ILogger logger, string? address, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A runner from {Address} closed its connection: {Status} {Reason}")]
    private static partial void LogRunnerClosed(ILogger logger, string? address, WebSocketCloseStatus status, string? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "The match file of {Id} is broken; it is served as not found")]
    private static partial void LogBrokenMatch(ILogger logger, Exception error, string id);

    private static async Task<IResult> NewOwnerAsync(HttpContext context, Registration registration)
    {
        var request = await ReadAsync(context.Request, PokerJson.Default.NameRequest).ConfigureAwait(false);
        var result = registration.NewOwner(request?.Name ?? string.Empty, AddressOf(context));
        return result.Ok ? Results.Json(new OwnerIssued(result.Value.Token), PokerJson.Default.OwnerIssued) : Refused(result.Refusal);
    }

    private static async Task<IResult> NewBotAsync(HttpContext context, Registration registration)
    {
        var request = await ReadAsync(context.Request, PokerJson.Default.NameRequest).ConfigureAwait(false);
        var result = registration.NewBot(OwnerToken(context), request?.Name ?? string.Empty, AddressOf(context));
        return result.Ok
            ? Results.Json(new BotIssued(result.Value.Account.Name, result.Value.Token), PokerJson.Default.BotIssued)
            : Refused(result.Refusal);
    }

    /// <summary>Gives the bot a new token, and cuts off a runner still connected with the old one (which may have leaked).</summary>
    private static IResult RenewToken(HttpContext context, string name, Registration registration, RelayHost relays)
    {
        var result = registration.RenewBotToken(OwnerToken(context), name);
        if (!result.Ok)
        {
            return Refused(result.Refusal);
        }

        relays.Drop(result.Value.Account.Name, "token replaced");
        return Results.Json(new TokenIssued(result.Value.Token), PokerJson.Default.TokenIssued);
    }

    private static async Task<IResult> PractiseAsync(HttpContext context, string name, Registry registry, PokerLeague league)
    {
        var request = await ReadAsync(context.Request, PokerJson.Default.PracticeRequest).ConfigureAwait(false);
        if (registry.OwnerOfToken(OwnerToken(context)) is not { } owner)
        {
            return Refused("unknown owner");
        }

        if (registry.Bot(name) is not { } bot || bot.OwnerId != owner.Id)
        {
            return Refused("unknown bot");
        }

        if (!HouseBots.All.Any(h => h.Name == request?.HouseBot))
        {
            return Refused("unknown house bot");
        }

        return league.Practise(bot.Name, request!.HouseBot!)
            ? Results.Accepted()
            : Refused("the bot cannot practise now: it is not connected, already playing, banned or named like that house bot");
    }

    /// <summary>A bot's public page: not found for a banned bot, or a bot of a banned owner, as the ranking leaves those out.</summary>
    private static IResult BotPageOf(string name, Registry registry, Ratings ratings, MatchStore store)
    {
        if (registry.Bot(name) is not { Banned: false } bot || registry.OwnerById(bot.OwnerId) is not { Banned: false })
        {
            return Results.NotFound();
        }

        return Results.Json(new BotPage(ratings.Of(bot.Name), RankedOf(store, bot.Name)), PokerJson.Default.BotPage);
    }

    /// <summary>The owner's bots; 401 "unknown owner" for a token the server does not know, 403 "banned" for a banned owner.</summary>
    private static IResult Mine(HttpContext context, Registry registry, Ratings ratings, MatchStore store, RelayHost relays)
    {
        if (registry.OwnerOfToken(OwnerToken(context)) is not { } owner)
        {
            return registry.AnyOwnerOfToken(OwnerToken(context)) is { Banned: true }
                ? Results.Json(new Refusal("banned"), PokerJson.Default.Refusal, statusCode: StatusCodes.Status403Forbidden)
                : Results.Json(new Refusal("unknown owner"), PokerJson.Default.Refusal, statusCode: StatusCodes.Status401Unauthorized);
        }

        var ranking = ratings.Ranking();
        var bots = registry.BotsOf(owner.Id)
            .Select(bot => new MyBot(
                bot.Name,
                ranking.FirstOrDefault(r => string.Equals(r.Bot, bot.Name, StringComparison.OrdinalIgnoreCase)),
                store.Of(bot.Name, Recent),
                relays.Agent(bot.Name) is { IsAway: false }))
            .ToList();
        return Results.Json(new MyBots(bots), PokerJson.Default.MyBots);
    }

    /// <summary>
    /// A match as the asking owner's bot may see it, in the match file's JSON. Not found, alike, when the id is no match,
    /// there is no such match, or the owner has no bot in it: nobody learns which matches exist. A broken match file is
    /// logged, and not found either.
    /// </summary>
    private static IResult Download(HttpContext context, string id, Registry registry, MatchStore store, ILogger<MatchStore> log)
    {
        if (registry.OwnerOfToken(OwnerToken(context)) is not { } owner)
        {
            return Results.NotFound();
        }

        MatchResult? match;
        try
        {
            match = store.Load(id);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
        {
            LogBrokenMatch(log, e, id);
            return Results.NotFound();
        }

        if (match is null)
        {
            return Results.NotFound();
        }

        var bot = match.Bots.FirstOrDefault(name => registry.Bot(name) is { } account && account.OwnerId == owner.Id);
        return bot is null ? Results.NotFound() : Results.Content(MatchFile.Write(OwnerView.For(match, bot)), "application/json; charset=utf-8");
    }

    /// <summary>A bot's last ranked matches, newest first; its practice matches are its owner's only.</summary>
    private static List<StoredMatch> RankedOf(MatchStore store, string bot) =>
        [.. store.Of(bot, int.MaxValue).Where(m => m.Ranked).Take(Recent)];

    private static string OwnerToken(HttpContext context) => context.Request.Headers[OwnerHeader].ToString();

    /// <summary>The client's address: the connection's, which the forwarded-headers middleware sets from the proxy's header when the proxy is trusted.</summary>
    private static string? AddressOf(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    /// <summary>A refusal with the server's reason: 403 for a ban ("banned" for the owner, "bot banned" for the bot alone), else 400.</summary>
    private static IResult Refused(string? reason) =>
        Results.Json(
            new Refusal(reason ?? "refused"),
            PokerJson.Default.Refusal,
            statusCode: reason is "banned" or "bot banned" ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);

    /// <summary>
    /// Refuses a request body over <see cref="MaxBody"/> with 413 before the endpoint reads it: at once when it says it is
    /// longer, else (a body streamed without its length) once more than that has come. Such a body within the limit is
    /// kept in memory for the endpoint to read.
    /// </summary>
    private static async ValueTask<object?> LimitBodyAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var request = context.Request;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = MaxBody; // Kestrel's own limit too, for a body longer than it said
        }

        if (request.ContentLength > MaxBody)
        {
            return TooLarge();
        }

        if (request.ContentLength is null)
        {
            var body = new MemoryStream();
            context.Response.RegisterForDispose(body);
            var chunk = new byte[4096];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > MaxBody)
                {
                    return TooLarge();
                }

                body.Write(chunk, 0, read);
            }

            body.Position = 0;
            request.Body = body;
        }

        return await next(invocation).ConfigureAwait(false);
    }

    private static IResult TooLarge() =>
        Results.Json(new Refusal("too large"), PokerJson.Default.Refusal, statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Text from a peer as a log line may show it: each control character (a line break that would forge a line of its own,
    /// a terminal's escape) as '?', and at most <see cref="MaxReason"/> characters.
    /// </summary>
    private static string? Printable(string? text) =>
        text is null ? null : string.Concat(text.Take(MaxReason).Select(c => char.IsControl(c) ? '?' : c));

    /// <summary>The request's JSON body, or null when there is none or it is not the JSON expected.</summary>
    private static async Task<T?> ReadAsync<T>(HttpRequest request, JsonTypeInfo<T> info)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, info, request.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
