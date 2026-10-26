using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Poker.Arena;

namespace Poker.Online;

/// <summary>
/// One line of the server's poker ranking. <paramref name="Range"/> is twice the rating deviation; <paramref name="Place"/>
/// is null for a provisional bot, listed after the established ones; <paramref name="Margin"/> is the 95% margin of
/// <paramref name="BbPer100"/>.
/// </summary>
public sealed record RankingRowJson(
    int? Place,
    string Bot,
    string Owner,
    double Rating,
    double Range,
    bool Provisional,
    int Matches,
    int Won,
    int Drawn,
    int Lost,
    double BbPer100,
    double Margin,
    DateTimeOffset? LastPlayed);

/// <summary>
/// A match the server keeps, as it lists it: its id (to download it with), when it was played, whether it was ranked
/// (or practice), its two bots and what each won in chips, and how many hands were played.
/// </summary>
public sealed record StoredMatchJson(string Id, DateTimeOffset At, bool Ranked, string BotA, string BotB, int NetA, int NetB, long Hands);

/// <summary>
/// One of the owner's bots: its line of the ranking (null before its first ranked match, or when banned), its last
/// matches newest first with the practice ones, and whether its runner is connected.
/// </summary>
public sealed record MyBotJson(string Name, RankingRowJson? Row, IReadOnlyList<StoredMatchJson> Recent, bool Connected);

/// <summary>Everything of an owner's bots.</summary>
public sealed record MineJson(IReadOnlyList<MyBotJson> Bots);

/// <summary>
/// What went wrong with a request to the server: <see cref="Exception.Message"/> is the server's own words when it
/// refused (<see cref="Status"/> says how), or why the server could not be reached (<see cref="IsUnreachable"/>).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "A problem the player is told of, by the name the workshop uses for it.")]
public sealed class OnlineProblem : Exception
{
    public OnlineProblem()
    {
    }

    public OnlineProblem(string message)
        : base(message)
    {
    }

    public OnlineProblem(string message, Exception inner)
        : base(message, inner)
    {
    }

    public OnlineProblem(string message, HttpStatusCode? status, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
    }

    /// <summary>The server's answer, or null when there was none.</summary>
    public HttpStatusCode? Status { get; }

    /// <summary>Whether no answer came: the server is down, or this computer is offline.</summary>
    public bool IsUnreachable => Status is null;
}

/// <summary>
/// The poker league's HTTP API as the bot workshop uses it (see the server's <c>/api/poker</c> endpoints): owners and
/// bots registered, tokens renewed, the ranking and the owner's bots read, practice asked for and matches downloaded.
/// The owner is known by its token in the <see cref="OwnerHeader"/> header; tokens are never written anywhere by this
/// class. Every failure is an <see cref="OnlineProblem"/>.
/// </summary>
/// <param name="http">Sends the requests; the caller owns it (and sets its time-out).</param>
/// <param name="server">The server's HTTP address, as <see cref="ApiAddress"/> gives it.</param>
public sealed class OnlineClient(HttpClient http, Uri server)
{
    /// <summary>The header that carries an owner's token.</summary>
    public const string OwnerHeader = "X-Owner-Token";

    /// <summary>
    /// The HTTP address of the game server at <paramref name="gameServer"/> (the app's WebSocket address,
    /// <c>ws://host:port/play</c>): http for ws and https for wss, at the root.
    /// </summary>
    public static Uri ApiAddress(Uri gameServer)
    {
        ArgumentNullException.ThrowIfNull(gameServer);
        return new UriBuilder(gameServer) { Scheme = Secure(gameServer) ? "https" : "http", Path = "/", Query = "", Port = Port(gameServer) }.Uri;
    }

    /// <summary>The address a runner connects to on the game server at <paramref name="gameServer"/>: ws or wss, at <c>/bots</c>.</summary>
    public static Uri RunnerAddress(Uri gameServer)
    {
        ArgumentNullException.ThrowIfNull(gameServer);
        return new UriBuilder(gameServer) { Scheme = Secure(gameServer) ? "wss" : "ws", Path = "/bots", Query = "", Port = Port(gameServer) }.Uri;
    }

    /// <summary>Makes a new owner called <paramref name="name"/> and returns its token, which is shown this once.</summary>
    public async Task<string> NewOwnerAsync(string name, CancellationToken cancel = default)
    {
        var issued = await SendAsync(HttpMethod.Post, "api/poker/owners", null, Name(name), OnlineJson.Default.OwnerIssued, cancel).ConfigureAwait(false);
        return issued.OwnerToken;
    }

    /// <summary>Registers a bot called <paramref name="name"/>; returns its name as the server keeps it and its token, shown this once.</summary>
    public async Task<(string Name, string Token)> NewBotAsync(string ownerToken, string name, CancellationToken cancel = default)
    {
        var issued = await SendAsync(HttpMethod.Post, "api/poker/bots", ownerToken, Name(name), OnlineJson.Default.BotIssued, cancel).ConfigureAwait(false);
        return (issued.Name, issued.BotToken);
    }

