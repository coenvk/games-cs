using Jentl.Cards;

namespace Klondike.Tests;

public class KlondikeRulesTests
{
    private readonly KlondikeRules _rules = new();

    [Fact]
    public void Deal_LaysOutTheTableau()
    {
        var table = KlondikeRules.Deal(seed: 1);

        for (var i = 0; i < KlondikeRules.Columns; i++)
        {
            var column = table[KlondikeRules.Column(i)];
            column.Count.Should().Be(i + 1);
            column.Cards.Count(c => c.FaceUp).Should().Be(1);
            column.Top!.Value.FaceUp.Should().BeTrue();
        }

        table[KlondikeRules.Stock].Count.Should().Be(24);
        table[KlondikeRules.Stock].Cards.Should().OnlyContain(c => !c.FaceUp);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void Deal_IsReproducibleBySeed() =>
        Snapshot(KlondikeRules.Deal(42)).Should().Be(Snapshot(KlondikeRules.Deal(42)));

    [Fact]
    public void ClickingStock_DrawsThenRecycles()
    {
        var table = KlondikeRules.Deal(1);
        var game = new CardGame(table, _rules);
        var stock = table[KlondikeRules.Stock];
        var waste = table[KlondikeRules.Waste];
        var firstDrawn = stock.Top!.Value.Card;

        game.Play(new ClickPile(stock, stock.Count - 1))!.Has(KlondikeRules.DrawTag).Should().BeTrue();
        waste.Top.Should().Be(new PlacedCard(firstDrawn, FaceUp: true));

        while (!stock.IsEmpty)
        {
            game.Play(new ClickPile(stock, stock.Count - 1));
        }

        game.Play(new ClickPile(stock, -1))!.Has(KlondikeRules.RecycleTag).Should().BeTrue();
        waste.IsEmpty.Should().BeTrue();
        stock.Count.Should().Be(24);
        stock.Top.Should().Be(new PlacedCard(firstDrawn, FaceUp: false), "recycling turns the waste over, so the first card drawn is drawn first again");
    }

    [Fact]
    public void Foundations_StartWithAnAce_AndBuildUpBySuit()
    {
        var table = Layout(columns: ["AH", "2H", "2S"]);
        var home = table[KlondikeRules.Foundation(0)];

        KlondikeRules.CanMove(table[KlondikeRules.Column(1)], 0, home).Should().BeFalse("only an ace starts a foundation");
        Play(table, KlondikeRules.Column(0), 0, KlondikeRules.Foundation(0)).Should().BeTrue();
        KlondikeRules.CanMove(table[KlondikeRules.Column(2)], 0, home).Should().BeFalse("2S does not go on AH");
        Play(table, KlondikeRules.Column(1), 0, KlondikeRules.Foundation(0)).Should().BeTrue();
    }

    [Fact]
    public void Tableau_BuildsDownInAlternatingColours()
    {
        var table = Layout(columns: ["9S", "8H", "8C", "10H"]);

        KlondikeRules.CanMove(table[KlondikeRules.Column(1)], 0, table[KlondikeRules.Column(0)]).Should().BeTrue("8H on 9S");
        KlondikeRules.CanMove(table[KlondikeRules.Column(2)], 0, table[KlondikeRules.Column(0)]).Should().BeFalse("8C on 9S is same colour");
        KlondikeRules.CanMove(table[KlondikeRules.Column(0)], 0, table[KlondikeRules.Column(2)]).Should().BeFalse("9S does not go on a lower card");
        KlondikeRules.CanMove(table[KlondikeRules.Column(0)], 0, table[KlondikeRules.Column(3)]).Should().BeTrue("9S on 10H");
    }

    [Fact]
    public void EmptyColumns_TakeOnlyKings()
    {
        var table = Layout(columns: ["QH", "KS", ""]);

        KlondikeRules.CanMove(table[KlondikeRules.Column(0)], 0, table[KlondikeRules.Column(2)]).Should().BeFalse();
        KlondikeRules.CanMove(table[KlondikeRules.Column(1)], 0, table[KlondikeRules.Column(2)]).Should().BeTrue();
    }

    [Fact]
    public void Runs_MoveTogether_AndTheUncoveredCardTurnsUp()
    {
        var table = Layout(columns: ["#4D 9S 8H 7C", "10D"]);
        var game = new CardGame(table, _rules);
        var source = table[KlondikeRules.Column(0)];

        var turn = game.Play(new DragCards(source, 1, table[KlondikeRules.Column(1)]));

        turn.Should().NotBeNull();
        turn!.Has(KlondikeRules.RevealTag).Should().BeTrue();
        Codes(table[KlondikeRules.Column(1)]).Should().Equal("10D", "9S", "8H", "7C");
        source.Top.Should().Be(new PlacedCard(Card.Parse("4D"), FaceUp: true));

        game.Undo();
        source[0].FaceUp.Should().BeFalse("undo turns it back down");
        source.Count.Should().Be(4);
    }

    [Fact]
    public void BrokenRuns_CannotMove()
    {
        // 8C on 9S is not a valid run (same colour), so nothing from 9S up can be dragged.
        var table = Layout(columns: ["9S 8C", "10D"]);

        KlondikeRules.CanMove(table[KlondikeRules.Column(0)], 0, table[KlondikeRules.Column(1)]).Should().BeFalse();
    }

    [Fact]
    public void TappingACard_SendsItHomeFirst()
    {
        var table = Layout(columns: ["AD", "2S"]);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[KlondikeRules.Column(0)], 0))!.Has(KlondikeRules.FoundationTag).Should().BeTrue();
        table[KlondikeRules.Column(0)].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TappingACard_WithNowhereToGo_IsRejected()
    {
        var table = Layout(columns: ["5D", "9S"]);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[KlondikeRules.Column(0)], 0)).Should().BeNull();
    }

    [Fact]
    public void Hints_PreferFoundations()
    {
        var table = Layout(columns: ["10H", "9S", "AC"]);

        _rules.Hints(table).First().Should().Be(new DragCards(table[KlondikeRules.Column(2)], 0, table[KlondikeRules.Foundation(0)]));
    }

    [Fact]
    public void AutoMove_FinishesASettledGame()
    {
        // Everything face up, stock and waste empty: the game plays itself home.
        var table = Layout(columns: ["KH QS JH 10S 9H 8S 7H 6S 5H 4S 3H 2S AH", "KS QH JS 10H 9S 8H 7S 6H 5S 4H 3S 2H AS",
            "KD QC JD 10C 9D 8C 7D 6C 5D 4C 3D 2C AD", "KC QD JC 10D 9C 8D 7C 6D 5C 4D 3C 2D AC"]);
        var game = new CardGame(table, _rules);

        while (game.PlayAutoMove() is not null)
        {
        }

        game.IsWon.Should().BeTrue();
        game.History.Should().HaveCount(52);
    }

    [Fact]
    public void AutoMove_WaitsWhileCardsAreHidden()
    {
        var table = Layout(columns: ["#5D AH"]);

        _rules.AutoMove(table).Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_NeverBreaksTheRules_AndUndoReturnsToTheDeal(int seed)
    {
        var table = KlondikeRules.Deal(seed);
        var deal = Snapshot(table);
        var game = new CardGame(table, _rules);
        var seen = new HashSet<string>();

        // Follow the first hint until the position repeats (the stock cycles) or the game is won.
        for (var step = 0; step < 2000 && !game.IsWon && seen.Add(Snapshot(table)); step++)
        {
            var hint = _rules.Hints(table).FirstOrDefault();
            if (hint is null || game.Play(hint) is null)
            {
                break;
            }

            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().OnlyHaveUniqueItems();
            table.Piles.Sum(p => p.Count).Should().Be(52);
        }

        game.Restart();
        Snapshot(table).Should().Be(deal);
    }

    /// <summary>Columns by card codes, bottom first; a leading # marks a face-down card. Unnamed piles start empty.</summary>
    private static Table Layout(string[] columns)
    {
        var table = new Table();
        table.Add(KlondikeRules.Stock);
        table.Add(KlondikeRules.Waste);
        for (var i = 0; i < KlondikeRules.Foundations; i++)
        {
            table.Add(KlondikeRules.Foundation(i));
        }

        for (var i = 0; i < KlondikeRules.Columns; i++)
        {
            var pile = table.Add(KlondikeRules.Column(i));
            if (i < columns.Length)
            {
                foreach (var code in columns[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    pile.Deal(Card.Parse(code.TrimStart('#')), faceUp: !code.StartsWith('#'));
                }
            }
        }

        return table;
    }

    private bool Play(Table table, string from, int index, string to) =>
        new CardGame(table, _rules).Play(new DragCards(table[from], index, table[to])) is not null;

    private static IEnumerable<string> Codes(Pile pile) => pile.Cards.Select(c => c.Card.ToString());

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
