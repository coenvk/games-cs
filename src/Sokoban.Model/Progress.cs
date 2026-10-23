using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sokoban.Model;

/// <summary>The best results for one level.</summary>
/// <param name="BestMoves">Fewest moves in any solve.</param>
/// <param name="BestPushes">Fewest pushes in any solve (not necessarily the same solve).</param>
/// <param name="BestTime">Fastest solve.</param>
/// <param name="Solution">LURD solution of the fewest-moves solve.</param>
/// <param name="FirstSolved">When the level was first solved.</param>
public sealed record LevelRecord(
    int BestMoves,
    int BestPushes,
    TimeSpan BestTime,
    string Solution,
    DateTimeOffset FirstSolved);

/// <summary>What a solve improved on. <see cref="FirstSolve"/> is reported alone.</summary>
[Flags]
public enum Improvement
{
    None = 0,
    FirstSolve = 1,
    Moves = 2,
    Pushes = 4,
    Time = 8,
}

/// <summary>Saved progress across levels, keyed by <see cref="Level.Key"/>.</summary>
public sealed class Progress
{
    private const int CurrentVersion = 1;

    private readonly Dictionary<string, LevelRecord> _levels;

    public Progress()
        : this(new Dictionary<string, LevelRecord>())
    {
    }

    private Progress(Dictionary<string, LevelRecord> levels) => _levels = levels;

    public LevelRecord? Get(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return _levels.GetValueOrDefault(level.Key);
    }

    public bool IsSolved(Level level) => Get(level) is not null;

    public int SolvedCount(IEnumerable<Level> levels) => levels.Count(IsSolved);

    /// <summary>Index of the first unsolved level, or null when every level is solved.</summary>
    public int? FirstUnsolved(IReadOnlyList<Level> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        for (var i = 0; i < levels.Count; i++)
        {
            if (!IsSolved(levels[i]))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Records a solved session and reports what it improved.</summary>
    public Improvement Record(LevelSession session, TimeSpan time, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsSolved)
        {
            throw new InvalidOperationException("Only solved sessions can be recorded.");
        }

        var key = session.Level.Key;
        if (!_levels.TryGetValue(key, out var best))
        {
            _levels[key] = new LevelRecord(session.MoveCount, session.PushCount, time, session.ToLurd(), now);
            return Improvement.FirstSolve;
        }

        var improvement = Improvement.None;
        if (session.MoveCount < best.BestMoves)
        {
            improvement |= Improvement.Moves;
            best = best with { BestMoves = session.MoveCount, Solution = session.ToLurd() };
        }

        if (session.PushCount < best.BestPushes)
        {
            improvement |= Improvement.Pushes;
            best = best with { BestPushes = session.PushCount };
        }

        if (time < best.BestTime)
        {
            improvement |= Improvement.Time;
            best = best with { BestTime = time };
        }

        _levels[key] = best;
        return improvement;
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new ProgressData(CurrentVersion, _levels), ProgressJsonContext.Default.ProgressData);

    /// <exception cref="InvalidDataException">The JSON is malformed or from an unknown version.</exception>
    public static Progress FromJson(string json)
    {
        ProgressData? data;
        try
        {
            data = JsonSerializer.Deserialize(json, ProgressJsonContext.Default.ProgressData);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Progress file is not valid JSON.", e);
        }

        if (data is null || data.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported progress version {data?.Version}.");
        }

        return new Progress(new Dictionary<string, LevelRecord>(data.Levels));
    }
}

internal sealed record ProgressData(int Version, Dictionary<string, LevelRecord> Levels);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ProgressData))]
internal sealed partial class ProgressJsonContext : JsonSerializerContext;
