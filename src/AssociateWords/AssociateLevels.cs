using System.Text.Json;

namespace AssociateWords;

/// <summary>A category: its name (on its category card) and its words.</summary>
public sealed record WordGroup(string Name, IReadOnlyList<string> Words);

/// <summary>
/// A level: the categories, how many slots there are to open them in, how many cards each column is dealt (the rest
/// go to the draw pile), how many moves there are, and the seed of its fixed deal.
/// </summary>
public sealed record AssociateLevel(IReadOnlyList<WordGroup> Groups, int Slots, IReadOnlyList<int> Columns, int Moves, int Seed)
{
    public int CardCount => Groups.Sum(g => g.Words.Count + 1);
}

/// <summary>Reads the levels file: <c>{ "levels": [ { "slots": 3, "columns": [..5..], "moves": 60, "seed": 1, "groups": [ { "name": "...", "words": [...] } ] } ] }</c>.</summary>
public static class AssociateLevels
{
    public const int ColumnCount = 5;

    /// <exception cref="InvalidDataException">The file isn't levels, or a level breaks a rule (saying which and why).</exception>
    public static IReadOnlyList<AssociateLevel> FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var levels = document.RootElement.GetProperty("levels").EnumerateArray().Select(level => new AssociateLevel(
                [.. level.GetProperty("groups").EnumerateArray().Select(group => new WordGroup(
                    group.GetProperty("name").GetString() ?? "",
                    [.. group.GetProperty("words").EnumerateArray().Select(word => word.GetString() ?? "")]))],
                level.GetProperty("slots").GetInt32(),
                [.. level.GetProperty("columns").EnumerateArray().Select(c => c.GetInt32())],
                level.GetProperty("moves").GetInt32(),
                level.GetProperty("seed").GetInt32())).ToList();
            for (var i = 0; i < levels.Count; i++)
            {
                Check(levels[i], i + 1);
            }

            return levels;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"The levels file can't be read: {exception.Message}", exception);
        }
    }

    /// <summary>Reads a bank of categories: <c>{ "groups": [ { "name": "...", "words": [...] } ] }</c>.</summary>
    /// <exception cref="InvalidDataException">The file isn't a bank of categories.</exception>
    public static IReadOnlyList<WordGroup> GroupsFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return [.. document.RootElement.GetProperty("groups").EnumerateArray().Select(group => new WordGroup(
                group.GetProperty("name").GetString() ?? "",
                [.. group.GetProperty("words").EnumerateArray().Select(word => word.GetString() ?? "")]))];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"The categories can't be read: {exception.Message}", exception);
        }
    }

    public static void Check(AssociateLevel level, int number)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Groups.Count < 2 || level.Slots < 1 || level.Slots > 4 || level.Slots > level.Groups.Count)
        {
            throw new InvalidDataException($"Level {number} needs at least two categories and 1 to 4 slots, no more than categories.");
        }

        if (level.Columns.Count != ColumnCount || level.Columns.Any(c => c < 1) || level.Columns.Sum() > level.CardCount)
        {
            throw new InvalidDataException($"Level {number} needs {ColumnCount} columns of at least one card each, and no more cards dealt than it has.");
        }

        if (level.Groups.Any(g => string.IsNullOrWhiteSpace(g.Name) || g.Words.Count < 2) || level.Moves < 1)
        {
            throw new InvalidDataException($"Level {number}: every category needs a name and two or more words, and the level some moves.");
        }

        var names = level.Groups.Select(g => g.Name).Concat(level.Groups.SelectMany(g => g.Words)).Select(w => w.Trim());
        if (names.GroupBy(w => w, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            throw new InvalidDataException($"Level {number}: '{twice.Key}' is on two cards; every word and category name must be on one only.");
        }
    }
}
