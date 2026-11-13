namespace AssociateWords;

/// <summary>How big a made-up deal is.</summary>
public enum DealSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Deals beyond the made levels: categories drawn from a bank, dealt from a seed, with a move limit a quarter above
/// what the autoplayer needs (a deal it can't win is skipped for the next seed). Categories only go together when no
/// word or name is shared between them, so every card has one place.
/// </summary>
public static class AssociateDeals
{
    /// <summary>Every category in <paramref name="levels"/>, each name once with the most words it is given anywhere.</summary>
    public static IReadOnlyList<WordGroup> Bank(IEnumerable<AssociateLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return [.. levels.SelectMany(l => l.Groups).GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.MaxBy(x => x.Words.Count)!)];
    }

    /// <summary>A deal of a size: how many categories, words each, slots, the columns, and a joker (two in a large one).</summary>
    public static AssociateLevel Make(IReadOnlyList<WordGroup> bank, int seed, DealSize size) => size switch
    {
        DealSize.Small => Make(bank, seed, groups: 3, words: 3, slots: 2, columns: [1, 1, 2, 2, 2], jokers: 1),
        DealSize.Medium => Make(bank, seed, groups: 5, words: 4, slots: 3, columns: [2, 3, 3, 4, 4], jokers: 1),
        _ => Make(bank, seed, groups: 7, words: 5, slots: 3, columns: [4, 4, 5, 5, 6], jokers: 2),
    };

    /// <exception cref="InvalidOperationException">The bank hasn't enough categories that fit, or no deal could be won.</exception>
    public static AssociateLevel Make(IReadOnlyList<WordGroup> bank, int seed, int groups, int words, int slots, IReadOnlyList<int> columns, int jokers = 0)
    {
        ArgumentNullException.ThrowIfNull(bank);
        var random = new Random(seed);
        var candidates = bank.Where(g => g.Words.Count >= words).ToArray();
        random.Shuffle(candidates);
        var chosen = new List<WordGroup>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates)
        {
            var all = group.Words.Append(group.Name).Select(w => w.Trim()).ToList();
            if (chosen.Count == groups || all.Any(taken.Contains))
            {
                continue;
            }

            taken.UnionWith(all);
            var picked = group.Words.ToArray();
            random.Shuffle(picked);
            chosen.Add(group with { Words = [.. picked.Take(words)] });
        }

        if (chosen.Count < groups)
        {
            throw new InvalidOperationException($"The bank has only {chosen.Count} categories that fit together with {words} words, not {groups}.");
        }

        for (var deal = seed; deal < seed + 100; deal++)
        {
            var trial = new AssociateLevel(chosen, slots, columns, Moves: 10_000, Seed: deal, jokers);
            if (AssociateAutoplay.Play(new AssociateGame(trial)) is { } needed)
            {
                var level = trial with { Moves = (int)Math.Ceiling(needed * 1.25) };
                AssociateLevels.Check(level, 0);
                return level;
            }
        }

        throw new InvalidOperationException("No deal of these categories could be won.");
    }

    /// <summary>
    /// The made levels' shapes filled from another language's bank: level by level the same slots, columns and number
    /// of categories, words as many as the bank has (up to the level's), for a language without levels of its own.
    /// </summary>
    public static IReadOnlyList<AssociateLevel> Translate(IReadOnlyList<AssociateLevel> shapes, IReadOnlyList<WordGroup> bank)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        return [.. shapes.Select((shape, i) => Make(bank, seed: (i + 1) * 101, shape.Groups.Count, shape.Groups.Max(g => g.Words.Count), shape.Slots, shape.Columns))];
    }

    /// <summary>The daily deal's seed: the date as yyyymmdd.</summary>
    public static int DailySeed(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;
}
