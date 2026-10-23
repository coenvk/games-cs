using Jentl.Cards;

namespace Klondike;

/// <summary>
/// Klondike solitaire, draw one: build the four foundations up by suit from ace to king. Tableau
/// columns build down in alternating colours; only a king (or a run headed by one) goes into an
/// empty column. Tapping the stock turns a card onto the waste, and an empty stock takes the
/// waste back. Uncovered tableau cards turn face up.
/// </summary>
public sealed class KlondikeRules : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const int Foundations = 4;
    public const int Columns = 7;

    public const string DrawTag = "draw";
    public const string RecycleTag = "recycle";
    public const string RevealTag = "reveal";
    public const string FoundationTag = "foundation";

    public static string Foundation(int index) => $"foundation{index}";

    public static string Column(int index) => $"tableau{index}";

    /// <summary>A shuffled deal: column i gets i + 1 cards with only the last face up, the rest goes to the stock face down.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        var stock = table.Add(Stock);
        table.Add(Waste);
        for (var i = 0; i < Foundations; i++)
        {
            table.Add(Foundation(i));
        }

        var columns = Enumerable.Range(0, Columns).Select(i => table.Add(Column(i))).ToArray();
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var column = 0; column < Columns; column++)
        {
            for (var row = 0; row <= column; row++)
            {
                columns[column].Deal(deck.Dequeue(), faceUp: row == column);
            }
        }

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
                return DrawOrRecycle(turn, table);
            case ClickPile click when click.Index >= 0 && click.Pile[click.Index].FaceUp:
                // A tap on a face-up card sends it wherever it can go: home first, then to a column.
                return BestTarget(table, click.Pile, click.Index) is { } target && Move(turn, click.Pile, click.Index, target);
            case DragCards drag:
                return CanMove(drag.From, drag.Index, drag.To) && Move(turn, drag.From, drag.Index, drag.To);
            default:
                return false;
        }
    }

    public bool IsWon(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Enumerable.Range(0, Foundations).All(i => table[Foundation(i)].Count == Card.King);
    }

    /// <summary>Moves worth making, best first: home to a foundation, tableau moves that uncover cards, the waste, then the stock.</summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var columns = ColumnsOf(table);

        foreach (var source in columns.Append(table[Waste]))
        {
            if (!source.IsEmpty && FoundationFor(table, source.Top!.Value.Card) is { } home)
            {
                yield return new DragCards(source, source.Count - 1, home);
            }
        }

        foreach (var source in columns)
        {
            var head = FirstFaceUp(source);
            if (head < 0)
            {
                continue;
            }

            foreach (var target in columns)
            {
                // Moving a whole column's face-up run is only useful when it uncovers something
                // (moving a king from one empty-bottomed column to another just shuffles).
                if (target != source && CanMove(source, head, target) && (head > 0 || !target.IsEmpty))
                {
                    yield return new DragCards(source, head, target);
                }
            }
        }

        var waste = table[Waste];
        if (!waste.IsEmpty)
        {
            foreach (var target in columns)
            {
                if (CanMove(waste, waste.Count - 1, target))
                {
                    yield return new DragCards(waste, waste.Count - 1, target);
                }
            }
        }

        if (!table[Stock].IsEmpty || !waste.IsEmpty)
        {
            yield return new ClickPile(table[Stock], table[Stock].Count - 1);
        }
    }

    /// <summary>
    /// Once the stock and waste are empty and every tableau card is face up the game is won anyway,
    /// so finish it: the lowest card that can go home goes home.
    /// </summary>
    public CardInput? AutoMove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var columns = ColumnsOf(table);
        if (!table[Stock].IsEmpty || !table[Waste].IsEmpty || columns.Any(c => c.Cards.Any(card => !card.FaceUp)) || IsWon(table))
        {
            return null;
        }

        return columns
            .Where(c => !c.IsEmpty && FoundationFor(table, c.Top!.Value.Card) is not null)
            .OrderBy(c => c.Top!.Value.Card.Rank)
            .Select(c => new DragCards(c, c.Count - 1, FoundationFor(table, c.Top!.Value.Card)!))
            .FirstOrDefault();
    }

    /// <summary>Whether the cards from <paramref name="index"/> up in <paramref name="from"/> may go onto <paramref name="to"/>.</summary>
    public static bool CanMove(Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || index < 0 || index >= from.Count || !from[index].FaceUp || IsStockOrWaste(to))
        {
            return false;
        }

        var count = from.Count - index;
        if (from.Name is Stock || (from.Name is Waste && count != 1) || (IsFoundation(from) && count != 1))
        {
            return false;
        }

        var moving = from[index].Card;
        if (IsFoundation(to))
        {
            return count == 1 && (to.IsEmpty ? moving.Rank == Card.Ace : to.Top!.Value.Card.Suit == moving.Suit && to.Top.Value.Card.Rank == moving.Rank - 1);
        }

        if (!IsValidRun(from, index))
        {
            return false;
        }

        return to.IsEmpty
            ? moving.Rank == Card.King
            : to.Top!.Value is { FaceUp: true } top && top.Card.Color != moving.Color && top.Card.Rank == moving.Rank + 1;
    }

    private static bool DrawOrRecycle(CardTurnBuilder turn, Table table)
    {
        var stock = table[Stock];
        var waste = table[Waste];
        if (!stock.IsEmpty)
        {
            turn.MoveTop(stock, waste);
            turn.Flip(waste, waste.Count - 1, faceUp: true);
            turn.Tag(DrawTag);
            return true;
        }

        if (waste.IsEmpty)
        {
            return false;
        }

        // Card by card, so the waste's top ends up at the bottom of the stock, as when turning it over.
        while (!waste.IsEmpty)
        {
            turn.MoveTop(waste, stock);
            turn.Flip(stock, stock.Count - 1, faceUp: false);
        }

        turn.Tag(RecycleTag);
        return true;
    }

    private static bool Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
        turn.Move(from, index, to);
        if (IsFoundation(to))
        {
            turn.Tag(FoundationTag);
        }

        if (IsColumn(from) && from.Top is { FaceUp: false })
        {
            turn.Flip(from, from.Count - 1, faceUp: true);
            turn.Tag(RevealTag);
        }

        return true;
    }

    private static Pile? BestTarget(Table table, Pile from, int index)
    {
        if (index == from.Count - 1 && FoundationFor(table, from[index].Card) is { } home && CanMove(from, index, home))
        {
            return home;
        }

        // Prefer building on a card over taking an empty column.
        return ColumnsOf(table)
            .Where(c => CanMove(from, index, c))
            .OrderBy(c => c.IsEmpty)
            .FirstOrDefault();
    }

    private static Pile? FoundationFor(Table table, Card card) =>
        Enumerable.Range(0, Foundations)
            .Select(i => table[Foundation(i)])
            .FirstOrDefault(f => f.IsEmpty ? card.Rank == Card.Ace : f.Top!.Value.Card.Suit == card.Suit && f.Top.Value.Card.Rank == card.Rank - 1);

    private static bool IsValidRun(Pile pile, int index)
    {
        for (var i = index + 1; i < pile.Count; i++)
        {
            var below = pile[i - 1];
            var above = pile[i];
            if (!above.FaceUp || above.Card.Color == below.Card.Color || above.Card.Rank != below.Card.Rank - 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int FirstFaceUp(Pile pile)
    {
        for (var i = 0; i < pile.Count; i++)
        {
            if (pile[i].FaceUp)
            {
                return i;
            }
        }

        return -1;
    }

    private static Pile[] ColumnsOf(Table table) => Enumerable.Range(0, Columns).Select(i => table[Column(i)]).ToArray();

    private static bool IsFoundation(Pile pile) => pile.Name.StartsWith("foundation", StringComparison.Ordinal);

    private static bool IsColumn(Pile pile) => pile.Name.StartsWith("tableau", StringComparison.Ordinal);

    private static bool IsStockOrWaste(Pile pile) => pile.Name is Stock or Waste;
}
