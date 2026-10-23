using Jentl.Cards;

namespace TriPeaks.Tests;

public class TriPeaksRulesTests
{
    private readonly TriPeaksRules _rules = new();

    [Fact]
    public void Deal_BuildsThreePeaks_WithOnlyTheBottomRowFaceUp()
    {
        var table = TriPeaksRules.Deal(seed: 4);
        var peaks = Enumerable.Range(0, TriPeaksRules.Cards).Select(i => table[TriPeaksRules.Position(i)]).ToArray();

        peaks.Should().OnlyContain(p => p.Count == 1);
        Enumerable.Range(0, TriPeaksRules.Cards).GroupBy(TriPeaksRules.RowOf).Select(g => g.Count()).Should().Equal(3, 6, 9, 10);
        Enumerable.Range(0, TriPeaksRules.Cards).Should().OnlyContain(i => peaks[i][0].FaceUp == (TriPeaksRules.RowOf(i) == 3));
        table[TriPeaksRules.Waste].Cards.Should().ContainSingle().Which.FaceUp.Should().BeTrue();
        table[TriPeaksRules.Stock].Count.Should().Be(23);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void Deal_IsReproducibleBySeed() =>
        Snapshot(TriPeaksRules.Deal(8)).Should().Be(Snapshot(TriPeaksRules.Deal(8)));

    [Fact]
    public void Layout_PutsEachCardBetweenTheTwoBelowIt()
    {
        // The first tip sits over the first peak's second row, which sits over the third row, and so on.
        TriPeaksRules.ColumnOf(0).Should().Be(1.5f);
        TriPeaksRules.ColumnOf(3).Should().Be(1f);
        TriPeaksRules.ColumnOf(9).Should().Be(0.5f);
        TriPeaksRules.ColumnOf(18).Should().Be(0f);
        TriPeaksRules.ColumnOf(27).Should().Be(9f);
    }

    [Theory]
    [InlineData("5H", "4S", true)]
    [InlineData("5H", "6C", true)]
    [InlineData("KH", "AS", true)]
    [InlineData("AH", "KS", true)]
    [InlineData("5H", "5S", false)]
    [InlineData("5H", "7S", false)]
    public void Cards_PlayOneRankUpOrDown_WrappingKingAndAce(string waste, string card, bool next) =>
        TriPeaksRules.IsNext(Card.Parse(waste), Card.Parse(card)).Should().Be(next);

    [Fact]
    public void PlayingACard_MovesItToTheWaste_AndUndoPutsItBack()
    {
        var table = Table("5H", ("4S", 18), ("9C", 19));
        var game = new CardGame(table, _rules);
        var four = table[TriPeaksRules.Position(18)];

        game.Play(new ClickPile(table[TriPeaksRules.Position(19)], 0)).Should().BeNull("9 is not next to 5");
        game.Play(new ClickPile(four, 0))!.Has(TriPeaksRules.PlayTag).Should().BeTrue();

        four.IsEmpty.Should().BeTrue();
        table[TriPeaksRules.Waste].Top!.Value.Card.Should().Be(Card.Parse("4S"));

        game.Undo();
        four.Top!.Value.Card.Should().Be(Card.Parse("4S"));
    }

    [Fact]
    public void DraggingOntoTheWaste_PlaysToo()
    {
        var table = Table("5H", ("6D", 20));
        var game = new CardGame(table, _rules);

        game.Play(new DragCards(table[TriPeaksRules.Position(20)], 0, table[TriPeaksRules.Waste])).Should().NotBeNull();
        game.IsWon.Should().BeTrue();
    }

    [Fact]
    public void ACard_TurnsUpWhenBothCardsCoveringItAreGone()
    {
        // Position 9 (third row) is covered by 18 and 19.
        var table = Table("5H", ("4S", 18), ("3S", 19), ("#QD", 9));
        var game = new CardGame(table, _rules);
        var hidden = table[TriPeaksRules.Position(9)];

        TriPeaksRules.IsFree(table, hidden).Should().BeFalse();
        game.Play(new ClickPile(table[TriPeaksRules.Position(18)], 0))!.Has(TriPeaksRules.RevealTag).Should().BeFalse();
        hidden[0].FaceUp.Should().BeFalse();

        game.Play(new ClickPile(table[TriPeaksRules.Position(19)], 0))!.Has(TriPeaksRules.RevealTag).Should().BeTrue();
        hidden[0].FaceUp.Should().BeTrue();
        TriPeaksRules.IsFree(table, hidden).Should().BeTrue();

        game.Undo();
        hidden[0].FaceUp.Should().BeFalse();
    }

    [Fact]
    public void TheStock_TurnsOverOneCard_AndIsNotRedealt()
    {
        var table = Table("5H", ("9C", 18));
        table[TriPeaksRules.Stock].Deal(Card.Parse("8D"), faceUp: false);
        var game = new CardGame(table, _rules);
        var stock = table[TriPeaksRules.Stock];

        game.Play(new ClickPile(stock, 0))!.Has(TriPeaksRules.DrawTag).Should().BeTrue();
        table[TriPeaksRules.Waste].Top.Should().Be(new PlacedCard(Card.Parse("8D"), FaceUp: true));

        game.Play(new ClickPile(stock, -1)).Should().BeNull();
        game.Play(new ClickPile(table[TriPeaksRules.Position(18)], 0)).Should().NotBeNull("9 goes on the drawn 8");
    }

    [Fact]
    public void Hints_PreferCardsThatTurnOthersUp()
    {
        // Playing the 4 turns up the queen, as nothing else covers it; the 6 turns up nothing.
        var table = Table("5H", ("6C", 27), ("4S", 18), ("#QD", 9));

        _rules.Hints(table).First().Should().Be(new ClickPile(table[TriPeaksRules.Position(18)], 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = TriPeaksRules.Deal(seed);
        var start = Snapshot(table);
        var game = new CardGame(table, _rules);

        for (var step = 0; step < 200 && !game.IsWon; step++)
        {
            if (_rules.Hints(table).FirstOrDefault() is not { } hint || game.Play(hint) is null)
            {
                break;
            }

            table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
            Enumerable.Range(0, TriPeaksRules.Cards).Select(i => table[TriPeaksRules.Position(i)])
                .Where(p => !p.IsEmpty).All(p => p[0].FaceUp == TriPeaksRules.IsFree(table, p)).Should().BeTrue("exactly the free cards are face up");
        }

        _rules.Hints(table).Should().BeEmpty("hints play until nothing is left to try");
        game.IsWon.Should().Be(seed == 3, "deal 3 happens to be won by always taking the first hint");
        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>A table with the waste card and these cards at these positions (# marks face down); the rest empty.</summary>
    private static Table Table(string waste, params (string Card, int At)[] cards)
    {
        var table = new Table();
        table.Add(TriPeaksRules.Stock);
        table.Add(TriPeaksRules.Waste).Deal(Card.Parse(waste), faceUp: true);
        for (var i = 0; i < TriPeaksRules.Cards; i++)
        {
            var pile = table.Add(TriPeaksRules.Position(i));
            foreach (var (code, _) in cards.Where(c => c.At == i))
            {
                pile.Deal(Card.Parse(code.TrimStart('#')), faceUp: !code.StartsWith('#'));
            }
        }

        return table;
    }

    private static string Snapshot(Table table) => string.Join(" | ", table.Piles.Select(p => p.ToString()));
}
