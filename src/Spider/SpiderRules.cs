using Jentl.Cards;

namespace Spider;

/// <summary>
/// Spider solitaire with two decks' worth of cards in one, two or four suits. Ten columns build down
/// regardless of suit, but only a run of one suit moves as a unit. A complete king-to-ace run of one
/// suit leaves the table at once. The stock deals one card onto every column, and only while no
/// column is empty. Clear all eight runs to win.
/// </summary>
public sealed class SpiderRules : ICardRules
{
    public const string Stock = "stock";
    public const int Columns = 10;
    public const int Runs = 8;

    public const string DealTag = "deal";
    public const string CompleteTag = "complete";
    public const string RevealTag = "reveal";

    public static string Column(int index) => $"column{index}";

    /// <summary>Where completed runs go; there is one place for each of the eight.</summary>
    public static string Completed(int index) => $"completed{index}";

    /// <summary>
    /// A shuffled deal of 104 cards in <paramref name="suits"/> suits (1, 2 or 4): the first four columns
    /// get six cards, the rest five, only the last face up; the other 50 wait in the stock.
    /// </summary>
    public static Table Deal(int seed, int suits)
    {
        var used = suits switch
        {
            1 => new[] { Suit.Spades },
            2 => new[] { Suit.Spades, Suit.Hearts },
            4 => Enum.GetValues<Suit>(),
            _ => throw new ArgumentOutOfRangeException(nameof(suits), suits, "Spider is played with 1, 2 or 4 suits."),
        };

        var table = new Table();
        var stock = table.Add(Stock);
        for (var i = 0; i < Runs; i++)
        {
            table.Add(Completed(i));
        }

        var columns = Enumerable.Range(0, Columns).Select(i => table.Add(Column(i))).ToArray();
        var cards = Deck.Suits(Enumerable.Range(0, Runs).Select(i => used[i % used.Length]));
        var deck = new Queue<Card>(Deck.Shuffled(cards, new Random(seed)));
        for (var column = 0; column < Columns; column++)
        {
            var count = column < 4 ? 6 : 5;
            for (var row = 0; row < count; row++)
            {
                columns[column].Deal(deck.Dequeue(), faceUp: row == count - 1);
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
            case DragCards drag when CanMove(drag.From, drag.Index, drag.To):
                Move(turn, table, drag.From, drag.Index, drag.To);
                return true;
            case ClickPile click when click.Index >= 0 && BestTarget(table, click.Pile, click.Index) is { } target:
                Move(turn, table, click.Pile, click.Index, target);
                return true;
            default:
                return false;
        }
    }

    public bool IsWon(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Enumerable.Range(0, Runs).All(i => table[Completed(i)].Count == Card.King);
    }

    /// <summary>
    /// Moves worth making, best first: onto the same suit, then moves that uncover a card, then onto
    /// any card one higher, then into an empty column; dealing from the stock last.
    /// </summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var columns = ColumnsOf(table);
        var moves = new List<(DragCards Move, int Score)>();
        foreach (var source in columns.Where(c => !c.IsEmpty))
        {
            var head = RunStart(source);
            foreach (var target in columns)
            {
                if (target == source || !CanMove(source, head, target))
                {
                    continue;
                }

                var uncovers = head > 0 && !source[head - 1].FaceUp;
                var sameSuit = !target.IsEmpty && target.Top!.Value.Card.Suit == source[head].Card.Suit;
                var breaksRun = head > 0 && source[head - 1].FaceUp && IsNext(source[head - 1].Card, source[head].Card);

                // Moving off a card it already sits on in sequence (same rank step) gains nothing unless it joins its own suit.
                if (breaksRun && !sameSuit)
                {
                    continue;
                }

                if (target.IsEmpty && head == 0)
                {
                    continue; // a whole column into an empty one just moves it
                }

                var score = (sameSuit ? 4 : 0) + (uncovers ? 2 : 0) + (target.IsEmpty ? -1 : 1);
                moves.Add((new DragCards(source, head, target), score));
            }
        }

        foreach (var (move, _) in moves.OrderByDescending(m => m.Score))
        {
            yield return move;
        }

        var stock = table[Stock];
        if (!stock.IsEmpty && columns.All(c => !c.IsEmpty))
        {
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Whether the cards from <paramref name="index"/> up in <paramref name="from"/> may go onto <paramref name="to"/>.</summary>
    public static bool CanMove(Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || !IsColumn(from) || !IsColumn(to) || index < 0 || index >= from.Count || !IsMovableRun(from, index))
        {
            return false;
        }

        return to.IsEmpty || to.Top!.Value.Card.Rank == from[index].Card.Rank + 1;
    }

    /// <summary>Whether the cards from <paramref name="index"/> up are face up, one suit, and each one lower.</summary>
    public static bool IsMovableRun(Pile pile, int index)
    {
        ArgumentNullException.ThrowIfNull(pile);
        if (index < 0 || index >= pile.Count || !pile[index].FaceUp)
        {
            return false;
        }

        for (var i = index + 1; i < pile.Count; i++)
        {
            var below = pile[i - 1].Card;
            var above = pile[i].Card;
            if (!pile[i].FaceUp || above.Suit != below.Suit || above.Rank != below.Rank - 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a deal is allowed now: cards left in the stock and no empty column.</summary>
    public static bool CanDeal(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return !table[Stock].IsEmpty && ColumnsOf(table).All(c => !c.IsEmpty);
    }

    private static bool DealRow(CardTurnBuilder turn, Table table)
    {
        if (!CanDeal(table))
        {
            return false;
        }

        var stock = table[Stock];
        foreach (var column in ColumnsOf(table))
        {
            turn.MoveTop(stock, column);
            turn.Flip(column, column.Count - 1, faceUp: true);
        }

        turn.Tag(DealTag);

        // A dealt card can finish a run.
        foreach (var column in ColumnsOf(table))
        {
            CollectRun(turn, table, column);
        }

        return true;
    }

    private static void Move(CardTurnBuilder turn, Table table, Pile from, int index, Pile to)
    {
        turn.Move(from, index, to);
        Reveal(turn, from);
        CollectRun(turn, table, to);
    }

    /// <summary>Takes a complete king-to-ace run of one suit off the top of <paramref name="column"/>.</summary>
    private static void CollectRun(CardTurnBuilder turn, Table table, Pile column)
    {
        if (column.Count < Card.King)
        {
            return;
        }

        var start = column.Count - Card.King;
        if (column[start].Card.Rank != Card.King || !IsMovableRun(column, start))
        {
            return;
        }

        var place = Enumerable.Range(0, Runs).Select(i => table[Completed(i)]).First(p => p.IsEmpty);
        turn.Move(column, start, place);
        turn.Tag(CompleteTag);
        Reveal(turn, column);
    }

    private static void Reveal(CardTurnBuilder turn, Pile column)
    {
        if (column.Top is { FaceUp: false })
        {
            turn.Flip(column, column.Count - 1, faceUp: true);
            turn.Tag(RevealTag);
        }
    }

    /// <summary>A tap: onto the same suit, else any card one higher, else an empty column.</summary>
    private static Pile? BestTarget(Table table, Pile from, int index)
    {
        if (!IsColumn(from) || !IsMovableRun(from, index))
        {
            return null;
        }

        var moving = from[index].Card;
        var targets = ColumnsOf(table).Where(c => CanMove(from, index, c)).ToArray();
        return targets.FirstOrDefault(c => !c.IsEmpty && c.Top!.Value.Card.Suit == moving.Suit)
            ?? targets.FirstOrDefault(c => !c.IsEmpty)
            ?? (index > 0 ? targets.FirstOrDefault(c => c.IsEmpty) : null);
    }

    /// <summary>The lowest index from which the top of the column is a movable run.</summary>
    private static int RunStart(Pile pile)
    {
        var index = pile.Count - 1;
        while (index > 0 && IsMovableRun(pile, index - 1))
        {
            index--;
        }

        return index;
    }

    private static bool IsNext(Card below, Card above) => above.Rank == below.Rank - 1;

    private static Pile[] ColumnsOf(Table table) => Enumerable.Range(0, Columns).Select(i => table[Column(i)]).ToArray();

    private static bool IsColumn(Pile pile) => pile.Name.StartsWith("column", StringComparison.Ordinal);
}
