using Jentl.Cards;

namespace FortyThieves;

/// <summary>
/// Forty Thieves, a hard two-deck solitaire: ten columns of four face-up cards, eight foundations to build
/// up by suit from ace to king, and a stock gone through once, one card at a time. Columns build down in the
/// same suit, and only one card moves at a time (the top of a column or of the waste); an empty column takes
/// any card.
/// </summary>
public sealed class FortyThievesRules : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const int Foundations = 8;
    public const int Columns = 10;
    public const int Rows = 4;

    public const string DrawTag = "draw";
    public const string FoundationTag = "foundation";

    public static string Foundation(int index) => $"foundation{index}";

    public static string Column(int index) => $"column{index}";

    /// <summary>A shuffled deal of two decks: ten columns of four face-up cards and the other 64 in the stock.</summary>
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
        var cards = Deck.Standard52(copy: 0).Concat(Deck.Standard52(copy: 1));
        var deck = new Queue<Card>(Deck.Shuffled(cards, new Random(seed)));
        for (var row = 0; row < Rows; row++)
        {
            foreach (var column in columns)
            {
                column.Deal(deck.Dequeue(), faceUp: true);
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
                return Draw(turn, table);
            case DragCards drag when CanMove(table, drag.From, drag.Index, drag.To):
                Move(turn, drag.From, drag.To);
                return true;
            case ClickPile click when click.Index >= 0 && click.Index == click.Pile.Count - 1 && BestTarget(table, click.Pile) is { } target:
                Move(turn, click.Pile, target);
                return true;
            default:
                return false;
        }
    }

    public bool IsWon(Table table) => FoundationsOf(table).All(f => f.Count == Card.King);

    /// <summary>Moves worth making, best first: home, then builds that empty a column or free the waste, then the stock.</summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (IsWon(table))
        {
            yield break;
        }

        var sources = ColumnsOf(table).Append(table[Waste]).Where(p => !p.IsEmpty).ToArray();
        foreach (var from in sources)
        {
            if (FoundationFor(table, from.Top!.Value.Card) is { } home)
            {
                yield return new DragCards(from, from.Count - 1, home);
            }
        }

        // Cards that can go home were offered above; one empty column stands for all of them.
        var builds = new List<(DragCards Move, int Score)>();
        var empty = ColumnsOf(table).FirstOrDefault(c => c.IsEmpty);
        foreach (var from in sources.Where(s => FoundationFor(table, s.Top!.Value.Card) is null))
        {
            foreach (var to in ColumnsOf(table).Where(t => t != from && (!t.IsEmpty || t == empty) && CanMove(table, from, from.Count - 1, t)))
            {
                // Shuffling a lone card between empty columns, or off a card it already builds on, gains nothing.
                if ((to.IsEmpty && from.Count == 1 && from.Name != Waste)
                    || (from.Count > 1 && from.Name != Waste && Fits(from.Top!.Value.Card, from[^2].Card)))
                {
                    continue;
                }

                var score = (from.Name == Waste ? 2 : 0) + (from.Count == 1 && from.Name != Waste ? 3 : 0) + (to.IsEmpty ? 0 : 1);
                builds.Add((new DragCards(from, from.Count - 1, to), score));
            }
        }

        foreach (var (move, _) in builds.OrderByDescending(b => b.Score))
        {
            yield return move;
        }

        var stock = table[Stock];
        if (!stock.IsEmpty)
        {
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Aces go home by themselves; they can never be wanted anywhere else.</summary>
    public CardInput? AutoMove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var from in ColumnsOf(table).Append(table[Waste]).Where(p => !p.IsEmpty))
        {
            if (from.Top!.Value.Card.Rank == Card.Ace && FoundationFor(table, from.Top.Value.Card) is { } home)
            {
                return new DragCards(from, from.Count - 1, home);
            }
        }

        return null;
    }

    /// <summary>Whether the top card of <paramref name="from"/> (a column or the waste) may go onto <paramref name="to"/>.</summary>
    public static bool CanMove(Table table, Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || (!IsColumn(from) && from.Name != Waste) || from.IsEmpty || index != from.Count - 1)
        {
            return false;
        }

        var card = from.Top!.Value.Card;
        return IsFoundation(to) ? FoundationFor(table, card) == to
            : IsColumn(to) && (to.IsEmpty || Fits(card, to.Top!.Value.Card));
    }

    /// <summary>Whether <paramref name="card"/> builds on <paramref name="below"/>: same suit, one rank lower.</summary>
    public static bool Fits(Card card, Card below) => card.Suit == below.Suit && card.Rank == below.Rank - 1;

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

    private static void Move(CardTurnBuilder turn, Pile from, Pile to)
    {
        turn.MoveTop(from, to);
        if (IsFoundation(to))
        {
            turn.Tag(FoundationTag);
        }
    }

    /// <summary>A tap: home first, then onto a matching column, then into an empty column (not from a lone card).</summary>
    private static Pile? BestTarget(Table table, Pile from)
    {
        if (from.IsEmpty || (!IsColumn(from) && from.Name != Waste))
        {
            return null;
        }

        var index = from.Count - 1;
        return FoundationFor(table, from.Top!.Value.Card)
            ?? ColumnsOf(table).Where(c => !c.IsEmpty).FirstOrDefault(c => CanMove(table, from, index, c))
            ?? (IsColumn(from) && from.Count == 1 ? null : ColumnsOf(table).FirstOrDefault(c => c.IsEmpty));
    }

    private static Pile? FoundationFor(Table table, Card card) =>
        card.Rank == Card.Ace
            ? FoundationsOf(table).FirstOrDefault(f => f.IsEmpty)
            : FoundationsOf(table).FirstOrDefault(f => f.Top is { } top && top.Card.Suit == card.Suit && top.Card.Rank == card.Rank - 1);

    private static IEnumerable<Pile> FoundationsOf(Table table) => Enumerable.Range(0, Foundations).Select(i => table[Foundation(i)]);

    private static IEnumerable<Pile> ColumnsOf(Table table) => Enumerable.Range(0, Columns).Select(i => table[Column(i)]);

    private static bool IsColumn(Pile pile) => pile.Name.StartsWith("column", StringComparison.Ordinal);

    private static bool IsFoundation(Pile pile) => pile.Name.StartsWith("foundation", StringComparison.Ordinal);
}
