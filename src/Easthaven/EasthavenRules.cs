using Jentl.Cards;

namespace Easthaven;

/// <summary>
/// Easthaven, a cross between Klondike and Spider: seven columns of three cards with only the top one face
/// up, and a stock that deals one card face up onto every column (31 cards: four full rows, then a last
/// row of three). Columns build down in alternating colours, a properly built run moves as one, and an empty
/// column takes any card or run. Build the four foundations up by suit from ace to king.
/// </summary>
public sealed class EasthavenRules : ICardRules
{
    public const string Stock = "stock";
    public const int Foundations = 4;
    public const int Columns = 7;
    public const int Rows = 3;

    public const string DealTag = "deal";
    public const string RevealTag = "reveal";
    public const string FoundationTag = "foundation";

    public static string Foundation(int index) => $"foundation{index}";

    public static string Column(int index) => $"column{index}";

    /// <summary>A shuffled deal: seven columns of two face-down cards under a face-up one; the other 31 in the stock.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        var stock = table.Add(Stock);
        for (var i = 0; i < Foundations; i++)
        {
            table.Add(Foundation(i));
        }

        var columns = Enumerable.Range(0, Columns).Select(i => table.Add(Column(i))).ToArray();
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var row = 0; row < Rows; row++)
        {
            foreach (var column in columns)
            {
                column.Deal(deck.Dequeue(), faceUp: row == Rows - 1);
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
                return DealRow(turn, table);
            case DragCards drag when CanMove(table, drag.From, drag.Index, drag.To):
                Move(turn, drag.From, drag.Index, drag.To);
                return true;
            case ClickPile click when click.Index >= 0 && BestTarget(table, click.Pile, click.Index) is { } target:
                Move(turn, click.Pile, click.Index, target);
                return true;
            default:
                return false;
        }
    }

    public bool IsWon(Table table) => FoundationsOf(table).All(f => f.Count == Card.King);

    /// <summary>Moves worth making, best first: home, then moves that uncover a card, then other builds; dealing last.</summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (IsWon(table))
        {
            yield break;
        }

        foreach (var column in ColumnsOf(table).Where(c => !c.IsEmpty))
        {
            if (FoundationFor(table, column.Top!.Value.Card) is { } home)
            {
                yield return new DragCards(column, column.Count - 1, home);
            }
        }

        // One empty column stands for all of them.
        var empty = ColumnsOf(table).FirstOrDefault(c => c.IsEmpty);
        var moves = new List<(DragCards Move, int Score)>();
        foreach (var from in ColumnsOf(table).Where(c => !c.IsEmpty))
        {
            var head = RunStart(from);
            foreach (var to in ColumnsOf(table).Where(t => t != from && (!t.IsEmpty || t == empty) && CanMove(table, from, head, t)))
            {
                var uncovers = head > 0 && !from[head - 1].FaceUp;
                var empties = head == 0;

                // A whole column into an empty one just moves it.
                if (to.IsEmpty && empties)
                {
                    continue;
                }

                moves.Add((new DragCards(from, head, to), (uncovers ? 4 : 0) + (empties ? 2 : 0) + (to.IsEmpty ? 0 : 1)));
            }
        }

        foreach (var (move, _) in moves.OrderByDescending(m => m.Score))
        {
            yield return move;
        }

        var stock = table[Stock];
        if (!stock.IsEmpty)
        {
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Sends cards home when no column could still want them, as FreeCell does.</summary>
    public CardInput? AutoMove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var column in ColumnsOf(table).Where(c => !c.IsEmpty))
        {
            var card = column.Top!.Value.Card;
            if (FoundationFor(table, card) is { } home && IsSafeToSendHome(table, card))
            {
                return new DragCards(column, column.Count - 1, home);
            }
        }

        return null;
    }

    /// <summary>Whether the cards from <paramref name="index"/> up in <paramref name="from"/> may go onto <paramref name="to"/>.</summary>
    public static bool CanMove(Table table, Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || !IsColumn(from) || !IsRun(from, index))
        {
            return false;
        }

        var card = from[index].Card;
        if (IsFoundation(to))
        {
            return index == from.Count - 1 && FoundationFor(table, card) == to;
        }

        return IsColumn(to) && (to.IsEmpty || Fits(card, to.Top!.Value.Card));
    }

    /// <summary>Whether the cards from <paramref name="index"/> up are face up and built down in alternating colours.</summary>
    public static bool IsRun(Pile pile, int index)
    {
        ArgumentNullException.ThrowIfNull(pile);
        if (index < 0 || index >= pile.Count || !pile[index].FaceUp)
        {
            return false;
        }

        for (var i = index + 1; i < pile.Count; i++)
        {
            if (!pile[i].FaceUp || !Fits(pile[i].Card, pile[i - 1].Card))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="card"/> builds on <paramref name="below"/>: other colour, one rank lower.</summary>
    public static bool Fits(Card card, Card below) => card.Color != below.Color && card.Rank == below.Rank - 1;

    /// <summary>One face-up card from the stock onto each column in turn, as far as the stock goes.</summary>
    private static bool DealRow(CardTurnBuilder turn, Table table)
    {
        var stock = table[Stock];
        if (stock.IsEmpty)
        {
            return false;
        }

        foreach (var column in ColumnsOf(table))
        {
            if (stock.IsEmpty)
            {
                break;
            }

            turn.MoveTop(stock, column);
            turn.Flip(column, column.Count - 1, faceUp: true);
        }

        turn.Tag(DealTag);
        return true;
    }

    private static void Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
        turn.Move(from, index, to);
        if (IsFoundation(to))
        {
            turn.Tag(FoundationTag);
        }

        if (from.Top is { FaceUp: false })
        {
            turn.Flip(from, from.Count - 1, faceUp: true);
            turn.Tag(RevealTag);
        }
    }

    /// <summary>A tap: home if the card is on top and can go, else the first column it fits, else an empty column.</summary>
    private static Pile? BestTarget(Table table, Pile from, int index)
    {
        if (!IsColumn(from) || !IsRun(from, index))
        {
            return null;
        }

        if (index == from.Count - 1 && FoundationFor(table, from[index].Card) is { } home)
        {
            return home;
        }

        return ColumnsOf(table).Where(c => !c.IsEmpty).FirstOrDefault(c => CanMove(table, from, index, c))
            ?? (index == 0 ? null : ColumnsOf(table).FirstOrDefault(c => c.IsEmpty));
    }

    /// <summary>The lowest index from which the top of the column is a run.</summary>
    private static int RunStart(Pile pile)
    {
        var index = pile.Count - 1;
        while (index > 0 && IsRun(pile, index - 1))
        {
            index--;
        }

        return index;
    }

    private static Pile? FoundationFor(Table table, Card card) =>
        card.Rank == Card.Ace
            ? FoundationsOf(table).FirstOrDefault(f => f.IsEmpty)
            : FoundationsOf(table).FirstOrDefault(f => f.Top is { } top && top.Card.Suit == card.Suit && top.Card.Rank == card.Rank - 1);

    private static bool IsSafeToSendHome(Table table, Card card)
    {
        if (card.Rank <= 2)
        {
            return true;
        }

        var otherColour = FoundationsOf(table).Where(f => !f.IsEmpty && f.Top!.Value.Card.Color != card.Color).Select(f => f.Top!.Value.Card.Rank).ToArray();
        return otherColour.Length == 2 && otherColour.All(rank => rank >= card.Rank - 1);
    }

    private static IEnumerable<Pile> FoundationsOf(Table table) => Enumerable.Range(0, Foundations).Select(i => table[Foundation(i)]);

    private static IEnumerable<Pile> ColumnsOf(Table table) => Enumerable.Range(0, Columns).Select(i => table[Column(i)]);

    private static bool IsColumn(Pile pile) => pile.Name.StartsWith("column", StringComparison.Ordinal);

    private static bool IsFoundation(Pile pile) => pile.Name.StartsWith("foundation", StringComparison.Ordinal);
}
