using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GameServer.Poker;

/// <summary>
/// The folder the poker side of the server keeps its files in: the owners and bots, the ratings, the league, and a
/// <see cref="MatchesFolder"/> with one file per match. Files are written aside and moved into place, so a crash
/// halfway never leaves half a file. It does no locking of its own: whoever owns a file serialises its own writes.
/// </summary>
public sealed class PokerData
{
    private readonly object _lock = new();
    private readonly List<string> _problems = [];
    private readonly string _folder;

    /// <param name="folder">Where the files live; it is created when it does not exist yet.</param>
    public PokerData(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = folder;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(MatchesFolder);
    }

    /// <summary>Where the finished matches are kept, one file each.</summary>
    public string MatchesFolder => Path.Combine(_folder, "matches");

    /// <summary>
    /// What went wrong while reading, for the operator: each broken file and where it was moved to. Nothing is
    /// logged yet (no logging is wired), so the server can print these at start-up.
    /// </summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_lock)
            {
                return [.. _problems];
            }
        }
    }

    /// <summary>
    /// Reads a file, or null when it is missing. A broken file is moved aside to <c>name.broken-&lt;time&gt;</c> (so the
    /// next save cannot wipe it), noted in <see cref="Problems"/>, and read as missing: the server starts rather than stops.
    /// </summary>
    public T? Read<T>(string name, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var file = Path.Combine(_folder, name);
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), info) : default;
        }
        catch (JsonException ex)
        {
            var aside = $"{file}.broken-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(file, aside, overwrite: true);
            lock (_lock)
            {
                _problems.Add($"{name} could not be read ({ex.Message}); it was moved to {Path.GetFileName(aside)} and started empty.");
            }

            return default;
        }
    }

    /// <summary>Writes a file: to a temporary file first, then moved over the old one.</summary>
    public void Write<T>(string name, T value, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var file = Path.Combine(_folder, name);
        var temporary = file + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, info));
        File.Move(temporary, file, overwrite: true);
    }
}

/// <summary>The JSON of the files <see cref="Registry"/> keeps.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<Owner>))]
[JsonSerializable(typeof(List<BotAccount>))]
internal sealed partial class RegistryJson : JsonSerializerContext;
