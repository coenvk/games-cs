using Jentl.Cards;

namespace AssociateWords.Tests;

public class AssociateGameTests
{
    private static readonly AssociateLevel Small = new(
        [new WordGroup("Fruit", ["apple", "pear"]), new WordGroup("Pets", ["dog", "cat"]), new WordGroup("Colours", ["red", "blue"])],
        Slots: 2,
        Columns: [1, 1, 1, 1, 1],
        Moves: 40,
        Seed: 1);

    private static IReadOnlyList<AssociateLevel> Levels()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "Games.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return AssociateLevels.FromJson(File.ReadAllText(Path.Combine(folder, "apps", "AssociateWords", "Assets", "associatewords", "levels.json")));
    }

    [Fact]
    public void TheDeal_FillsFiveColumns_TopCardsUp_TheRestInTheDrawPile()
    {
        var game = new AssociateGame(Small);

        game.Columns.Should().HaveCount(5).And.OnlyContain(c => c.Count == 1 && c.Top!.Value.FaceUp);
        game.Table[AssociateGame.Stock].Count.Should().Be(9 - 5);
        game.Table[AssociateGame.Stock].Cards.Should().OnlyContain(c => !c.FaceUp);
        game.Slots.Should().HaveCount(2).And.OnlyContain(s => s.IsEmpty);
        game.MovesLeft.Should().Be(40);
    }

    [Fact]
    public void Drawing_TurnsOneCard_AndTheWasteGoesBackOver()
    {
        var game = new AssociateGame(Small);
        var stock = game.Table[AssociateGame.Stock];
        var waste = game.Table[AssociateGame.Waste];

        for (var i = 0; i < 4; i++)
        {
            game.Game.Play(new ClickPile(stock, stock.Count - 1))!.Has("draw").Should().BeTrue();
        }

        (stock.Count, waste.Count).Should().Be((0, 4));
        game.Game.Play(new ClickPile(stock, -1))!.Has("recycle").Should().BeTrue();
        (stock.Count, waste.Count).Should().Be((4, 0));
        stock.Cards.Should().OnlyContain(c => !c.FaceUp);
        game.MovesUsed.Should().Be(5);
        game.Game.Undo();
        (stock.Count, waste.Count, game.MovesUsed).Should().Be((0, 4, 4));
    }

    [Fact]
    public void ASlot_OpensWithItsCategoryCard_TakesOnlyItsWords_AndClearsWhenFull()
    {
        var game = new AssociateGame(Small);
        var slot = game.Slots[0];
        var played = 0;

        // Play the whole level by hints, checking the slots' rules on every turn.
        while (!game.IsCleared && played++ < 200)
        {
            foreach (var open in game.Slots.Where(s => !s.IsEmpty))
            {
                open.Cards.Should().OnlyContain(c => AssociateGame.GroupOf(c.Card) == AssociateGame.OpenIn(open));
                open.Cards.Should().Contain(c => AssociateGame.IsCategory(c.Card), "a slot is opened by its category card");
            }

            var turn = game.Game.Play(game.Hints(game.Table).First());
            turn.Should().NotBeNull();
            if (turn!.Has("complete"))
            {
                game.Table[AssociateGame.Done].Count.Should().Be(game.Completed * 3);
            }
        }

        game.IsCleared.Should().BeTrue();
        game.Completed.Should().Be(3);
        slot.IsEmpty.Should().BeTrue();
        game.Stars.Should().BeInRange(1, 3);
        game.Bonus.Should().Be(3 * AssociateGame.Reward, "a move back for each category finished");
        game.MovesLeft.Should().Be(game.Level.Moves + game.Bonus - game.MovesUsed);
    }

    [Fact]
    public void AWord_GoesOnlyOnItsOwnKind_AndAnythingInAnEmptyColumn()
    {
        var game = new AssociateGame(Small);
        foreach (var from in game.Columns)
        {
            foreach (var to in game.Columns.Where(c => c != from))
            {
                var same = AssociateGame.GroupOf(from.Top!.Value.Card) == AssociateGame.GroupOf(to.Top!.Value.Card);
                game.Fits(from, 0, to).Should().Be(same);
            }

            foreach (var slot in game.Slots)
            {
                game.Fits(from, 0, slot).Should().Be(AssociateGame.IsCategory(from.Top!.Value.Card), "only a category card opens an empty slot");
            }
        }
    }

    [Fact]
    public void RunningOutOfMoves_LosesTheLevel()
    {
        var game = new AssociateGame(Small with { Moves = 3 });
        var stock = game.Table[AssociateGame.Stock];

        for (var i = 0; i < 3; i++)
        {
            game.Game.Play(new ClickPile(stock, stock.Count - 1));
        }

        game.IsLost.Should().BeTrue();
        game.Game.Play(new ClickPile(stock, stock.Count - 1)).Should().BeNull();
        game.Hints(game.Table).Should().BeEmpty();
        game.Game.Undo();
        game.IsLost.Should().BeFalse("undo gives the move back");
    }

    [Fact]
    public void ARunOfOneCategory_MovesTogether_AndTurnsOverTheCardUnder()
    {
        // Two columns of two, face down under face up: put a word on its kind, then move that run of two.
        var level = new AssociateLevel(
            [new WordGroup("Fruit", ["apple", "pear", "plum"]), new WordGroup("Pets", ["dog", "cat", "fish"])],
            Slots: 1,
            Columns: [2, 2, 2, 1, 1],
            Moves: 50,
            Seed: 3);
        for (var seed = 1; seed < 200; seed++)
        {
            var game = new AssociateGame(level with { Seed = seed });
            var pair = game.Columns.SelectMany(a => game.Columns.Where(b => b != a && a.Count == 2 && b.Count == 2
                && AssociateGame.GroupOf(a.Top!.Value.Card) == AssociateGame.GroupOf(b.Top!.Value.Card)).Select(b => (From: a, To: b))).FirstOrDefault();
            if (pair.From is null)
            {
                continue;
            }

            var turn = game.Game.Play(new DragCards(pair.From, 1, pair.To));

            turn!.Has("reveal").Should().BeTrue();
            pair.From.Top!.Value.FaceUp.Should().BeTrue();
            game.CanTake(pair.To, 1).Should().BeTrue("the two of a kind on top move as one");
            return;
        }

        Assert.Fail("No deal had two columns of two with tops of one kind.");
    }

    [Fact]
    public void EveryLevel_IsWonByTheAutoplayer_WithinItsMoves()
    {
        var levels = Levels();

        levels.Should().HaveCount(15);
        foreach (var (level, number) in levels.Select((l, i) => (l, i + 1)))
        {
            var moves = AssociateAutoplay.Play(new AssociateGame(level));
            moves.Should().NotBeNull($"level {number}");
            moves!.Value.Should().BeLessThanOrEqualTo(level.Moves, $"level {number}");
        }
    }

    [Theory]
    [InlineData("""{ "levels": [ { "slots": 2, "columns": [1,1,1,1,1], "moves": 9, "seed": 1, "groups": [ { "name": "A", "words": ["x", "y"] }, { "name": "B", "words": ["x", "z"] } ] } ] }""", "Level 1: 'x' is on two cards*")]
    [InlineData("""{ "levels": [ { "slots": 3, "columns": [1,1,1,1,1], "moves": 9, "seed": 1, "groups": [ { "name": "A", "words": ["x", "y"] }, { "name": "B", "words": ["p", "z"] } ] } ] }""", "Level 1 needs at least two categories and 1 to 4 slots*")]
    [InlineData("""{ "levels": [ { "slots": 1, "columns": [1,1,1,1], "moves": 9, "seed": 1, "groups": [ { "name": "A", "words": ["x", "y"] }, { "name": "B", "words": ["p", "z"] } ] } ] }""", "Level 1 needs 5 columns*")]
    [InlineData("""{ "levels": [ { "slots": 1 } ] }""", "The levels file can't be read*")]
    public void BadLevels_SayWhatIsWrong(string json, string message)
    {
        var act = () => AssociateLevels.FromJson(json);

        act.Should().Throw<InvalidDataException>().WithMessage(message);
    }
}
