using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Multiplayer;

namespace Poker.Arena;

/// <summary>How one bot did over a match or a batch.</summary>
/// <param name="Name">The bot's name.</param>
/// <param name="BigBlindsPer100">Its result in big blinds won per hundred hands.</param>
/// <param name="Margin95">The 95% margin of error of <paramref name="BigBlindsPer100"/>; 0 for a single watched match.</param>
/// <param name="Hands">The hands it was dealt into.</param>
/// <param name="Late">Its decisions that were not answered in time.</param>
/// <param name="Illegal">Its decisions answered with nothing or with a move that was not allowed.</param>
/// <param name="Gone">Matches in which it stopped playing.</param>
/// <param name="Places">The places it finished in, one per sit-and-go.</param>
public sealed record BotResult(
    string Name,
    double BigBlindsPer100,
    double Margin95,
    int Hands,
    int Late,
    int Illegal,
    int Gone,
    IReadOnlyList<int> Places);

/// <summary>A finished match or batch: how it was played, how each bot did and every hand.</summary>
/// <param name="Settings">The rules of the table.</param>
/// <param name="Batch">The rules of the batch it was part of, or null for a single match.</param>
/// <param name="At">When it was played.</param>
/// <param name="Bots">The names of the bots that took part.</param>
/// <param name="Results">How each bot did; a watched match has one per seat, with margin 0.</param>
/// <param name="Hands">Every hand played.</param>
public sealed record MatchResult(
    ArenaSettings Settings,
    BatchSettings? Batch,
    DateTimeOffset At,
    IReadOnlyList<string> Bots,
    IReadOnlyList<BotResult> Results,
    IReadOnlyList<HandRecord> Hands);

/// <summary>
/// Saves a <see cref="MatchResult"/> as JSON and reads it back. Property names are camelCase and enums are written by
/// name (for example "Preflop"). Cards are written as their codes ("As"), a move as <c>{ "kind": "raise", "to": 60 }</c> (the kinds are fold,
/// check, call, raise and allIn) and every time limit as whole milliseconds.
/// </summary>
public static class MatchFile
{
    /// <summary>The match as JSON text.</summary>
    public static string Write(MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return JsonSerializer.Serialize(MatchJson.From(match), MatchJsonContext.Default.MatchJson);
    }

