namespace ConnectWord.Tests;

public class ConnectWordGameTests
{
    private static readonly WordLevel Small = new(
    [
        new WordCategory("Fruit", ["apple", "pear", "plum"]),
        new WordCategory("Colours", ["red", "blue", "green"]),
        new WordCategory("Pets", ["dog", "cat", "fish"]),
    ]);

    private static IEnumerable<(Cell Cell, WordCard Card)> Of(ConnectWordGame game, int category) => game.Cards.Where(c => c.Card.Category == category);

    private static string Words(ConnectWordGame game) => string.Join(" ", game.Cards.Select(c => $"{c.Cell.Column}{c.Cell.Row}:{c.Card}"));

    [Fact]
    public void TheGrid_IsDealtFromTheDeck_AndCardsSitAtTheBottom()
    {
        var game = new ConnectWordGame(Small, seed: 1, columns: 4, rows: 4);

        game.Cards.Should().HaveCount(9 - game.Solved.Sum(s => 3));
        game.DeckCount.Should().Be(0);
        game.Cards.Should().OnlyContain(c => c.Cell.Row >= 4 - game.Cards.Count(o => o.Cell.Column == c.Cell.Column));
    }

    [Fact]
    public void DroppingOnTheSameCategory_Merges_AndTheGapFillsFromAbove()
    {
        var game = new ConnectWordGame(Small, seed: 3, columns: 3, rows: 3);
        var fruit = Of(game, 0).ToList();
        var (from, to) = (fruit[0].Cell, fruit[1].Cell);
        game.DrainEvents();

        game.Drop(from, to).Should().Be(MoveKind.Merged);

        game.Moves.Should().Be(1);
        game.Cards.Count().Should().Be(8);
        game.Cards.Single(c => c.Card.Words.Count == 2).Card.Words.Should().BeEquivalentTo([fruit[0].Card.Words[0], fruit[1].Card.Words[0]]);
        game.DrainEvents().OfType<CardsMerged>().Should().ContainSingle();
        foreach (var column in Enumerable.Range(0, 3))
        {
            var rows = game.Cards.Where(c => c.Cell.Column == column).Select(c => c.Cell.Row).ToList();
            rows.Should().OnlyContain(r => r >= 3 - rows.Count, "cards fall to the bottom");
        }
    }

    [Fact]
    public void DroppingOnAnotherCategory_Swaps()
    {
        var game = new ConnectWordGame(Small, seed: 3, columns: 3, rows: 3);
        var fruit = Of(game, 0).First();
        var colour = Of(game, 1).First();

        game.Drop(fruit.Cell, colour.Cell).Should().Be(MoveKind.Swapped);

        game[colour.Cell].Should().Be(fruit.Card);
        game[fruit.Cell].Should().Be(colour.Card);
    }

    [Fact]
    public void ACategoryInOneCard_IsMatched_AndLeavesTheGrid()
    {
        var game = new ConnectWordGame(Small, seed: 5, columns: 3, rows: 3);
        game.DrainEvents();

        // Merge all three fruit into one card.
        while (!game.Solved.Contains(0))
        {
            var fruit = Of(game, 0).OrderByDescending(c => c.Card.Words.Count).ToList();
            game.Drop(fruit[1].Cell, fruit[0].Cell).Should().Be(MoveKind.Merged);
        }

        var matched = game.DrainEvents().OfType<CategoryMatched>().Should().ContainSingle().Subject;
        matched.Category.Should().Be(0);
        matched.Cards.SelectMany(c => c.Words).Should().BeEquivalentTo(["apple", "pear", "plum"]);
        Of(game, 0).Should().BeEmpty();
        game.Moves.Should().BeInRange(1, 2, "one merge is enough when the third fruit is already in that row");
    }

    [Fact]
    public void ACategorySpreadAlongOneRow_IsMatchedToo()
    {
        // Three columns, three rows: line up the colours along the bottom row by swaps, with no merging.
        var game = new ConnectWordGame(Small, seed: 8, columns: 3, rows: 3);
        for (var column = 0; column < 3 && !game.Solved.Contains(1); column++)
        {
            var target = new Cell(column, 2);
            if (game[target]?.Category == 1)
            {
                continue;
            }

            var colour = Of(game, 1).First(c => c.Cell.Row != 2 || c.Cell.Column > column);
            game.Drop(colour.Cell, target).Should().Be(game[target] is null ? MoveKind.Moved : MoveKind.Swapped);
        }

        game.Solved.Should().Contain(1);
        game.DrainEvents().OfType<CategoryMatched>().Should().Contain(m => m.Category == 1 && m.Cards.Count == 3);
    }

