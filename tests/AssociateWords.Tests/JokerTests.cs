using Jentl.Cards;

namespace AssociateWords.Tests;

public class JokerTests
{
    // Two small categories and a joker, one card in each column: the deal is all on show but two cards.
    private static readonly AssociateLevel WithJoker = new(
        [new WordGroup("Fruit", ["apple", "pear"]), new WordGroup("Pets", ["dog", "cat"])],
        Slots: 2,
        Columns: [1, 1, 1, 1, 1],
        Moves: 40,
        Seed: 1,
        Jokers: 1);

    private static Pile? Showing(AssociateGame game, Func<Card, bool> card) => game.Columns.FirstOrDefault(c => c.Top is { } top && card(top.Card));

    /// <summary>A deal showing the Fruit card, the joker and both fruits in the columns.</summary>
    private static AssociateGame FruitOnShow()
    {
        for (var seed = 1; seed < 5000; seed++)
        {
            var game = new AssociateGame(WithJoker with { Seed = seed });
            if (Showing(game, c => AssociateGame.IsCategory(c) && AssociateGame.GroupOf(c) == "Fruit") is not null
                && Showing(game, AssociateGame.IsJoker) is not null
                && Showing(game, c => c.Face.Label == "apple") is not null
                && Showing(game, c => c.Face.Label == "pear") is not null)
            {
                return game;
            }
        }

        throw new InvalidOperationException("No deal shows them all.");
    }

    [Fact]
    public void ADeal_HasItsJokers() =>
        new AssociateGame(WithJoker).Table.Piles.SelectMany(p => p.Cards).Count(c => AssociateGame.IsJoker(c.Card)).Should().Be(1);

    [Fact]
    public void AJoker_CantOpenASlot_ButGoesIntoAnOpenOne()
    {
        var game = FruitOnShow();
        var joker = Showing(game, AssociateGame.IsJoker)!;
        var slot = game.Slots[0];

        game.Fits(joker, 0, slot).Should().BeFalse("a slot opens with its category card");
        game.Game.Play(new DragCards(Showing(game, AssociateGame.IsCategory)!, 0, slot)).Should().NotBeNull();
        game.Fits(joker, 0, slot).Should().BeTrue();
        game.Game.Play(new DragCards(joker, 0, slot)).Should().NotBeNull();

        game.Progress(slot).Should().Be((2, 3), "the joker counts as one of the words");
    }

    [Fact]
    public void NothingGoesOnAJoker_AndAJokerOnlyIntoAnEmptyColumn()
    {
        var game = FruitOnShow();
        var joker = Showing(game, AssociateGame.IsJoker)!;
        var apple = Showing(game, c => c.Face.Label == "apple")!;

        game.Fits(apple, 0, joker).Should().BeFalse();
        game.Fits(joker, 0, apple).Should().BeFalse();
    }

    [Fact]
    public void ACategoryFinishedWithAJoker_LeavesAWordOver_ThatGoesToTheFinishedPile()
    {
        var game = FruitOnShow();
        var slot = game.Slots[0];
        game.Game.Play(new DragCards(Showing(game, c => AssociateGame.IsCategory(c) && AssociateGame.GroupOf(c) == "Fruit")!, 0, slot));
        game.Game.Play(new DragCards(Showing(game, AssociateGame.IsJoker)!, 0, slot));
        var completed = game.Game.Play(new DragCards(Showing(game, c => c.Face.Label == "apple")!, 0, slot));

        completed!.Has("complete").Should().BeTrue("the category card, the joker and apple make three");
        game.IsFinished("Fruit").Should().BeTrue();
        slot.IsEmpty.Should().BeTrue();

        var pear = Showing(game, c => c.Face.Label == "pear")!;
        game.Fits(pear, 0, game.Table[AssociateGame.Done]).Should().BeTrue("its category is done");
        game.Hints(game.Table).First().Should().BeOfType<DragCards>().Which.To.Name.Should().Be(AssociateGame.Done, "the best move");
        game.Game.Play(new ClickPile(pear, 0))!.Has("complete").Should().BeTrue("a tap sends it there");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DealsWithJokers_AreWonByTheAutoplayer(int seed)
    {
        var level = WithJoker with { Seed = seed, Moves = 10_000 };
        AssociateAutoplay.Play(new AssociateGame(level)).Should().NotBeNull();
    }

    [Fact]
    public void Levels_ReadTheirJokers()
    {
        var level = AssociateLevels.FromJson("""
            { "levels": [ { "slots": 2, "columns": [1, 1, 1, 1, 1], "moves": 30, "seed": 4, "jokers": 2,
              "groups": [ { "name": "Fruit", "words": ["apple", "pear"] }, { "name": "Pets", "words": ["dog", "cat"] } ] } ] }
            """)[0];

        level.Jokers.Should().Be(2);
        level.CardCount.Should().Be(8);
    }
}