    /// <summary>Gives the bot a new token, shown this once; the old one no longer works.</summary>
    public async Task<string> NewBotTokenAsync(string ownerToken, string name, CancellationToken cancel = default)
    {
        var issued = await SendAsync(HttpMethod.Post, $"api/poker/bots/{Uri.EscapeDataString(name)}/token", ownerToken, null, OnlineJson.Default.TokenIssued, cancel).ConfigureAwait(false);
        return issued.BotToken;
    }

    /// <summary>The ranking, established bots first by place, then the provisional ones.</summary>
    public async Task<IReadOnlyList<RankingRowJson>> RankingAsync(CancellationToken cancel = default) =>
        await SendAsync(HttpMethod.Get, "api/poker/ranking", null, null, OnlineJson.Default.IReadOnlyListRankingRowJson, cancel).ConfigureAwait(false);

    /// <summary>The owner's bots, with their practice matches.</summary>
    public Task<MineJson> MineAsync(string ownerToken, CancellationToken cancel = default) =>
        SendAsync(HttpMethod.Get, "api/poker/mine", ownerToken, null, OnlineJson.Default.MineJson, cancel);

    /// <summary>Asks for a practice match of the owner's bot <paramref name="name"/> against a house bot; it is played once the bot is free.</summary>
    public async Task PractiseAsync(string ownerToken, string name, string houseBot, CancellationToken cancel = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/poker/bots/{Uri.EscapeDataString(name)}/practice", ownerToken, JsonContent.Create(new PracticeRequest(houseBot), OnlineJson.Default.PracticeRequest), cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// A match one of the owner's bots played, as its owner may see it: no seeds, and the opponent's cards only where
    /// they were shown (see <see cref="OwnerView"/>).
    /// </summary>
    public async Task<MatchResult> MatchAsync(string ownerToken, string id, CancellationToken cancel = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/poker/matches/" + Uri.EscapeDataString(id), ownerToken, null, cancel).ConfigureAwait(false);
        try
        {
            return MatchFile.Read(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch (InvalidDataException e)
        {
            throw new OnlineProblem(e.Message, response.StatusCode, e);
        }
    }

    private static bool Secure(Uri address) => address.Scheme is "wss" or "https";

    // A default port stays the scheme's default; another is kept.
    private static int Port(Uri address) => address.IsDefaultPort ? -1 : address.Port;

    private static JsonContent Name(string? name) => JsonContent.Create(new NameRequest(name), OnlineJson.Default.NameRequest);

    // The request disposes its content.
    private async Task<T> SendAsync<T>(HttpMethod method, string path, string? ownerToken, HttpContent? content, JsonTypeInfo<T> answer, CancellationToken cancel)
    {
        using var response = await SendAsync(method, path, ownerToken, content, cancel).ConfigureAwait(false);
        try
        {
            return await response.Content.ReadFromJsonAsync(answer, cancel).ConfigureAwait(false)
                ?? throw new OnlineProblem("The server gave an empty answer.", response.StatusCode);
        }
        catch (JsonException e)
        {
            throw new OnlineProblem("The server's answer could not be read: " + e.Message, response.StatusCode, e);
        }
    }

    /// <summary>The server's answer to the request, if it was a success; otherwise its refusal as an <see cref="OnlineProblem"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? ownerToken, HttpContent? content, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, new Uri(server, path)) { Content = content };
        if (ownerToken is not null)
        {
            request.Headers.Add(OwnerHeader, ownerToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new OnlineProblem(e.Message, null, e);
        }
        catch (TaskCanceledException e) when (!cancel.IsCancellationRequested)
        {
            throw new OnlineProblem("The server did not answer in time.", null, e);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new OnlineProblem(await RefusalAsync(response, cancel).ConfigureAwait(false), response.StatusCode);
        }
    }

    // The server's { "error": "…" }, or what the status says when there is none (a 404 has no body).
    private static async Task<string> RefusalAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            var refusal = await response.Content.ReadFromJsonAsync(OnlineJson.Default.Refusal, cancel).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(refusal?.Error))
            {
                return refusal.Error;
            }
        }
        catch (JsonException)
        {
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => "not found",
            HttpStatusCode.Unauthorized => "unknown owner",
            _ => $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}",
        };
    }
}

internal sealed record NameRequest(string? Name);

internal sealed record PracticeRequest(string HouseBot);

internal sealed record OwnerIssued(string OwnerToken);

internal sealed record BotIssued(string Name, string BotToken);

internal sealed record TokenIssued(string BotToken);

internal sealed record Refusal(string? Error);

/// <summary>The JSON of the poker endpoints, camelCase as the server writes it.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(PracticeRequest))]
[JsonSerializable(typeof(OwnerIssued))]
[JsonSerializable(typeof(BotIssued))]
[JsonSerializable(typeof(TokenIssued))]
[JsonSerializable(typeof(Refusal))]
[JsonSerializable(typeof(MineJson))]
[JsonSerializable(typeof(IReadOnlyList<RankingRowJson>))]
internal sealed partial class OnlineJson : JsonSerializerContext;
