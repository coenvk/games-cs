using Jentl.Cards;

namespace FortyThieves.Tests;

public class FortyThievesRulesTests
{
    private readonly FortyThievesRules _rules = new();

    [Fact]
    public void Deal_UsesTwoDecks_TenColumnsOfFour_AndAStockOf64()
    {
        var table = FortyThievesRules.Deal(seed: 5);
        var cards = table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).ToArray();

        Enumerable.Range(0, 10).Select(i => table[FortyThievesRules.Column(i)]).Should().OnlyContain(c => c.Count == 4 && c.Cards.All(x => x.FaceUp));
        table[FortyThievesRules.Stock].Count.Should().Be(64);
        cards.Should().HaveCount(104).And.OnlyHaveUniqueItems();
        cards.GroupBy(c => (c.Rank, c.Suit)).Should().OnlyContain(g => g.Count() == 2);
    }

    [Fact]
    public void Columns_BuildDownInSuit_OneCardAtATime()
    {
        var table = Layout(["9S", "8S", "8H", "", "7S 6S"]);

        FortyThievesRules.CanMove(table, table[FortyThievesRules.Column(1)], 0, table[FortyThievesRules.Column(0)]).Should().BeTrue();
        FortyThievesRules.CanMove(table, table[FortyThievesRules.Column(2)], 0, table[FortyThievesRules.Column(0)]).Should().BeFalse("other suit");
        FortyThievesRules.CanMove(table, table[FortyThievesRules.Column(4)], 0, table[FortyThievesRules.Column(1)]).Should().BeFalse("only the top card moves");
        FortyThievesRules.CanMove(table, table[FortyThievesRules.Column(4)], 1, table[FortyThievesRules.Column(3)]).Should().BeTrue("an empty column takes any card");
    }

    [Fact]
    public void Foundations_TakeAces_ThenTheSameSuitUp_FromEitherDeck()
    {
        var table = Layout(["AH", "2H", "AH"]);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[FortyThievesRules.Column(0)], 0))!.Has(FortyThievesRules.FoundationTag).Should().BeTrue();
        game.Play(new ClickPile(table[FortyThievesRules.Column(1)], 0)).Should().NotBeNull();
        game.Play(new ClickPile(table[FortyThievesRules.Column(2)], 0)).Should().NotBeNull("the second ace of hearts starts its own foundation");

        table[FortyThievesRules.Foundation(0)].Count.Should().Be(2);
        table[FortyThievesRules.Foundation(1)].Count.Should().Be(1);
    }

    [Fact]
    public void TheWaste_PlaysToColumns_AndTheStockIsGoneThroughOnce()
    {
        var table = Layout(["9D"]);
        table[FortyThievesRules.Stock].Deal(Card.Parse("8D"), faceUp: false);
        var game = new CardGame(table, _rules);
        var stock = table[FortyThievesRules.Stock];

        game.Play(new ClickPile(stock, 0))!.Has(FortyThievesRules.DrawTag).Should().BeTrue();
        game.Play(new ClickPile(stock, -1)).Should().BeNull();
        game.Play(new DragCards(table[FortyThievesRules.Waste], 0, table[FortyThievesRules.Column(0)])).Should().NotBeNull();
        table[FortyThievesRules.Column(0)].Count.Should().Be(2);
    }

    [Fact]
    public void Aces_GoHomeByThemselves()
    {
        var table = Layout(["5C AS"]);

        _rules.AutoMove(table).Should().Be(new DragCards(table[FortyThievesRules.Column(0)], 1, table[FortyThievesRules.Foundation(0)]));
    }

    [Fact]
    public void Hints_SendCardsHome_ThenBuild_ThenDeal()
    {
        var table = Layout(["10C 2C", "AC", "JC"]);
        table[FortyThievesRules.Foundation(0)].Deal(Card.Parse("AC"), faceUp: true);
        table[FortyThievesRules.Stock].Deal(Card.Parse("3D"), faceUp: false);

        _rules.Hints(table).Should().Equal(
            new DragCards(table[FortyThievesRules.Column(0)], 1, table[FortyThievesRules.Foundation(0)]),
            new DragCards(table[FortyThievesRules.Column(1)], 0, table[FortyThievesRules.Foundation(1)]),
            new ClickPile(table[FortyThievesRules.Stock], 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = FortyThievesRules.Deal(seed);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);
        var seen = new HashSet<string>();

        for (var step = 0; step < 600 && seen.Add(Snapshot(table)) && _rules.Hints(table).FirstOrDefault() is { } hint; step++)
        {
            game.Play(hint).Should().NotBeNull();
            while (game.PlayAutoMove() is not null)
            {
            }

            table.Piles.SelectMany(p => p.Cards).Should().HaveCount(104);
        }

        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>Columns by card codes, bottom first; each card a different copy so they stay unique.</summary>
    private static Table Layout(string[] columns)
    {
        var table = new Table();
        table.Add(FortyThievesRules.Stock);
        table.Add(FortyThievesRules.Waste);
        for (var i = 0; i < FortyThievesRules.Foundations; i++)
        {
            table.Add(FortyThievesRules.Foundation(i));
        }

        var copy = 1;
        for (var i = 0; i < FortyThievesRules.Columns; i++)
        {
            var pile = table.Add(FortyThievesRules.Column(i));
            foreach (var code in i < columns.Length ? columns[i].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [])
            {
                var card = Card.Parse(code);
                pile.Deal(new Card(card.Rank, card.Suit, copy++), faceUp: true);
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
