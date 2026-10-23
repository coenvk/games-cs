using Jentl.Cards;

namespace Easthaven.Tests;

public class EasthavenRulesTests
{
    private readonly EasthavenRules _rules = new();

    [Fact]
    public void Deal_LaysSevenColumnsOfThree_TopCardsUp_AndAStockOf31()
    {
        var table = EasthavenRules.Deal(seed: 6);
        var columns = Enumerable.Range(0, 7).Select(i => table[EasthavenRules.Column(i)]).ToArray();

        columns.Should().OnlyContain(c => c.Count == 3 && c.Cards.Count(x => x.FaceUp) == 1 && c.Top!.Value.FaceUp);
        table[EasthavenRules.Stock].Count.Should().Be(31);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void TheStock_DealsARowFaceUp_AndTheLastRowOnlyReachesThreeColumns()
    {
        var table = EasthavenRules.Deal(seed: 6);
        var game = new CardGame(table, _rules);
        var stock = table[EasthavenRules.Stock];

        for (var deal = 0; deal < 4; deal++)
        {
            game.Play(new ClickPile(stock, stock.Count - 1))!.Has(EasthavenRules.DealTag).Should().BeTrue();
        }

        stock.Count.Should().Be(3);
        Enumerable.Range(0, 7).Select(i => table[EasthavenRules.Column(i)].Count).Should().OnlyContain(n => n == 7);

        game.Play(new ClickPile(stock, stock.Count - 1));
        Enumerable.Range(0, 7).Select(i => table[EasthavenRules.Column(i)].Count).Should().Equal(8, 8, 8, 7, 7, 7, 7);
        Enumerable.Range(0, 7).Should().OnlyContain(i => table[EasthavenRules.Column(i)].Top!.Value.FaceUp);
        game.Play(new ClickPile(stock, -1)).Should().BeNull("the stock is used up");
    }

    [Fact]
    public void Runs_InAlternatingColours_MoveAsOne_AndUncoverTheCardBelow()
    {
        var table = Layout(["#4D 9C 8H 7S", "10D"]);
        var game = new CardGame(table, _rules);
        var from = table[EasthavenRules.Column(0)];

        EasthavenRules.IsRun(from, 1).Should().BeTrue();
        game.Play(new DragCards(from, 1, table[EasthavenRules.Column(1)]))!.Has(EasthavenRules.RevealTag).Should().BeTrue();

        Codes(table[EasthavenRules.Column(1)]).Should().Equal("10D", "9C", "8H", "7S");
        from[0].FaceUp.Should().BeTrue();
    }

    [Fact]
    public void OutOfOrderCards_DoNotMoveTogether()
    {
        var table = Layout(["9C 8C", "10D"]);

        EasthavenRules.IsRun(table[EasthavenRules.Column(0)], 0).Should().BeFalse("same colour");
        EasthavenRules.CanMove(table, table[EasthavenRules.Column(0)], 0, table[EasthavenRules.Column(1)]).Should().BeFalse();
    }

    [Fact]
    public void EmptyColumns_TakeAnyCardOrRun_NotJustKings()
    {
        var table = Layout(["5S 4H", ""]);

        EasthavenRules.CanMove(table, table[EasthavenRules.Column(0)], 0, table[EasthavenRules.Column(1)]).Should().BeTrue();
    }

    [Fact]
    public void Foundations_BuildUpBySuit_AndLowCardsGoHomeByThemselves()
    {
        var table = Layout(["2C", "AC 9H"]);
        var game = new CardGame(table, _rules);

        EasthavenRules.CanMove(table, table[EasthavenRules.Column(1)], 0, table[EasthavenRules.Foundation(0)]).Should().BeFalse("the 9 lies on it");
        game.Play(new DragCards(table[EasthavenRules.Column(1)], 1, table[EasthavenRules.Column(0)])).Should().BeNull("9 does not go on 2");

        var aceTable = Layout(["9H AC"]);
        _rules.AutoMove(aceTable).Should().Be(new DragCards(aceTable[EasthavenRules.Column(0)], 1, aceTable[EasthavenRules.Foundation(0)]));
    }

    [Fact]
    public void Hints_SendCardsHome_ThenUncover_ThenDeal()
    {
        var table = Layout(["#3S 8C", "9H", "AS"]);
        table[EasthavenRules.Stock].Deal(Card.Parse("4D"), faceUp: false);

        _rules.Hints(table).Should().Equal(
            new DragCards(table[EasthavenRules.Column(2)], 0, table[EasthavenRules.Foundation(0)]),
            new DragCards(table[EasthavenRules.Column(0)], 1, table[EasthavenRules.Column(1)]),
            new DragCards(table[EasthavenRules.Column(0)], 1, table[EasthavenRules.Column(3)]),
            new ClickPile(table[EasthavenRules.Stock], 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = EasthavenRules.Deal(seed);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);
        var seen = new HashSet<string>();

        for (var step = 0; step < 500 && seen.Add(Snapshot(table)) && _rules.Hints(table).FirstOrDefault() is { } hint; step++)
        {
            game.Play(hint).Should().NotBeNull();
            while (game.PlayAutoMove() is not null)
            {
            }

            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().HaveCount(52).And.OnlyHaveUniqueItems();
            table.Piles.Where(p => p.Name != EasthavenRules.Stock && !p.IsEmpty).Should().OnlyContain(p => p.Top!.Value.FaceUp);
        }

        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>Columns by card codes, bottom first; # marks a face-down card. The stock starts empty.</summary>
    private static Table Layout(string[] columns)
    {
        var table = new Table();
        table.Add(EasthavenRules.Stock);
        for (var i = 0; i < EasthavenRules.Foundations; i++)
        {
            table.Add(EasthavenRules.Foundation(i));
        }

        for (var i = 0; i < EasthavenRules.Columns; i++)
        {
            var pile = table.Add(EasthavenRules.Column(i));
            foreach (var code in i < columns.Length ? columns[i].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [])
            {
                pile.Deal(Card.Parse(code.TrimStart('#')), faceUp: !code.StartsWith('#'));
            }
        }

        return table;
    }

    private static IEnumerable<string> Codes(Pile pile) => pile.Cards.Select(c => c.Card.ToString());

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
