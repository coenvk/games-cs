using Jentl.Cards;

namespace Golf;

/// <summary>
/// Golf solitaire: seven columns of five face-up cards. Play the top card of any column onto the waste when
/// it is one rank above or below the waste's top card (no wrapping: nothing goes on a king). The stock turns
/// over one card at a time onto the waste and is gone through once. Clear the columns to win; like a round
/// of golf, the fewer cards left at the end, the better.
/// </summary>
public sealed class GolfRules : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const int Columns = 7;
    public const int Rows = 5;

    public const string DrawTag = "draw";
    public const string PlayTag = "play";

    public static string Column(int index) => $"column{index}";

    /// <summary>A shuffled deal: seven columns of five face-up cards, one card on the waste, 16 in the stock.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        var stock = table.Add(Stock);
        var waste = table.Add(Waste);
        var columns = Enumerable.Range(0, Columns).Select(i => table.Add(Column(i))).ToArray();
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var row = 0; row < Rows; row++)
        {
            foreach (var column in columns)
            {
                column.Deal(deck.Dequeue(), faceUp: true);
            }
        }

        waste.Deal(deck.Dequeue(), faceUp: true);
        while (deck.TryDequeue(out var card))
        {
            stock.Deal(card, faceUp: false);
        }

        return table;
    }

    public bool Play(CardTurnBuilder turn, CardInput input)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var table = turn.Table;
        switch (input)
        {
            case ClickPile { Pile.Name: Stock }:
                return Draw(turn, table);
            case ClickPile click when CanPlay(table, click.Pile):
                PlayCard(turn, table, click.Pile);
                return true;
            case DragCards { To.Name: Waste } drag when drag.Index == drag.From.Count - 1 && CanPlay(table, drag.From):
                PlayCard(turn, table, drag.From);
                return true;
            default:
                return false;
        }
    }

    public bool IsWon(Table table) => ColumnsOf(table).All(c => c.IsEmpty);

    /// <summary>
    /// Playable column tops, preferring one whose play leaves another play open (so a run keeps going), then
    /// the taller columns; the stock when nothing plays.
    /// </summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (IsWon(table))
        {
            yield break;
        }

        var playable = ColumnsOf(table).Where(c => CanPlay(table, c)).ToArray();
        foreach (var column in playable.OrderByDescending(c => FollowUps(table, c)).ThenByDescending(c => c.Count))
        {
            yield return new ClickPile(column, column.Count - 1);
        }

        var stock = table[Stock];
        if (!stock.IsEmpty)
        {
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Whether the top card of <paramref name="pile"/> (a column) goes on the waste.</summary>
    public static bool CanPlay(Table table, Pile pile)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(pile);
        return IsColumn(pile) && pile.Top is { } top && table[Waste].Top is { } waste && IsNext(waste.Card, top.Card);
    }

    /// <summary>One rank apart; kings and aces are not next to each other.</summary>
    public static bool IsNext(Card waste, Card card) => waste.Rank != Card.King && Math.Abs(waste.Rank - card.Rank) == 1;

    /// <summary>How many cards are still in the columns: the score, lower is better.</summary>
    public static int CardsLeft(Table table) => ColumnsOf(table).Sum(c => c.Count);

    private static bool Draw(CardTurnBuilder turn, Table table)
    {
        var stock = table[Stock];
        if (stock.IsEmpty)
        {
            return false;
        }

        var waste = table[Waste];
        turn.MoveTop(stock, waste);
        turn.Flip(waste, waste.Count - 1, faceUp: true);
        turn.Tag(DrawTag);
        return true;
    }

    private static void PlayCard(CardTurnBuilder turn, Table table, Pile column)
    {
        turn.MoveTop(column, table[Waste]);
        turn.Tag(PlayTag);
    }

    // How many other column tops would play after this one.
    private static int FollowUps(Table table, Pile column)
    {
        var card = column.Top!.Value.Card;
        return ColumnsOf(table).Count(c => c != column && c.Top is { } top && IsNext(card, top.Card))
            + (column.Count > 1 && IsNext(card, column[^2].Card) ? 1 : 0);
    }

    private static IEnumerable<Pile> ColumnsOf(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Enumerable.Range(0, Columns).Select(i => table[Column(i)]);
    }

    private static bool IsColumn(Pile pile) => pile.Name.StartsWith("column", StringComparison.Ordinal);
}
