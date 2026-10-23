using Jentl.Cards;

namespace Pyramid;

/// <summary>
/// Pyramid solitaire: 28 cards in a pyramid of seven rows, each row half covering the one above. Remove
/// two uncovered cards whose ranks add up to 13 (ace 1, jack 11, queen 12), or a king on its own. The top
/// card of the waste plays too; the stock turns over one card at a time and may be gone through three
/// times. Clear the pyramid to win.
/// </summary>
public sealed class PyramidRules : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const string Removed = "removed";
    public const int Rows = 7;
    public const int Cards = Rows * (Rows + 1) / 2;
    public const int Pair = 13;

    /// <summary>How often the waste may be turned back into the stock (three passes in all).</summary>
    public const int Redeals = 2;

    /// <summary>The <see cref="Table.Counter"/> of redeals used so far.</summary>
    public const string RedealCounter = "redeals";

    public const string DrawTag = "draw";
    public const string RedealTag = "redeal";
    public const string RemoveTag = "remove";

    /// <summary>The pile for the card in <paramref name="row"/> (0 at the top) and <paramref name="column"/> (0 to row).</summary>
    public static string Position(int row, int column)
    {
        if (row is < 0 or >= Rows || column < 0 || column > row)
        {
            throw new ArgumentOutOfRangeException(nameof(column), $"No pyramid position ({row}, {column}).");
        }

        return $"pyramid{row * (row + 1) / 2 + column}";
    }

    /// <summary>A shuffled deal: the pyramid face up, top row first, and the other 24 cards face down in the stock.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        var stock = table.Add(Stock);
        table.Add(Waste);
        table.Add(Removed);
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column <= row; column++)
            {
                table.Add(Position(row, column)).Deal(deck.Dequeue(), faceUp: true);
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
                return DrawOrRedeal(turn, table);
            case ClickPile click when IsAvailableKing(table, click.Pile):
                return Remove(turn, table, click.Pile);
            case DragCards { To.Name: Removed } drag when IsAvailableKing(table, drag.From):
                return Remove(turn, table, drag.From);
            case DragCards drag when CanPair(table, drag.From, drag.To):
                return Remove(turn, table, drag.From, drag.To);
            default:
                return false;
        }
    }

    public bool IsWon(Table table) => PyramidPiles(table).All(p => p.IsEmpty);

    /// <summary>Kings and pairs first (pyramid cards before the waste), then the stock.</summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var available = AvailablePiles(table).ToArray();
        foreach (var pile in available.Where(p => IsAvailableKing(table, p)))
        {
            yield return new ClickPile(pile, pile.Count - 1);
        }

        for (var a = 0; a < available.Length; a++)
        {
            for (var b = a + 1; b < available.Length; b++)
            {
                if (CanPair(table, available[a], available[b]))
                {
                    // Drag the waste card onto the pyramid, which reads better than the other way round.
                    var (from, to) = available[b].Name == Waste ? (available[b], available[a]) : (available[a], available[b]);
                    yield return new DragCards(from, from.Count - 1, to);
                }
            }
        }

        if (CanDrawOrRedeal(table))
        {
            var stock = table[Stock];
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Whether the top card of <paramref name="pile"/> can be played: an uncovered pyramid card or the waste's top card.</summary>
    public static bool IsAvailable(Table table, Pile pile)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(pile);
        if (pile.IsEmpty)
        {
            return false;
        }

        if (pile.Name == Waste)
        {
            return true;
        }

        if (!TryPosition(pile, out var row, out var column))
        {
            return false;
        }

        return row == Rows - 1 || (table[Position(row + 1, column)].IsEmpty && table[Position(row + 1, column + 1)].IsEmpty);
    }

    /// <summary>Whether the top cards of two different available piles add up to 13.</summary>
    public static bool CanPair(Table table, Pile a, Pile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a != b && IsAvailable(table, a) && IsAvailable(table, b) && a.Top!.Value.Card.Rank + b.Top!.Value.Card.Rank == Pair;
    }

    /// <summary>Whether the stock can turn over a card, or the waste can go back for another pass.</summary>
    public static bool CanDrawOrRedeal(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return !table[Stock].IsEmpty || (!table[Waste].IsEmpty && table.Counter(RedealCounter) < Redeals);
    }

    private static bool IsAvailableKing(Table table, Pile pile) => IsAvailable(table, pile) && pile.Top!.Value.Card.Rank == Card.King;

    private static bool Remove(CardTurnBuilder turn, Table table, params Pile[] piles)
    {
        foreach (var pile in piles)
        {
            turn.MoveTop(pile, table[Removed]);
        }

        turn.Tag(RemoveTag);
        return true;
    }

    private static bool DrawOrRedeal(CardTurnBuilder turn, Table table)
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

        if (!CanDrawOrRedeal(table))
        {
            return false;
        }

        while (!waste.IsEmpty)
        {
            turn.MoveTop(waste, stock);
            turn.Flip(stock, stock.Count - 1, faceUp: false);
        }

        turn.Count(RedealCounter);
        turn.Tag(RedealTag);
        return true;
    }

    private static IEnumerable<Pile> AvailablePiles(Table table) =>
        PyramidPiles(table).Append(table[Waste]).Where(p => IsAvailable(table, p));

    private static IEnumerable<Pile> PyramidPiles(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column <= row; column++)
            {
                yield return table[Position(row, column)];
            }
        }
    }

    private static bool TryPosition(Pile pile, out int row, out int column)
    {
        row = column = 0;
        if (!pile.Name.StartsWith("pyramid", StringComparison.Ordinal)
            || !int.TryParse(pile.Name.AsSpan("pyramid".Length), System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        while (index > row)
        {
            index -= row + 1;
            row++;
        }

        column = index;
        return true;
    }
}
