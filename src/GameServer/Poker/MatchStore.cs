using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Poker.Arena;

namespace GameServer.Poker;

/// <summary>
/// A finished match as the lists show it. <paramref name="BotA"/> sat in seat 0 (in a practice match, the owner's bot;
/// <paramref name="BotB"/> is then the house bot). <paramref name="NetA"/> and <paramref name="NetB"/> are the chips each
/// won over the match (negative: lost), at blinds 1/2.
/// </summary>
public sealed record StoredMatch(string Id, DateTimeOffset At, bool Ranked, string BotA, string BotB, int NetA, int NetB, long Hands);

/// <summary>
/// The finished matches, ranked and practice: each in its own gzip-compressed match file, <c>matches/&lt;id&gt;.json.gz</c>,
/// and all of them listed in <c>matches.json</c>, so they outlive a restart. A file is written aside and moved into place
/// before the list names it, so a crash halfway leaves no half match. Thread-safe: matches end on their own tasks.
/// </summary>
public sealed partial class MatchStore
{
    private const string IndexFile = "matches.json";

    private readonly object _lock = new();
    private readonly PokerData _data;
    private readonly List<StoredMatch> _index;

    public MatchStore(PokerData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _index = _data.Read(IndexFile, MatchStoreJson.Default.ListStoredMatch) ?? [];
    }

    /// <summary>Keeps a heads-up match and returns its id: when it was played (UTC) and 64 random bits.</summary>
    public string Save(MatchResult match, bool ranked) => Add(match, ranked).Id;

    /// <summary>
    /// The last <paramref name="count"/> matches of <paramref name="bot"/>, newest first: its ranked matches, and its practice
    /// matches (those are its own: a house bot's name lists none). Practice matches are for the bot's owner only; whoever
    /// shows the list to others leaves them out.
    /// </summary>
    public IReadOnlyList<StoredMatch> Of(string bot, int count)
    {
        ArgumentNullException.ThrowIfNull(bot);
        lock (_lock)
        {
            return [.. _index
                .Where(m => Same(m.BotA, bot) || (m.Ranked && Same(m.BotB, bot)))
                .OrderByDescending(m => m.At)
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .Take(count)];
        }
    }

    /// <summary>The match with this id, every card in it; null when there is none (or the text is no match id).</summary>
    /// <exception cref="InvalidDataException">The match's file is broken.</exception>
    public MatchResult? Load(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!IdPattern().IsMatch(id))
        {
            return null;
        }

        var file = FileOf(id);
        if (!File.Exists(file))
        {
            return null;
        }

        using var stream = File.OpenRead(file);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return MatchFile.Read(reader.ReadToEnd());
    }

    /// <summary>Keeps a heads-up match, as <see cref="Save"/> does, and returns how the lists show it.</summary>
    internal StoredMatch Add(MatchResult match, bool ranked)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Bots.Count != 2)
        {
            throw new ArgumentException("Only heads-up matches are kept.", nameof(match));
        }

        var id = $"{match.At.UtcDateTime:yyyyMMdd-HHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}";
        var file = FileOf(id);
        var temporary = file + ".new";
        using (var stream = File.Create(temporary))
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            writer.Write(MatchFile.Write(match));
        }

        File.Move(temporary, file);
        var stored = new StoredMatch(id, match.At, ranked, match.Bots[0], match.Bots[1], Net(match, 0), Net(match, 1), match.Hands.Count);
        lock (_lock)
        {
            _index.Add(stored);
            _data.Write(IndexFile, _index, MatchStoreJson.Default.ListStoredMatch);
        }

        return stored;
    }

    private static int Net(MatchResult match, int seat) => match.Hands.Sum(hand => ArenaBatch.HandNet(hand, seat));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string FileOf(string id) => Path.Combine(_data.MatchesFolder, id + ".json.gz");

    [GeneratedRegex(@"^[0-9]{8}-[0-9]{6}-[0-9a-f]{16}\z")]
    private static partial Regex IdPattern();
}

/// <summary>The JSON of <c>matches.json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<StoredMatch>))]
internal sealed partial class MatchStoreJson : JsonSerializerContext;
