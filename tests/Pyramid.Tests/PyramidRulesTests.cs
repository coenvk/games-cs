using Jentl.Cards;

namespace Pyramid.Tests;

public class PyramidRulesTests
{
    private readonly PyramidRules _rules = new();

    [Fact]
    public void Deal_BuildsTheSevenRowPyramid_AndAStockOf24()
    {
        var table = PyramidRules.Deal(seed: 5);

        Enumerable.Range(0, 7).SelectMany(r => Enumerable.Range(0, r + 1).Select(c => table[PyramidRules.Position(r, c)]))
            .Should().HaveCount(28).And.OnlyContain(p => p.Count == 1 && p.Top!.Value.FaceUp);
        table[PyramidRules.Stock].Count.Should().Be(24);
        table[PyramidRules.Stock].Cards.Should().OnlyContain(c => !c.FaceUp);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void Deal_IsReproducibleBySeed() =>
        Snapshot(PyramidRules.Deal(11)).Should().Be(Snapshot(PyramidRules.Deal(11)));

    [Fact]
    public void OnlyCardsWithBothCoveringCardsGone_AreAvailable()
    {
        var table = Bottom("KC", "KH");
        var above = table[PyramidRules.Position(5, 0)];
        above.Deal(Card.Parse("QD"), faceUp: true);
        var game = new CardGame(table, _rules);

        PyramidRules.IsAvailable(table, table[PyramidRules.Position(6, 1)]).Should().BeTrue("the bottom row is uncovered");
        PyramidRules.IsAvailable(table, above).Should().BeFalse();

        game.Play(new ClickPile(table[PyramidRules.Position(6, 0)], 0));
        PyramidRules.IsAvailable(table, above).Should().BeFalse("one covering card is left");

        game.Play(new ClickPile(table[PyramidRules.Position(6, 1)], 0));
        PyramidRules.IsAvailable(table, above).Should().BeTrue();
    }

    [Fact]
    public void PairsAddingUpTo13_AreRemoved_AndUndoPutsThemBack()
    {
        var table = Bottom("6H", "7S", "8D");
        var game = new CardGame(table, _rules);
        var six = table[PyramidRules.Position(6, 0)];
        var seven = table[PyramidRules.Position(6, 1)];

        game.Play(new DragCards(six, 0, table[PyramidRules.Position(6, 2)])).Should().BeNull("6 + 8 is not 13");
        game.Play(new DragCards(six, 0, seven))!.Has(PyramidRules.RemoveTag).Should().BeTrue();

        six.IsEmpty.Should().BeTrue();
        seven.IsEmpty.Should().BeTrue();
        table[PyramidRules.Removed].Count.Should().Be(2);

        game.Undo();
        six.Top!.Value.Card.Should().Be(Card.Parse("6H"));
        seven.Top!.Value.Card.Should().Be(Card.Parse("7S"));
    }

    [Fact]
    public void AKing_IsRemovedOnItsOwn_ByTapOrDrag()
    {
        var table = Bottom("KH", "KS");
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[PyramidRules.Position(6, 0)], 0)).Should().NotBeNull();
        game.Play(new DragCards(table[PyramidRules.Position(6, 1)], 0, table[PyramidRules.Removed])).Should().NotBeNull();

        table[PyramidRules.Removed].Count.Should().Be(2);
    }

    [Fact]
    public void CoveredCards_CannotPair()
    {
        var table = Bottom("2C");
        table[PyramidRules.Position(5, 0)].Deal(Card.Parse("JD"), faceUp: true);
        var game = new CardGame(table, _rules);

        game.Play(new DragCards(table[PyramidRules.Position(6, 0)], 0, table[PyramidRules.Position(5, 0)])).Should().BeNull();
    }

    [Fact]
    public void TheWasteCard_PairsWithThePyramid()
    {
        var table = Bottom("9C");
        table[PyramidRules.Stock].Deal(Card.Parse("4D"), faceUp: false);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[PyramidRules.Stock], 0))!.Has(PyramidRules.DrawTag).Should().BeTrue();
        table[PyramidRules.Waste].Top.Should().Be(new PlacedCard(Card.Parse("4D"), FaceUp: true));

        game.Play(new DragCards(table[PyramidRules.Waste], 0, table[PyramidRules.Position(6, 0)])).Should().NotBeNull();
        game.IsWon.Should().BeTrue();
    }

    [Fact]
    public void TheStock_CanBeGoneThroughThreeTimes()
    {
        var table = Bottom("QC");
        table[PyramidRules.Stock].Deal(Card.Parse("2D"), faceUp: false);
        table[PyramidRules.Stock].Deal(Card.Parse("3D"), faceUp: false);
        var game = new CardGame(table, _rules);
        var stock = table[PyramidRules.Stock];

        for (var pass = 0; pass < 3; pass++)
        {
            game.Play(new ClickPile(stock, stock.Count - 1));
            game.Play(new ClickPile(stock, stock.Count - 1));
            stock.IsEmpty.Should().BeTrue();
            if (pass < 2)
            {
                game.Play(new ClickPile(stock, -1))!.Has(PyramidRules.RedealTag).Should().BeTrue();
                stock.Cards.Select(c => c.Card.ToString()).Should().Equal("2D", "3D");
                stock.Cards.Should().OnlyContain(c => !c.FaceUp);
            }
        }

        PyramidRules.CanDrawOrRedeal(table).Should().BeFalse();
        game.Play(new ClickPile(stock, -1)).Should().BeNull();

        game.Undo();
        game.Undo();
        game.Undo();
        table.Counter(PyramidRules.RedealCounter).Should().Be(1, "undoing a redeal gives it back");
    }

    [Fact]
    public void Hints_OfferKingsAndPairs_BeforeTheStock()
    {
        var table = Bottom("3S", "KD", "10H");
        table[PyramidRules.Stock].Deal(Card.Parse("5C"), faceUp: false);

        _rules.Hints(table).Should().Equal(
            new ClickPile(table[PyramidRules.Position(6, 1)], 0),
            new DragCards(table[PyramidRules.Position(6, 0)], 0, table[PyramidRules.Position(6, 2)]),
            new ClickPile(table[PyramidRules.Stock], 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = PyramidRules.Deal(seed);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);

        for (var step = 0; step < 500 && !game.IsWon; step++)
        {
            if (_rules.Hints(table).FirstOrDefault() is not { } hint || game.Play(hint) is null)
            {
                break;
            }

            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
        }

        _rules.Hints(table).Should().BeEmpty("hints play until nothing is left to try");
        game.Restart();
        Snapshot(table).Should().Be(start);
        table.Counter(PyramidRules.RedealCounter).Should().Be(0);
    }

    /// <summary>A table with only these cards, left to right, in the bottom row; the rest of the pyramid is empty.</summary>
    private static Table Bottom(params string[] cards)
    {
        var table = new Table();
        table.Add(PyramidRules.Stock);
        table.Add(PyramidRules.Waste);
        table.Add(PyramidRules.Removed);
        for (var row = 0; row < PyramidRules.Rows; row++)
        {
            for (var column = 0; column <= row; column++)
            {
                var pile = table.Add(PyramidRules.Position(row, column));
                if (row == PyramidRules.Rows - 1 && column < cards.Length)
                {
                    pile.Deal(Card.Parse(cards[column]), faceUp: true);
                }
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