    [Fact]
    public void MatchingEveryCategory_WinsTheLevel_WithStarsForFewMoves()
    {
        var game = new ConnectWordGame(Small, seed: 2);
        while (!game.IsWon)
        {
            var (from, to) = game.Hint()!.Value;
            game.Drop(from, to).Should().Be(MoveKind.Merged);
        }

        game.Moves.Should().BeLessThanOrEqualTo(game.Par);
        game.Stars.Should().Be(3);
        game.DrainEvents().OfType<LevelWon>().Should().ContainSingle().Which.Stars.Should().Be(3);
        game.Drop(new Cell(0, 0), new Cell(1, 0)).Should().Be(MoveKind.None);
    }

    [Fact]
    public void ABigLevel_DealsSixteen_AndKeepsTheRestInTheDeck()
    {
        var level = new WordLevel([.. Enumerable.Range(0, 5).Select(i => new WordCategory($"C{i}", [.. Enumerable.Range(0, 5).Select(w => $"w{i}{w}")]))]);
        var game = new ConnectWordGame(level, seed: 4);

        game.Cards.Count().Should().Be(16 - (game.Solved.Count * 5));
        game.DeckCount.Should().Be(25 - 16);

        // Merging frees a cell; a card comes in from the deck at the top.
        game.DrainEvents();
        var (from, to) = game.Hint()!.Value;
        game.Drop(from, to);
        game.DrainEvents().OfType<CardDealt>().Should().ContainSingle().Which.At.Row.Should().Be(0);
        game.DeckCount.Should().Be(25 - 17);
    }

    [Fact]
    public void EveryLevel_CanBeWonByHints()
    {
        foreach (var (level, number) in Levels().Select((l, i) => (l, i + 1)))
        {
            var game = new ConnectWordGame(level, seed: number);
            for (var moves = 0; !game.IsWon && moves < 200; moves++)
            {
                var (from, to) = game.Hint() ?? throw new InvalidOperationException($"Level {number}: no pair left.\n{Words(game)}");
                game.Drop(from, to);
            }

            game.IsWon.Should().BeTrue($"level {number}");
        }
    }

    [Fact]
    public void TheSameSeed_DealsTheSame() =>
        Words(new ConnectWordGame(Small, 7)).Should().Be(Words(new ConnectWordGame(Small, 7))).And.NotBe(Words(new ConnectWordGame(Small, 8)));

    [Fact]
    public void TheLevelsFile_IsValid()
    {
        var levels = Levels();

        levels.Should().HaveCount(20);
        levels.Should().OnlyContain(l => l.Categories.Count >= 3);
    }

    [Theory]
    [InlineData("""{ "levels": [ { "categories": [ { "name": "A", "words": ["x", "y", "z"] }, { "name": "B", "words": ["x", "q", "r"] }, { "name": "C", "words": ["s", "t", "u"] } ] } ] }""", "Level 1: 'x' is in two places*")]
    [InlineData("""{ "levels": [ { "categories": [ { "name": "A", "words": ["x", "y"] }, { "name": "B", "words": ["p", "q", "r"] }, { "name": "C", "words": ["s", "t", "u"] } ] } ] }""", "Level 1: the category 'A' needs a name and 3 to 8 words.")]
    [InlineData("""{ "levels": [ { "categories": [] } ] }""", "Level 1 has 0 categories*")]
    [InlineData("""{ "nope": 1 }""", "The levels file can't be read*")]
    public void BadLevels_SayWhatIsWrong(string json, string message)
    {
        var act = () => WordLevels.FromJson(json);

        act.Should().Throw<InvalidDataException>().WithMessage(message);
    }

    private static IReadOnlyList<WordLevel> Levels()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "Games.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return WordLevels.FromJson(File.ReadAllText(Path.Combine(folder, "apps", "ConnectWord", "Assets", "connectword", "levels.json")));
    }
}
