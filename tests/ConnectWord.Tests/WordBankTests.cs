namespace ConnectWord.Tests;

public class WordBankTests
{
    private static readonly WordLevel Small = new(
    [
        new WordCategory("Fruit", ["apple", "pear", "plum"]),
        new WordCategory("Colours", ["red", "blue", "green"]),
        new WordCategory("Pets", ["dog", "cat", "fish"]),
    ]);

    public static TheoryData<string> Languages => ["levels.json", "levels.nl.json"];

    private static IReadOnlyList<WordLevel> Levels(string file)
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "Games.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return WordLevels.FromJson(File.ReadAllText(Path.Combine(folder, "apps", "ConnectWord", "Assets", "connectword", file)));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguage_HasItsTwentyLevels(string file) => Levels(file).Should().HaveCount(20);

    [Theory]
    [MemberData(nameof(Languages))]
    public void GeneratedPuzzles_AreFair_AndTheSameForASeed(string file)
    {
        var levels = Levels(file);
        for (var seed = 1; seed <= 300; seed++)
        {
            var level = WordBank.Generate(levels, seed);

            level.Categories.Count.Should().BeInRange(3, 5);
            level.Categories.Should().OnlyContain(c => c.Words.Count >= 3 && c.Words.Count <= 6);

            // No word of one category is any word of another's full list, used or not.
            var full = WordBank.Categories(levels).ToDictionary(c => c.Name, c => c.Words.ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var category in level.Categories)
            {
                level.Categories.Where(o => o != category).SelectMany(o => full[o.Name]).Should().NotIntersectWith(full[category.Name]);
            }
        }

        WordBank.Generate(levels, 42).Should().BeEquivalentTo(WordBank.Generate(levels, 42), o => o.WithStrictOrdering());
    }

    [Fact]
    public void DailySeed_IsTheDate() => WordBank.DailySeed(new DateOnly(2026, 11, 12)).Should().Be(20261112);

    [Fact]
    public void Undone_TakesTheLastMoveBack_DealingTheSame()
    {
        var game = new ConnectWordGame(Small, seed: 3, columns: 3, rows: 3);
        var before = string.Join(" ", game.Cards.Select(c => $"{c.Cell}:{c.Card}"));
        var (first, second) = (game.Cards.First().Cell, game.Cards.Last().Cell);

        game.Drop(first, second);
        var undone = game.Undone();

        string.Join(" ", undone.Cards.Select(c => $"{c.Cell}:{c.Card}")).Should().Be(before);
        undone.Moves.Should().Be(0);
        undone.History.Should().BeEmpty();
        undone.Undone().Should().BeSameAs(undone);
    }
}
