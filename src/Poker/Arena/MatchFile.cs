using System.Globalization;
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
    IReadOnlyList<HandRecord> Hands)
{
    /// <summary>
    /// The single match played at <paramref name="table"/> so far: one result per seat, in big blinds won per 100
    /// hands at the starting blinds, with no margin, a gone count of 0 or 1 and the seat's place when it has one.
    /// </summary>
    public static MatchResult FromTable(ArenaTable table, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(table);
        var settings = table.Settings;
        var bigBlind = settings.SmallBlind * 2.0;
        var results = table.Results();
        return new MatchResult(
            settings,
            null,
            at,
            [.. results.Select(r => r.Name)],
            [.. results.Select(r => new BotResult(
                r.Name,
                r.Hands == 0 ? 0 : r.Net / bigBlind * 100 / r.Hands,
                0,
                r.Hands,
                r.Late,
                r.Illegal,
                r.Gone ? 1 : 0,
                r.Place is { } place ? [place] : []))],
            [.. table.Hands]);
    }
}

/// <summary>
/// Saves a <see cref="MatchResult"/> as JSON and reads it back. Property names are camelCase and enums are written by
/// name (for example "Preflop"). Cards are written as their codes ("As"), a move as <c>{ "kind": "raise", "to": 60 }</c> (the kinds are fold,
/// check, call, raise and allIn), every seed as its 32 hexadecimal digits and every time limit as whole milliseconds.
/// </summary>
public static class MatchFile
{
    /// <summary>The match as JSON text.</summary>
    public static string Write(MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return JsonSerializer.Serialize(MatchJson.From(match), MatchJsonContext.Default.MatchJson);
    }

    /// <summary>
    /// The name of a match's file without its extension: when it was played, in local time, as <c>yyyyMMdd-HHmmss</c>, so
    /// that every match sorts by when it was played whatever clock dated it.
    /// </summary>
    public static string Name(MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.At.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the match into <paramref name="folder"/> (made if need be) as a new file named after its time,
    /// <c><see cref="Name"/>.json</c>, with <c>-2</c>, <c>-3</c> and so on added when that name is taken; an existing file
    /// is never overwritten. Returns the file's path.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be written to.</exception>
    public static string Save(string folder, MatchResult match) => Save(folder, match, static (file, json) =>
    {
        using var writer = new StreamWriter(file);
        writer.Write(json);
    });

    /// <summary>
    /// <see cref="Save(string, MatchResult)"/> with the writing of the text into the new file done by
    /// <paramref name="write"/>; a file it fails to write is deleted again, so no partial match file is left.
    /// </summary>
    internal static string Save(string folder, MatchResult match, Action<Stream, string> write)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(match);
        Directory.CreateDirectory(folder);
        var json = Write(match);
        var stem = Name(match);
        for (var number = 1; ; number++)
        {
            var path = Path.Combine(folder, number == 1 ? $"{stem}.json" : $"{stem}-{number}.json");
            FileStream file;
            try
            {
                // CreateNew fails on a taken name, so two saves at once cannot both have it.
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            try
            {
                using (file)
                {
                    write(file, json);
                }

                return path;
            }
            catch
            {
                // The disk filled up, say: the file made for this match goes again, and the failure is passed on.
                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }

                throw;
            }
        }
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
        catch (Exception e) when (e is JsonException or ArgumentException or FormatException or InvalidCastException or InvalidOperationException)
        {
            throw new InvalidDataException("The file is not a match file: " + e.Message, e);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, RespectNullableAnnotations = true)]
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
            Seed = m.Settings.Seed.ToString(),
            TimeLimitMs = (long)m.Settings.TimeLimit.TotalMilliseconds,
            StartingStack = m.Settings.StartingStack,
            SmallBlind = m.Settings.SmallBlind,
            StackBigBlinds = m.Settings.StackBigBlinds,
        },
        Batch = m.Batch is null ? null : new BatchJson
        {
            Format = m.Batch.Format,
            Hands = m.Batch.Hands,
            Seed = m.Batch.Seed.ToString(),
            TimeLimitMs = (long)m.Batch.TimeLimit.TotalMilliseconds,
            Parallel = m.Batch.Parallel,
        },
        At = m.At,
        Bots = [.. m.Bots],
        Results = [.. m.Results],
        Hands = [.. m.Hands.Select(HandJson.From)],
    };

    public MatchResult ToResult() => new(
        new ArenaSettings(Settings.Format, Settings.Hands, DealSeed.Parse(Settings.Seed), TimeSpan.FromMilliseconds(Settings.TimeLimitMs), Settings.StartingStack, Settings.SmallBlind, Settings.StackBigBlinds),
        Batch is null ? null : new BatchSettings(Batch.Format, Batch.Hands, DealSeed.Parse(Batch.Seed), TimeSpan.FromMilliseconds(Batch.TimeLimitMs), Batch.Parallel),
        At,
        Bots,
        Results,
        [.. Hands.Select(h => h.ToRecord())]);
}

