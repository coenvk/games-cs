using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameServer.Poker;

/// <summary>What a registration or a renewal asks for: the owner's or the bot's name.</summary>
public sealed record NameRequest(string? Name);

/// <summary>Which house bot a practice match is against.</summary>
public sealed record PracticeRequest(string? HouseBot);

/// <summary>A new owner's token, shown this once.</summary>
public sealed record OwnerIssued(string OwnerToken);

/// <summary>A new bot and its token, shown this once.</summary>
public sealed record BotIssued(string Name, string BotToken);

/// <summary>A bot's new token, shown this once; the old one no longer works.</summary>
public sealed record TokenIssued(string BotToken);

/// <summary>Why a request was turned away, in words for the person who made it.</summary>
public sealed record Refusal(string Error);

/// <summary>A bot's public page: its record and its last ranked matches, newest first.</summary>
public sealed record BotPage(BotRecord Record, IReadOnlyList<StoredMatch> Recent);

/// <summary>
/// One of the owner's bots as the owner sees it: its line of the ranking (null before its first ranked match, or when
/// banned), its last matches with the practice ones, and whether its runner is connected.
/// </summary>
public sealed record MyBot(string Name, RankingRow? Row, IReadOnlyList<StoredMatch> Recent, bool Connected);

/// <summary>Everything of an owner's bots.</summary>
public sealed record MyBots(IReadOnlyList<MyBot> Bots);

/// <summary>
/// What the server tells a runner when it is welcomed: <paramref name="State"/> "waiting", <paramref name="Text"/> to
/// print as it is, and the bot's rating with its range (twice the deviation) and ranked matches so far.
/// </summary>
public sealed record WelcomeStatus(string State, string Text, double Rating, double Range, int Matches);

/// <summary>The JSON of the poker endpoints, camelCase.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(PracticeRequest))]
[JsonSerializable(typeof(OwnerIssued))]
[JsonSerializable(typeof(BotIssued))]
[JsonSerializable(typeof(TokenIssued))]
[JsonSerializable(typeof(Refusal))]
[JsonSerializable(typeof(BotPage))]
[JsonSerializable(typeof(MyBots))]
[JsonSerializable(typeof(IReadOnlyList<RankingRow>))]
[JsonSerializable(typeof(WelcomeStatus))]
internal sealed partial class PokerJson : JsonSerializerContext;
