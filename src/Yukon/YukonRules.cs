using Jentl.Cards;

namespace Yukon;

/// <summary>
/// Yukon solitaire: Klondike's columns without a stock. All 52 cards are dealt into seven columns, the
/// first with one face-up card and the others with face-down cards under five face-up ones. Any face-up
/// card moves together with everything on top of it, in or out of order, onto a card of the other colour
/// one rank higher; only a king (with what lies on it) goes into an empty column. Build the four
/// foundations up by suit from ace to king.
/// </summary>
public sealed class YukonRules : ICardRules
{
    public const int Foundations = 4;
    public const int Columns = 7;
    public const int FaceUp = 5;

    public const string RevealTag = "reveal";
    public const string FoundationTag = "foundation";

    public static string Foundation(int index) => $"foundation{index}";

    public static string Column(int index) => $"column{index}";

    /// <summary>A shuffled deal: column i holds i face-down cards (none in the first) under the face-up ones.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        for (var i = 0; i < Foundations; i++)
        {
            table.Add(Foundation(i));
        }

        var columns = Enumerable.Range(0, Columns).Select(i => table.Add(Column(i))).ToArray();
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var c = 0; c < Columns; c++)
        {
            var faceUp = c == 0 ? 1 : FaceUp;
            for (var row = 0; row < c + faceUp; row++)
            {
                columns[c].Deal(deck.Dequeue(), faceUp: row >= c);
            }
        }

        return table;
    }

    public bool Play(CardTurnBuilder turn, CardInput input)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var table = turn.Table;
        switch (input)
        {
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

    /// <summary>Moves worth making, best first: home, then moves that uncover a card, then other builds.</summary>
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
        var moves = new List<(DragCards Move, int Score)>();
        var empty = ColumnsOf(table).FirstOrDefault(c => c.IsEmpty);
        foreach (var from in ColumnsOf(table))
        {
            for (var index = 0; index < from.Count; index++)
            {
                if (!from[index].FaceUp)
                {
                    continue;
                }

                foreach (var to in ColumnsOf(table).Where(t => t != from && (!t.IsEmpty || t == empty) && CanMove(table, from, index, t)))
                {
                    var uncovers = index > 0 && !from[index - 1].FaceUp;
                    var empties = index == 0;

                    // A king from the bottom of a column into another empty one achieves nothing.
                    if (to.IsEmpty && empties)
                    {
                        continue;
                    }

                    // Moving a card off the card it already sits on properly only shuffles things about.
                    if (index > 0 && from[index - 1].FaceUp && Fits(from[index].Card, from[index - 1].Card) && !uncovers)
                    {
                        continue;
                    }

                    moves.Add((new DragCards(from, index, to), (uncovers ? 4 : 0) + (empties ? 2 : 0) + (to.IsEmpty ? 0 : 1)));
                }
            }
        }

        foreach (var (move, _) in moves.OrderByDescending(m => m.Score))
        {
            yield return move;
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

    /// <summary>Whether the face-up card at <paramref name="index"/>, with everything on it, may go onto <paramref name="to"/>.</summary>
    public static bool CanMove(Table table, Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || !IsColumn(from) || index < 0 || index >= from.Count || !from[index].FaceUp)
        {
            return false;
        }

        var card = from[index].Card;
        if (IsFoundation(to))
        {
            return index == from.Count - 1 && FoundationFor(table, card) == to;
        }

        return IsColumn(to) && (to.IsEmpty ? card.Rank == Card.King : Fits(card, to.Top!.Value.Card));
    }

    /// <summary>Whether <paramref name="card"/> builds on <paramref name="below"/>: other colour, one rank lower.</summary>
    public static bool Fits(Card card, Card below) => card.Color != below.Color && card.Rank == below.Rank - 1;

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

    /// <summary>A tap: home if the card is on top and can go, else the first column it fits.</summary>
    private static Pile? BestTarget(Table table, Pile from, int index)
    {
        if (!IsColumn(from) || index >= from.Count || !from[index].FaceUp)
        {
            return null;
        }

        if (index == from.Count - 1 && FoundationFor(table, from[index].Card) is { } home)
        {
            return home;
        }

        return ColumnsOf(table).Where(c => !c.IsEmpty).FirstOrDefault(c => CanMove(table, from, index, c))
            ?? (index == 0 ? null : ColumnsOf(table).FirstOrDefault(c => c.IsEmpty && CanMove(table, from, index, c)));
    }

    /// <summary>The foundation that takes <paramref name="card"/> next: one of its suit one lower, or an empty one for an ace.</summary>
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
