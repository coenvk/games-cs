using System.Text;
using System.Text.Json;

namespace CodeLock;

/// <summary>How one difficulty has gone: games finished, games won, the fewest tries, and win streaks.</summary>
public sealed record LockRecord(int Played = 0, int Won = 0, int? BestTries = null, int Streak = 0, int BestStreak = 0)
{
    public static LockRecord None { get; } = new();
}

/// <summary>What a finished game changed about the record.</summary>
/// <param name="NewBest">Fewer tries than ever before (or the first win).</param>
/// <param name="NewBestStreak">The longest run of wins so far, now longer.</param>
public readonly record struct LockResult(bool NewBest, bool NewBestStreak);

/// <summary>
/// The player's records per difficulty (by name, e.g. "normal"). Only finished games count: a lock left
/// half-cracked is neither a win nor a loss. Reads and writes a small JSON file:
/// <c>{"version":1,"locks":{"normal":{"played":4,"won":3,"bestTries":5,"streak":2,"bestStreak":2}}}</c>.
/// </summary>
public sealed class LockRecords
{
    private const int Version = 1;
    private readonly Dictionary<string, LockRecord> _records = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LockRecord> All => _records;

    public LockRecord this[string difficulty] => _records.GetValueOrDefault(difficulty) ?? LockRecord.None;

    /// <summary>Counts a finished game; a loss ends the streak.</summary>
    public LockResult Record(string difficulty, bool solved, int tries)
    {
        ArgumentException.ThrowIfNullOrEmpty(difficulty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tries);
        var before = this[difficulty];
        var after = solved
            ? before with
            {
                Played = before.Played + 1,
                Won = before.Won + 1,
                BestTries = Math.Min(before.BestTries ?? int.MaxValue, tries),
                Streak = before.Streak + 1,
                BestStreak = Math.Max(before.BestStreak, before.Streak + 1),
            }
            : before with { Played = before.Played + 1, Streak = 0 };
        _records[difficulty] = after;
        return new LockResult(
            NewBest: solved && (before.BestTries is null || tries < before.BestTries),
            NewBestStreak: after.BestStreak > before.BestStreak && after.BestStreak > 1);
    }

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteStartObject("locks");
            foreach (var (difficulty, record) in _records.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(difficulty);
                w.WriteNumber("played", record.Played);
                w.WriteNumber("won", record.Won);
                if (record.BestTries is { } best)
                {
                    w.WriteNumber("bestTries", best);
                }

                w.WriteNumber("streak", record.Streak);
                w.WriteNumber("bestStreak", record.BestStreak);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads <see cref="ToJson"/>'s output; throws <see cref="InvalidDataException"/> for anything else.</summary>
    public static LockRecords FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != Version)
            {
                throw new InvalidDataException("Unknown code lock records version.");
            }

            var records = new LockRecords();
            if (root.TryGetProperty("locks", out var locks))
            {
                foreach (var entry in locks.EnumerateObject())
                {
                    var e = entry.Value;
                    int Number(string name) => e.TryGetProperty(name, out var value) ? Math.Max(0, value.GetInt32()) : 0;
                    records._records[entry.Name] = new LockRecord(
                        Number("played"),
                        Number("won"),
                        e.TryGetProperty("bestTries", out var best) ? best.GetInt32() : null,
                        Number("streak"),
                        Number("bestStreak"));
                }
            }

            return records;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Code lock records are unreadable: {e.Message}", e);
        }
    }
}
