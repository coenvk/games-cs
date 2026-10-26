using System.Text.Json.Serialization;
using Jentl.Collect;

namespace GameServer.Poker;

/// <summary>
/// What a bot has done on the ladder. <paramref name="NetChips"/> is in chips at blinds 1/2 (a big blind is 2 chips), so
/// bb/100 = NetChips / 2 / Hands × 100. <paramref name="SumSquares"/> is the sum of the squares of each hand's result
/// in chips, kept so the spread of the per-hand result can be derived across matches without keeping every hand.
/// </summary>
public sealed record BotRecord(
    string Bot,
    GlickoRating Rating,
    int Matches,
    int Won,
    int Drawn,
    int Lost,
    long Hands,
    long NetChips,
    double SumSquares,
    DateTimeOffset? LastPlayed);

/// <summary>
/// A finished ranked match: <paramref name="ScoreA"/> is 1 when A won, 0.5 for a draw, 0 when B won, and
/// <paramref name="NetPerHandA"/> is what A won (negative: lost) in chips on each hand; B's result is the opposite.
/// </summary>
public sealed record RankedOutcome(string BotA, string BotB, double ScoreA, long HandsPlayed, IReadOnlyList<int> NetPerHandA);

/// <summary>
/// One line of the ranking. <paramref name="Range"/> is twice the rating deviation. <paramref name="Place"/> is null for a
/// provisional bot (deviation above 100), which is listed after the established ones. <paramref name="Margin"/> is the
/// 95% margin of <paramref name="BbPer100"/>.
/// </summary>
public sealed record RankingRow(
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
/// The Glicko-2 rating and results of every bot, kept in <c>ratings.json</c> so they outlive a restart. A ranked match
/// is one rating period for both bots, each updated from the other's rating as it was before the match. Thread-safe.
/// </summary>
public sealed class Ratings
{
    private const string RatingsFile = "ratings.json";
    private const double BigBlind = 2;
    private const double EstablishedDeviation = 100;

    private readonly object _lock = new();
    private readonly PokerData _data;
    private readonly Registry _registry;
    private readonly Dictionary<string, BotRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public Ratings(PokerData data, Registry registry)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        foreach (var record in _data.Read(RatingsFile, RatingsJson.Default.ListBotRecord) ?? [])
        {
            _records[record.Bot] = record;
        }
    }

    /// <summary>What is known of a bot; a bot that has not played has the starting rating.</summary>
    public BotRecord Of(string bot)
    {
        lock (_lock)
        {
            return Get(bot);
        }
    }

    /// <summary>Takes a finished ranked match into both bots' ratings and results, and saves at once.</summary>
    public void Record(RankedOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_lock)
        {
            var a = Get(outcome.BotA);
            var b = Get(outcome.BotB);
            var now = _registry.Now;
            _records[a.Bot] = Apply(a, b.Rating, outcome.ScoreA, outcome.NetPerHandA, 1, now);
            _records[b.Bot] = Apply(b, a.Rating, 1 - outcome.ScoreA, outcome.NetPerHandA, -1, now);
            _data.Write(RatingsFile, [.. _records.Values.OrderBy(r => r.Bot, StringComparer.OrdinalIgnoreCase)], RatingsJson.Default.ListBotRecord);
        }
    }

    /// <summary>
    /// The ranking: established bots (deviation 100 or less) by rating with places 1, 2, ..., then provisional bots by
    /// rating without a place. Banned bots, and bots of banned owners, are left out.
    /// </summary>
    public IReadOnlyList<RankingRow> Ranking()
    {
        List<BotRecord> records;
        lock (_lock)
        {
            records = [.. _records.Values];
        }

        var rows = new List<(BotRecord Record, string Owner)>();
        foreach (var record in records)
        {
            var account = _registry.Bot(record.Bot);
            if (account is null || account.Banned)
            {
                continue;
            }

            rows.Add((record, _registry.OwnerById(account.OwnerId)?.Name ?? string.Empty));
        }

        var ordered = rows
            .OrderBy(r => Provisional(r.Record))
            .ThenByDescending(r => r.Record.Rating.Rating)
            .ThenBy(r => r.Record.Bot, StringComparer.OrdinalIgnoreCase);
        var place = 0;
        return [.. ordered.Select(r => ToRow(r.Record, r.Owner, Provisional(r.Record) ? null : ++place))];
    }

    private BotRecord Get(string bot) => _records.TryGetValue(bot, out var record) ? record : NewRecord(_registry.Bot(bot)?.Name ?? bot);

    private static BotRecord NewRecord(string bot) => new(bot, GlickoRating.New, 0, 0, 0, 0, 0, 0, 0, null);

    private static bool Provisional(BotRecord record) => record.Rating.Deviation > EstablishedDeviation;

    private static BotRecord Apply(BotRecord record, GlickoRating opponent, double score, IReadOnlyList<int> netPerHandA, int sign, DateTimeOffset now)
    {
        long net = 0;
        double squares = 0;
        foreach (var chips in netPerHandA)
        {
            net += sign * chips;
            squares += (double)chips * chips;
        }

        return record with
        {
            Rating = Glicko2.Update(record.Rating, [new GlickoResult(opponent, score)]),
            Matches = record.Matches + 1,
            Won = record.Won + (score > 0.5 ? 1 : 0),
            Drawn = record.Drawn + (score == 0.5 ? 1 : 0),
            Lost = record.Lost + (score < 0.5 ? 1 : 0),
            Hands = record.Hands + netPerHandA.Count,
            NetChips = record.NetChips + net,
            SumSquares = record.SumSquares + squares,
            LastPlayed = now,
        };
    }

    private static RankingRow ToRow(BotRecord r, string owner, int? place)
    {
        var bbPer100 = 0.0;
        var margin = 0.0;
        if (r.Hands > 0)
        {
            var mean = (double)r.NetChips / r.Hands;
            bbPer100 = mean / BigBlind * 100;
            if (r.Hands > 1)
            {
                var variance = Math.Max(0, (r.SumSquares - (r.Hands * mean * mean)) / (r.Hands - 1));
                margin = 1.96 * Math.Sqrt(variance) / BigBlind / Math.Sqrt(r.Hands) * 100;
            }
        }

        return new RankingRow(place, r.Bot, owner, r.Rating.Rating, 2 * r.Rating.Deviation, place is null, r.Matches, r.Won, r.Drawn, r.Lost, bbPer100, margin, r.LastPlayed);
    }
}

/// <summary>The JSON of <c>ratings.json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<BotRecord>))]
internal sealed partial class RatingsJson : JsonSerializerContext;
