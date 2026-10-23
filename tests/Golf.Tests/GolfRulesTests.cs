using Jentl.Cards;

namespace Golf.Tests;

public class GolfRulesTests
{
    private readonly GolfRules _rules = new();

    [Fact]
    public void Deal_LaysSevenColumnsOfFive_AllFaceUp()
    {
        var table = GolfRules.Deal(seed: 2);

        Enumerable.Range(0, 7).Select(i => table[GolfRules.Column(i)]).Should().OnlyContain(c => c.Count == 5 && c.Cards.All(x => x.FaceUp));
        table[GolfRules.Waste].Count.Should().Be(1);
        table[GolfRules.Stock].Count.Should().Be(16);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
        Snapshot(GolfRules.Deal(2)).Should().Be(Snapshot(table));
    }

    [Theory]
    [InlineData("5H", "4S", true)]
    [InlineData("5H", "6C", true)]
    [InlineData("5H", "5S", false)]
    [InlineData("KH", "QS", false)]
    [InlineData("KH", "AS", false)]
    [InlineData("AH", "KS", false)]
    [InlineData("AH", "2S", true)]
    [InlineData("QH", "KS", true)]
    public void OnlyNeighbouringRanksPlay_AndNothingGoesOnAKing(string waste, string card, bool next) =>
        GolfRules.IsNext(Card.Parse(waste), Card.Parse(card)).Should().Be(next);

    [Fact]
    public void Playing_MovesTheTopCardToTheWaste_AndUndoReturnsIt()
    {
        var table = Table("5H", ["2C 4S"], ["9D"]);
        var game = new CardGame(table, _rules);
        var column = table[GolfRules.Column(0)];

        game.Play(new ClickPile(table[GolfRules.Column(1)], 0)).Should().BeNull("9 is not next to 5");
        game.Play(new ClickPile(column, 1))!.Has(GolfRules.PlayTag).Should().BeTrue();

        table[GolfRules.Waste].Top!.Value.Card.Should().Be(Card.Parse("4S"));
        column.Count.Should().Be(1);
        game.Undo();
        column.Top!.Value.Card.Should().Be(Card.Parse("4S"));
    }

    [Fact]
    public void OnlyTheTopCardOfAColumnCanBeDragged()
    {
        var table = Table("5H", ["4S 9C"]);
        var game = new CardGame(table, _rules);

        game.Play(new DragCards(table[GolfRules.Column(0)], 0, table[GolfRules.Waste])).Should().BeNull();
    }

    [Fact]
    public void TheStock_TurnsOverOneCard_Once()
    {
        var table = Table("5H", ["9C"]);
        table[GolfRules.Stock].Deal(Card.Parse("8D"), faceUp: false);
        var game = new CardGame(table, _rules);
        var stock = table[GolfRules.Stock];

        game.Play(new ClickPile(stock, 0))!.Has(GolfRules.DrawTag).Should().BeTrue();
        table[GolfRules.Waste].Top.Should().Be(new PlacedCard(Card.Parse("8D"), FaceUp: true));
        game.Play(new ClickPile(stock, -1)).Should().BeNull();
        game.Play(new ClickPile(table[GolfRules.Column(0)], 0)).Should().NotBeNull();
        game.IsWon.Should().BeTrue();
        GolfRules.CardsLeft(table).Should().Be(0);
    }

    [Fact]
    public void Hints_PreferAPlayThatKeepsTheRunGoing()
    {
        // 4 and 6 both go on the 5; after the 6, the 7 plays too.
        var table = Table("5H", ["4S"], ["7D 6C"]);

        _rules.Hints(table).First().Should().Be(new ClickPile(table[GolfRules.Column(1)], 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = GolfRules.Deal(seed);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);

        while (_rules.Hints(table).FirstOrDefault() is { } hint)
        {
            game.Play(hint).Should().NotBeNull();
            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
        }

        table[GolfRules.Stock].IsEmpty.Should().BeTrue("hints only run out once the stock is gone");
        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>A table with this waste card and these columns (bottom card first); the rest of the columns empty.</summary>
    private static Table Table(string waste, params string[][] columns)
    {
        var table = new Table();
        table.Add(GolfRules.Stock);
        table.Add(GolfRules.Waste).Deal(Card.Parse(waste), faceUp: true);
        for (var i = 0; i < GolfRules.Columns; i++)
        {
            var pile = table.Add(GolfRules.Column(i));
            foreach (var code in i < columns.Length ? columns[i][0].Split(' ') : [])
            {
                pile.Deal(Card.Parse(code), faceUp: true);
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
