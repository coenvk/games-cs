using System.Text.Json;

namespace ConnectWord;

/// <summary>A category of a level: its name (shown once it is matched) and the words that belong to it.</summary>
public sealed record WordCategory(string Name, IReadOnlyList<string> Words);

/// <summary>A level: the categories to find, each word in exactly one of them.</summary>
public sealed record WordLevel(IReadOnlyList<WordCategory> Categories)
{
    public int WordCount => Categories.Sum(c => c.Words.Count);
}

/// <summary>Reads the levels file: <c>{ "levels": [ { "categories": [ { "name": "...", "words": [...] } ] } ] }</c>.</summary>
public static class WordLevels
{
    public const int MinWords = 3;
    public const int MaxWords = 8;
    public const int MinCategories = 3;
    public const int MaxCategories = 6;

    /// <exception cref="InvalidDataException">The file isn't levels, or a level breaks a rule (saying which and why).</exception>
    public static IReadOnlyList<WordLevel> FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var levels = document.RootElement.GetProperty("levels").EnumerateArray()
                .Select(level => new WordLevel([.. level.GetProperty("categories").EnumerateArray().Select(category => new WordCategory(
                    category.GetProperty("name").GetString() ?? "",
                    [.. category.GetProperty("words").EnumerateArray().Select(word => word.GetString() ?? "")]))]))
                .ToList();
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

    /// <summary>Throws when <paramref name="level"/> can't be played fairly: sizes out of range, or a word in two places.</summary>
    public static void Check(WordLevel level, int number)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Categories.Count is < MinCategories or > MaxCategories)
        {
            throw new InvalidDataException($"Level {number} has {level.Categories.Count} categories; it needs {MinCategories} to {MaxCategories}.");
        }

        foreach (var category in level.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Name) || category.Words.Count is < MinWords or > MaxWords)
            {
                throw new InvalidDataException($"Level {number}: the category '{category.Name}' needs a name and {MinWords} to {MaxWords} words.");
            }
        }

        var twice = level.Categories.SelectMany(c => c.Words).GroupBy(w => w.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
        {
            throw new InvalidDataException($"Level {number}: '{twice.Key}' is in two places; every word must be in one category only.");
        }
    }
}
