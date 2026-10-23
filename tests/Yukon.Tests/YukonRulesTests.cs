using Jentl.Cards;

namespace Yukon.Tests;

public class YukonRulesTests
{
    private readonly YukonRules _rules = new();

    [Fact]
    public void Deal_PutsAllCardsInTheColumns_WithFiveFaceUpOnTheFaceDownOnes()
    {
        var table = YukonRules.Deal(seed: 4);
        var columns = Enumerable.Range(0, 7).Select(i => table[YukonRules.Column(i)]).ToArray();

        columns.Select(c => c.Count).Should().Equal(1, 6, 7, 8, 9, 10, 11);
        columns.Select(c => c.Cards.Count(x => !x.FaceUp)).Should().Equal(0, 1, 2, 3, 4, 5, 6);
        table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().BeEquivalentTo(Deck.Standard52());
    }

    [Fact]
    public void AnyFaceUpCard_MovesWithEverythingOnIt_EvenOutOfOrder()
    {
        // The 9 of clubs carries the jack and 2 on top of it onto the 10 of hearts.
        var table = Layout(["#4D 9C JS 2H", "10H"]);
        var game = new CardGame(table, _rules);
        var from = table[YukonRules.Column(0)];

        YukonRules.CanMove(table, from, 0, table[YukonRules.Column(1)]).Should().BeFalse("face-down cards stay put");
        game.Play(new DragCards(from, 1, table[YukonRules.Column(1)]))!.Has(YukonRules.RevealTag).Should().BeTrue();

        Codes(table[YukonRules.Column(1)]).Should().Equal("10H", "9C", "JS", "2H");
        from[0].FaceUp.Should().BeTrue();
    }

    [Fact]
    public void Builds_NeedTheOtherColourOneHigher_AndEmptyColumnsTakeOnlyKings()
    {
        var table = Layout(["9C", "10S", "10D", "", "KH QS"]);

        YukonRules.CanMove(table, table[YukonRules.Column(0)], 0, table[YukonRules.Column(1)]).Should().BeFalse("same colour");
        YukonRules.CanMove(table, table[YukonRules.Column(0)], 0, table[YukonRules.Column(2)]).Should().BeTrue();
        YukonRules.CanMove(table, table[YukonRules.Column(0)], 0, table[YukonRules.Column(3)]).Should().BeFalse();
        YukonRules.CanMove(table, table[YukonRules.Column(4)], 0, table[YukonRules.Column(3)]).Should().BeTrue("a king with its cards");
    }

    [Fact]
    public void Foundations_BuildUpBySuit_FromTheTopOfAColumnOnly()
    {
        var table = Layout(["AH", "2H 5C", "2H"]);
        var game = new CardGame(table, _rules);

        game.Play(new ClickPile(table[YukonRules.Column(0)], 0))!.Has(YukonRules.FoundationTag).Should().BeTrue();
        YukonRules.CanMove(table, table[YukonRules.Column(1)], 0, table[YukonRules.Foundation(0)]).Should().BeFalse("the 5 lies on it");
        game.Play(new ClickPile(table[YukonRules.Column(2)], 0)).Should().NotBeNull();
        table[YukonRules.Foundation(0)].Count.Should().Be(2);
    }

    [Fact]
    public void LowCards_GoHomeByThemselves()
    {
        var table = Layout(["9S AD"]);

        _rules.AutoMove(table).Should().Be(new DragCards(table[YukonRules.Column(0)], 1, table[YukonRules.Foundation(0)]));
    }

    [Fact]
    public void Hints_SendCardsHome_ThenUncoverCards()
    {
        var table = Layout(["#3S 8C", "9H", "AS"]);

        _rules.Hints(table).Should().Equal(
            new DragCards(table[YukonRules.Column(2)], 0, table[YukonRules.Foundation(0)]),
            new DragCards(table[YukonRules.Column(0)], 1, table[YukonRules.Column(1)]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlayingHints_KeepsEveryCard_AndRestartReturnsToTheDeal(int seed)
    {
        var table = YukonRules.Deal(seed);
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
            table.Piles.Where(p => !p.IsEmpty).Should().OnlyContain(p => p.Top!.Value.FaceUp, "an uncovered card is always turned up");
        }

        game.Restart();
        Snapshot(table).Should().Be(start);
    }

    /// <summary>Columns by card codes, bottom first; # marks a face-down card.</summary>
    private static Table Layout(string[] columns)
    {
        var table = new Table();
        for (var i = 0; i < YukonRules.Foundations; i++)
        {
            table.Add(YukonRules.Foundation(i));
        }

        for (var i = 0; i < YukonRules.Columns; i++)
        {
            var pile = table.Add(YukonRules.Column(i));
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
