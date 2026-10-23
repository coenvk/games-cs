using Jentl.Cards;

namespace Spider.Tests;

public class SpiderRulesTests
{
    private readonly SpiderRules _rules = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    public void Deal_Lays104CardsInTheRightSuits(int suits, int expectedSuits)
    {
        var table = SpiderRules.Deal(seed: 3, suits);
        var cards = table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).ToArray();

        cards.Should().HaveCount(104).And.OnlyHaveUniqueItems();
        cards.Select(c => c.Suit).Distinct().Should().HaveCount(expectedSuits);
        cards.GroupBy(c => (c.Rank, c.Suit)).Should().OnlyContain(g => g.Count() == 8 / expectedSuits);
    }

    [Fact]
    public void Deal_LaysOutTheColumns()
    {
        var table = SpiderRules.Deal(seed: 3, suits: 4);

        Enumerable.Range(0, 10).Select(i => table[SpiderRules.Column(i)].Count).Should().Equal(6, 6, 6, 6, 5, 5, 5, 5, 5, 5);
        Enumerable.Range(0, 10).Should().OnlyContain(i => table[SpiderRules.Column(i)].Cards.Count(c => c.FaceUp) == 1
            && table[SpiderRules.Column(i)].Top!.Value.FaceUp);
        table[SpiderRules.Stock].Count.Should().Be(50);
    }

    [Fact]
    public void Deal_IsReproducibleBySeed() =>
        Snapshot(SpiderRules.Deal(9, 2)).Should().Be(Snapshot(SpiderRules.Deal(9, 2)));

    [Fact]
    public void Columns_BuildDownRegardlessOfSuit_ButOnlyOneSuitRunsMove()
    {
        var table = Layout("9S", "8H", "8S 7H", "8D 7D");

        SpiderRules.CanMove(table[SpiderRules.Column(1)], 0, table[SpiderRules.Column(0)]).Should().BeTrue("8H on 9S: any suit builds");
        SpiderRules.CanMove(table[SpiderRules.Column(2)], 0, table[SpiderRules.Column(0)]).Should().BeFalse("8S 7H is not one suit");
        SpiderRules.CanMove(table[SpiderRules.Column(3)], 0, table[SpiderRules.Column(0)]).Should().BeTrue("8D 7D is one suit");
        SpiderRules.CanMove(table[SpiderRules.Column(2)], 1, table[SpiderRules.Column(3)]).Should().BeFalse("7H does not go on 7D");
    }

    [Fact]
    public void EmptyColumns_TakeAnyRun()
    {
        var table = Layout("5C 4C", "");

        SpiderRules.CanMove(table[SpiderRules.Column(0)], 0, table[SpiderRules.Column(1)]).Should().BeTrue();
    }

    [Fact]
    public void Moving_UncoversTheCardBelow()
    {
        var table = Layout("#3D 9S", "10H");
        var game = new CardGame(table, _rules);

        game.Play(new DragCards(table[SpiderRules.Column(0)], 1, table[SpiderRules.Column(1)]))!.Has(SpiderRules.RevealTag).Should().BeTrue();
        table[SpiderRules.Column(0)].Top!.Value.FaceUp.Should().BeTrue();
    }

    [Fact]
    public void CompletingKingToAce_RemovesTheRun_AndUndoBringsItBack()
    {
        // Column 0 holds K down to 2 of spades; dropping the ace completes it.
        var table = Layout("#5H KS QS JS 10S 9S 8S 7S 6S 5S 4S 3S 2S", "AS");
        var game = new CardGame(table, _rules);

        var turn = game.Play(new DragCards(table[SpiderRules.Column(1)], 0, table[SpiderRules.Column(0)]));

        turn!.Has(SpiderRules.CompleteTag).Should().BeTrue();
        table[SpiderRules.Completed(0)].Count.Should().Be(13);
        table[SpiderRules.Column(0)].Cards.Select(c => (c.Card.ToString(), c.FaceUp)).Should().Equal(("5H", true));

        game.Undo();
        table[SpiderRules.Completed(0)].IsEmpty.Should().BeTrue();
        table[SpiderRules.Column(0)].Count.Should().Be(13);
        table[SpiderRules.Column(0)][0].FaceUp.Should().BeFalse();
        table[SpiderRules.Column(1)].Count.Should().Be(1);
    }

    [Fact]
    public void MixedSuitKingToAce_IsNotARun()
    {
        var table = Layout("KS QS JS 10S 9S 8S 7S 6S 5S 4S 3S 2S", "AH");
        var game = new CardGame(table, _rules);

        game.Play(new DragCards(table[SpiderRules.Column(1)], 0, table[SpiderRules.Column(0)]))!.Has(SpiderRules.CompleteTag).Should().BeFalse();
        table[SpiderRules.Column(0)].Count.Should().Be(13);
    }

    [Fact]
    public void Dealing_PutsOneCardOnEveryColumn()
    {
        var table = SpiderRules.Deal(seed: 3, suits: 4);
        var game = new CardGame(table, _rules);
        var stock = table[SpiderRules.Stock];

        game.Play(new ClickPile(stock, stock.Count - 1))!.Has(SpiderRules.DealTag).Should().BeTrue();

        stock.Count.Should().Be(40);
        Enumerable.Range(0, 10).Select(i => table[SpiderRules.Column(i)].Count).Should().Equal(7, 7, 7, 7, 6, 6, 6, 6, 6, 6);
        Enumerable.Range(0, 10).Should().OnlyContain(i => table[SpiderRules.Column(i)].Top!.Value.FaceUp);
    }

    [Fact]
    public void Dealing_IsRefusedWhileAColumnIsEmpty()
    {
        var table = Layout("5C", "");
        table[SpiderRules.Stock].Deal(Card.Parse("2D"), faceUp: false);

        SpiderRules.CanDeal(table).Should().BeFalse();
        new CardGame(table, _rules).Play(new ClickPile(table[SpiderRules.Stock], 0)).Should().BeNull();
    }

    [Fact]
    public void Tapping_PrefersTheSameSuit()
    {
        var table = Layout("9H", "9S", "8S");
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[SpiderRules.Column(2)], 0));

        table[SpiderRules.Column(1)].Top!.Value.Card.ToString().Should().Be("8S");
    }

    [Fact]
    public void Hints_PreferTheSameSuit()
    {
        var table = Layout("9H", "9S", "8S");

        _rules.Hints(table).First().Should().Be(new DragCards(table[SpiderRules.Column(2)], 0, table[SpiderRules.Column(1)]));
    }

    [Fact]
    public void Won_WhenAllEightRunsAreCompleted()
    {
        var table = Layout();
        for (var i = 0; i < SpiderRules.Runs; i++)
        {
            foreach (var rank in Enumerable.Range(1, 13).Reverse())
            {
                table[SpiderRules.Completed(i)].Deal(new Card(rank, Suit.Spades, i), faceUp: true);
            }
        }

        _rules.IsWon(table).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    public void PlayingHints_KeepsEveryCard_AndUndoReturnsToTheDeal(int seed, int suits)
    {
        var table = SpiderRules.Deal(seed, suits);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);
        var seen = new HashSet<string>();

        for (var step = 0; step < 1000 && !game.IsWon && seen.Add(Snapshot(table)); step++)
        {
            if (_rules.Hints(table).FirstOrDefault() is not { } hint || game.Play(hint) is null)
            {
                break;
            }

            table.Piles.Sum(p => p.Count).Should().Be(104);
            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().OnlyHaveUniqueItems();
        }

        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>Columns by card codes, bottom first; # marks a face-down card. Missing columns are empty.</summary>
    private static Table Layout(params string[] columns)
    {
        var table = new Table();
        table.Add(SpiderRules.Stock);
        for (var i = 0; i < SpiderRules.Runs; i++)
        {
            table.Add(SpiderRules.Completed(i));
        }

        var copy = 0;
        for (var i = 0; i < SpiderRules.Columns; i++)
        {
            var pile = table.Add(SpiderRules.Column(i));
            if (i < columns.Length)
            {
                foreach (var code in columns[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var card = Card.Parse(code.TrimStart('#'));
                    pile.Deal(new Card(card.Rank, card.Suit, copy++), faceUp: !code.StartsWith('#'));
                }
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