internal sealed record SettingsJson
{
    public required TableFormat Format { get; init; }

    public required int Hands { get; init; }

    public required string Seed { get; init; }

    public required long TimeLimitMs { get; init; }

    public required int StartingStack { get; init; }

    public required int SmallBlind { get; init; }

    public required int StackBigBlinds { get; init; }
}

internal sealed record BatchJson
{
    public required BatchFormat Format { get; init; }

    public required int Hands { get; init; }

    public required string Seed { get; init; }

    public required long TimeLimitMs { get; init; }

    public required bool Parallel { get; init; }
}

internal sealed record HandJson
{
    public required int Number { get; init; }

    public required string Seed { get; init; }

    public required int Button { get; init; }

    public required int SmallBlind { get; init; }

    public required int BigBlind { get; init; }

    public required int SmallBlindSeat { get; init; }

    public required int BigBlindSeat { get; init; }

    public required int SmallBlindPosted { get; init; }

    public required int BigBlindPosted { get; init; }

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
        Seed = h.Seed.ToString(),
        Button = h.Button,
        SmallBlind = h.SmallBlind,
        BigBlind = h.BigBlind,
        SmallBlindSeat = h.SmallBlindSeat,
        BigBlindSeat = h.BigBlindSeat,
        SmallBlindPosted = h.SmallBlindPosted,
        BigBlindPosted = h.BigBlindPosted,
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

    public HandRecord ToRecord()
    {
        Validate();
        return Build();
    }

    private HandRecord Build() => new(
        Number,
        DealSeed.Parse(Seed),
        Button,
        SmallBlind,
        BigBlind,
        SmallBlindSeat,
        BigBlindSeat,
        SmallBlindPosted,
        BigBlindPosted,
        Names,
        StartStacks,
        [.. Holes.Select(c => (IReadOnlyList<Jentl.Cards.Card>)[.. c.Select(BotView.Card)])],
        [.. Actions.Select(a => a.ToRecord())],
        [.. Board.Select(BotView.Card)],
        [.. Pots.Select(p => new Pot(p.Amount, p.Seats))],
        Won.ToDictionary(w => w.Seat, w => w.Amount),
        new HashSet<int>(Shown),
        [.. Faults.Select(f => (f.Seat, f.Fault))]);

    private void Validate()
    {
        var seats = Names.Count;
        if (seats < 2 || StartStacks.Count != seats || Holes.Count != seats)
        {
            throw new InvalidDataException($"Hand {Number}: the names, stacks and hole cards do not match.");
        }

        var all = new[] { Button, SmallBlindSeat, BigBlindSeat }
            .Concat(Actions.Select(a => a.Seat)).Concat(Shown).Concat(Won.Select(w => w.Seat))
            .Concat(Pots.SelectMany(p => p.Seats)).Concat(Faults.Select(f => f.Seat));
        if (all.Any(s => s < 0 || s >= seats))
        {
            throw new InvalidDataException($"Hand {Number}: a seat is out of range.");
        }
    }
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
