namespace ConnectWord;

/// <summary>
/// Puzzles beyond the made levels: categories drawn from all of a language's levels and mixed into a new level,
/// seeded, so a seed (a day's date, for the daily puzzle) always makes the same one. Two categories only go together
/// when none of their words, all of them and not just those used, are the same: a word that could be in either would
/// be unfair.
/// </summary>
public static class WordBank
{
    /// <summary>Every category of <paramref name="levels"/>, each name once.</summary>
    public static IReadOnlyList<WordCategory> Categories(IReadOnlyList<WordLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return [.. levels.SelectMany(l => l.Categories).DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A level of three to five categories from <paramref name="levels"/>, three to six words each.</summary>
    public static WordLevel Generate(IReadOnlyList<WordLevel> levels, int seed)
    {
        var random = new Random(seed);
        var bank = Categories(levels).ToArray();
        random.Shuffle(bank);
        var wanted = random.Next(3, 6);
        var chosen = new List<WordCategory>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in bank)
        {
            if (chosen.Count == wanted)
            {
                break;
            }

            var words = category.Words.Select(w => w.Trim()).ToList();
            if (words.Count < WordLevels.MinWords || words.Any(taken.Contains))
            {
                continue;
            }

            taken.UnionWith(words);
            var picked = words.ToArray();
            random.Shuffle(picked);
            chosen.Add(category with { Words = [.. picked.Take(random.Next(WordLevels.MinWords, Math.Min(6, picked.Length) + 1))] });
        }

        var level = new WordLevel(chosen);
        WordLevels.Check(level, 0);
        return level;
    }

    /// <summary>The daily puzzle's seed: the date as yyyymmdd.</summary>
    public static int DailySeed(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;
}
