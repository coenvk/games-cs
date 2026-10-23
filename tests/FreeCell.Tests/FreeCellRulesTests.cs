using Jentl.Cards;

namespace FreeCell.Tests;

public class FreeCellRulesTests
{
    private readonly FreeCellRules _rules = new();

    [Fact]
    public void Deal_MatchesMicrosoftDealOne()
    {
        var table = FreeCellRules.Deal(1);

        Row(table, 0).Should().Equal("JD", "2D", "9H", "JC", "5D", "7H", "7C", "5H");
        Row(table, 1).Should().Equal("KD", "KC", "9S", "5S", "AD", "QC", "KH", "3H");
    }

    [Fact]
    public void Deal_MatchesMicrosoftDeal617()
    {
        var table = FreeCellRules.Deal(617);

        Row(table, 0).Should().Equal("7D", "AD", "5C", "3S", "5S", "8C", "2D", "AH");
        Row(table, 1).Should().Equal("10D", "7S", "QD", "AC", "6D", "8H", "AS", "KH");
    }

    [Fact]
    public void Deal_FillsEightCascadesFaceUp()
    {
        var table = FreeCellRules.Deal(1);

        Enumerable.Range(0, 8).Select(i => table[FreeCellRules.Cascade(i)].Count).Should().Equal(7, 7, 7, 7, 6, 6, 6, 6);
        table.Piles.SelectMany(p => p.Cards).Should().OnlyContain(c => c.FaceUp);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void FreeCells_HoldOneCardEach()
    {
        var table = Layout(cascades: ["9S 8H", "KD"]);
        var cell = table[FreeCellRules.FreeCell(0)];

        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(0)], 0, cell).Should().BeFalse("a run of two does not fit a free cell");
        new CardGame(table, _rules).Play(new DragCards(table[FreeCellRules.Cascade(0)], 1, cell)).Should().NotBeNull();
        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(1)], 0, cell).Should().BeFalse("the cell is taken");
    }

    [Fact]
    public void Cascades_BuildDownInAlternatingColours_AndEmptyOnesTakeAnything()
    {
        var table = Layout(cascades: ["9S", "8H", "8C", "4D", ""]);

        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(1)], 0, table[FreeCellRules.Cascade(0)]).Should().BeTrue("8H on 9S");
        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(2)], 0, table[FreeCellRules.Cascade(0)]).Should().BeFalse("8C on 9S is same colour");
        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(3)], 0, table[FreeCellRules.Cascade(4)]).Should().BeTrue("any card starts an empty cascade");
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(2, 0, 3)]
    [InlineData(4, 0, 5)]
    [InlineData(1, 1, 4)]
    [InlineData(3, 2, 16)]
    public void Capacity_CountsFreeCellsAndEmptyCascades(int emptyCells, int emptyCascades, int expected)
    {
        var table = Layout(cascades: Enumerable.Repeat("KS", 8 - emptyCascades).Concat(Enumerable.Repeat("", emptyCascades)).ToArray());
        for (var i = 0; i < 4 - emptyCells; i++)
        {
            table[FreeCellRules.FreeCell(i)].Deal(Card.Parse($"{i + 2}H"), faceUp: true);
        }

        FreeCellRules.Capacity(table, table[FreeCellRules.Cascade(0)]).Should().Be(expected);
    }

    [Fact]
    public void Supermove_IsLimitedByCapacity_AndAnEmptyTargetDoesNotCount()
    {
        // Free cells all taken; exactly one empty cascade (the other seven hold cards). A run of 2 fits onto a card (capacity 2) but not into the empty cascade (capacity 1).
        var table = Layout(cascades: ["9S 8H", "10D", "", "KC", "KS", "QC", "QS", "JC"], freeCells: ["2C", "3C", "4C", "5C"]);
        var run = table[FreeCellRules.Cascade(0)];

        FreeCellRules.CanMove(table, run, 0, table[FreeCellRules.Cascade(1)]).Should().BeTrue();
        FreeCellRules.CanMove(table, run, 0, table[FreeCellRules.Cascade(2)]).Should().BeFalse();
        FreeCellRules.CanMove(table, run, 1, table[FreeCellRules.Cascade(2)]).Should().BeTrue("a single card always fits");
    }

    [Fact]
    public void BrokenRuns_CannotMove()
    {
        var table = Layout(cascades: ["9S 8C", "10D"]);

        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(0)], 0, table[FreeCellRules.Cascade(1)]).Should().BeFalse();
    }

    [Fact]
    public void Foundations_BuildBySuit_AndCardsNeverLeaveThem()
    {
        var table = Layout(cascades: ["AH", "2H", "2S", ""]);
        var game = new CardGame(table, _rules);
        var home = table[FreeCellRules.Foundation(0)];

        game.Play(new DragCards(table[FreeCellRules.Cascade(0)], 0, home)).Should().NotBeNull();
        FreeCellRules.CanMove(table, table[FreeCellRules.Cascade(2)], 0, home).Should().BeFalse("2S does not go on AH");
        game.Play(new DragCards(table[FreeCellRules.Cascade(1)], 0, home))!.Has(FreeCellRules.FoundationTag).Should().BeTrue();
        FreeCellRules.CanMove(table, home, 1, table[FreeCellRules.Cascade(3)]).Should().BeFalse();
    }

    [Fact]
    public void Tapping_PrefersHome_ThenACascade_ThenAFreeCell()
    {
        var table = Layout(cascades: ["JD 5D AC", "6S", "KH 3H", "KC", "KS", "QC", "QS", "JC"]);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[FreeCellRules.Cascade(0)], 2));
        table[FreeCellRules.Foundation(0)].Top!.Value.Card.Should().Be(Card.Parse("AC"));

        game.Play(new ClickPile(table[FreeCellRules.Cascade(0)], 1));
        table[FreeCellRules.Cascade(1)].Top!.Value.Card.Should().Be(Card.Parse("5D"));

        game.Play(new ClickPile(table[FreeCellRules.Cascade(2)], 1));
        table[FreeCellRules.FreeCell(0)].Top!.Value.Card.Should().Be(Card.Parse("3H"), "3H fits nowhere else, so it parks in a free cell");
    }

    [Fact]
    public void AutoMove_SendsHomeOnlyCardsNothingStillNeeds()
    {
        // Black foundations at 2 and 1: 3H is not safe yet (the AS's 2S could still want it).
        var table = Layout(cascades: ["3H", "2H"], foundations: ["AH", "2C", "AS", ""]);

        _rules.AutoMove(table).Should().Be(new DragCards(table[FreeCellRules.Cascade(1)], 0, table[FreeCellRules.Foundation(0)]), "twos are always safe");
        new CardGame(table, _rules).PlayAutoMove();
        _rules.AutoMove(table).Should().BeNull("3H could still be needed for 2S");

        table[FreeCellRules.Foundation(2)].Deal(Card.Parse("2S"), faceUp: true);
        _rules.AutoMove(table).Should().NotBeNull("both black foundations have reached 2 now");
    }

    [Fact]
    public void Hints_PreferFoundations()
    {
        var table = Layout(cascades: ["10H", "9S", "AC"]);

        _rules.Hints(table).First().Should().Be(new DragCards(table[FreeCellRules.Cascade(2)], 0, table[FreeCellRules.Foundation(0)]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(617)]
    [InlineData(11982)]
    public void PlayingHintsAndAutoMoves_KeepsTheDeckWhole_AndUndoReturnsToTheDeal(int deal)
    {
        var table = FreeCellRules.Deal(deal);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);
        var seen = new HashSet<string>();

        for (var step = 0; step < 500 && !game.IsWon && seen.Add(Snapshot(table)); step++)
        {
            while (game.PlayAutoMove() is not null)
            {
            }

            if (_rules.Hints(table).FirstOrDefault() is not { } hint || game.Play(hint) is null)
            {
                break;
            }

            table.Piles.Sum(p => p.Count).Should().Be(52);
            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().OnlyHaveUniqueItems();
        }

        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    private static IEnumerable<string> Row(Table table, int row) =>
        Enumerable.Range(0, 8).Select(i => table[FreeCellRules.Cascade(i)][row].Card.ToString());

    /// <summary>Cascades by card codes (bottom first); free cells and foundations by code, "" for empty.</summary>
    private static Table Layout(string[] cascades, string[]? freeCells = null, string[]? foundations = null)
    {
        var table = new Table();
        for (var i = 0; i < 4; i++)
        {
            var cell = table.Add(FreeCellRules.FreeCell(i));
            if (freeCells is not null && i < freeCells.Length && freeCells[i].Length > 0)
            {
                cell.Deal(Card.Parse(freeCells[i]), faceUp: true);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            var home = table.Add(FreeCellRules.Foundation(i));
            if (foundations is not null && i < foundations.Length && foundations[i].Length > 0)
            {
                // A foundation's top tells the rules everything; fill it from the ace up.
                var top = Card.Parse(foundations[i]);
                for (var rank = 1; rank <= top.Rank; rank++)
                {
                    home.Deal(new Card(rank, top.Suit), faceUp: true);
                }
            }
        }

        for (var i = 0; i < 8; i++)
        {
            var pile = table.Add(FreeCellRules.Cascade(i));
            if (i < cascades.Length)
            {
                foreach (var code in cascades[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    pile.Deal(Card.Parse(code), faceUp: true);
                }
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