    /// <summary>The match in <paramref name="json"/>, as <see cref="Write"/> wrote it.</summary>
    /// <exception cref="InvalidDataException">The text is not a match file.</exception>
    public static MatchResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var dto = JsonSerializer.Deserialize(json, MatchJsonContext.Default.MatchJson)
                ?? throw new InvalidDataException("The file is empty.");
            return dto.ToResult();
        }
        catch (Exception e) when (e is JsonException or ArgumentException or FormatException or InvalidCastException or NullReferenceException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The file is not a match file: " + e.Message, e);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchJson))]
internal sealed partial class MatchJsonContext : JsonSerializerContext;

internal sealed record MatchJson
{
    public required SettingsJson Settings { get; init; }

    public BatchJson? Batch { get; init; }

    public required DateTimeOffset At { get; init; }

    public required List<string> Bots { get; init; }

    public required List<BotResult> Results { get; init; }

    public required List<HandJson> Hands { get; init; }

    public static MatchJson From(MatchResult m) => new()
    {
        Settings = new SettingsJson
        {
            Format = m.Settings.Format,
            Hands = m.Settings.Hands,
            Seed = m.Settings.Seed,
            TimeLimitMs = (long)m.Settings.TimeLimit.TotalMilliseconds,
            StartingStack = m.Settings.StartingStack,
            SmallBlind = m.Settings.SmallBlind,
            StackBigBlinds = m.Settings.StackBigBlinds,
        },
        Batch = m.Batch is null ? null : new BatchJson
        {
            Format = m.Batch.Format,
            Hands = m.Batch.Hands,
            Seed = m.Batch.Seed,
            TimeLimitMs = (long)m.Batch.TimeLimit.TotalMilliseconds,
            Parallel = m.Batch.Parallel,
        },
        At = m.At,
        Bots = [.. m.Bots],
        Results = [.. m.Results],
        Hands = [.. m.Hands.Select(HandJson.From)],
    };

    public MatchResult ToResult() => new(
        new ArenaSettings(Settings.Format, Settings.Hands, Settings.Seed, TimeSpan.FromMilliseconds(Settings.TimeLimitMs), Settings.StartingStack, Settings.SmallBlind, Settings.StackBigBlinds),
        Batch is null ? null : new BatchSettings(Batch.Format, Batch.Hands, Batch.Seed, TimeSpan.FromMilliseconds(Batch.TimeLimitMs), Batch.Parallel),
        At,
        Bots,
        [.. Results.Select(r => r with { Places = r.Places ?? throw new InvalidDataException("A result has no places."), Name = r.Name ?? throw new InvalidDataException("A result has no name.") })],
        [.. Hands.Select(h => h.ToRecord())]);
}

internal sealed record SettingsJson
{
    public required TableFormat Format { get; init; }

    public required int Hands { get; init; }

    public required int Seed { get; init; }

    public required long TimeLimitMs { get; init; }

    public required int StartingStack { get; init; }

    public required int SmallBlind { get; init; }

    public required int StackBigBlinds { get; init; }
}

internal sealed record BatchJson
{
    public required BatchFormat Format { get; init; }

    public required int Hands { get; init; }

    public required int Seed { get; init; }

    public required long TimeLimitMs { get; init; }

    public required bool Parallel { get; init; }
}

internal sealed record HandJson
{
    public required int Number { get; init; }

    public required int Seed { get; init; }

    public required int Button { get; init; }

    public required int SmallBlind { get; init; }

    public required int BigBlind { get; init; }

    public required List<string> Names { get; init; }

    public required List<int> StartStacks { get; init; }

    public required List<List<string>> Holes { get; init; }

    public required List<ActionJson> Actions { get; init; }

    public required List<string> Board { get; init; }

    public required List<PotJson> Pots { get; init; }

    public required List<WonJson> Won { get; init; }

    public required List<int> Shown { get; init; }

    public required List<FaultJson> Faults { get; init; }

    public static HandJson From(HandRecord h) => new()
    {
        Number = h.Number,
        Seed = h.Seed,
        Button = h.Button,
        SmallBlind = h.SmallBlind,
        BigBlind = h.BigBlind,
        Names = [.. h.Names],
        StartStacks = [.. h.StartStacks],
        Holes = [.. h.Holes.Select(c => c.Select(BotView.Code).ToList())],
        Actions = [.. h.Actions.Select(ActionJson.From)],
        Board = [.. h.Board.Select(BotView.Code)],
        Pots = [.. h.Pots.Select(p => new PotJson { Amount = p.Amount, Seats = [.. p.Seats] })],
        Won = [.. h.Won.Select(w => new WonJson { Seat = w.Key, Amount = w.Value })],
        Shown = [.. h.Shown.Order()],
        Faults = [.. h.Faults.Select(f => new FaultJson { Seat = f.Seat, Fault = f.Fault })],
    };

    public HandRecord ToRecord() => new(
        Number,
        Seed,
        Button,
        SmallBlind,
        BigBlind,
        Names,
        StartStacks,
        [.. Holes.Select(c => (IReadOnlyList<Jentl.Cards.Card>)[.. c.Select(BotView.Card)])],
        [.. Actions.Select(a => a.ToRecord())],
        [.. Board.Select(BotView.Card)],
        [.. Pots.Select(p => new Pot(p.Amount, p.Seats))],
        Won.ToDictionary(w => w.Seat, w => w.Amount),
        new HashSet<int>(Shown),
        [.. Faults.Select(f => (f.Seat, f.Fault))]);
}

internal sealed record ActionJson
{
    public required Street Street { get; init; }

    public required int Seat { get; init; }

    public required string Kind { get; init; }

    public int? To { get; init; }

    public required int Amount { get; init; }

    public required int Total { get; init; }

    public static ActionJson From(ActionRecord a) => new()
    {
        Street = a.Street,
        Seat = a.Seat,
        Kind = a.Action switch
        {
            Fold => "fold",
            Check => "check",
            CallBet => "call",
            RaiseTo => "raise",
            AllIn => "allIn",
            _ => throw new ArgumentException("Unknown action: " + a.Action, nameof(a)),
        },
        To = (a.Action as RaiseTo)?.To,
        Amount = a.Amount,
        Total = a.Total,
    };

    public ActionRecord ToRecord() => new(
        Street,
        Seat,
        Kind switch
        {
            "fold" => Fold.Instance,
            "check" => Check.Instance,
            "call" => CallBet.Instance,
            "raise" => new RaiseTo(To ?? throw new InvalidDataException("A raise has no target.")),
            "allIn" => AllIn.Instance,
            _ => throw new InvalidDataException("Unknown action kind: " + Kind),
        },
        Amount,
        Total);
}

internal sealed record PotJson
{
    public required int Amount { get; init; }

    public required List<int> Seats { get; init; }
}

internal sealed record WonJson
{
    public required int Seat { get; init; }

    public required int Amount { get; init; }
}

internal sealed record FaultJson
{
    public required int Seat { get; init; }

    public required Fault Fault { get; init; }
}
