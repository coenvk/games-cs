using Jentl.Cards;

namespace TriPeaks;

/// <summary>
/// TriPeaks solitaire: 28 cards in three overlapping peaks of four rows, only the bottom row face up. Play
/// an uncovered card onto the waste when it is one rank above or below the waste's top card (king and ace
/// are next to each other). A card turns face up once nothing covers it. The stock turns over one card at a
/// time onto the waste and is gone through once. Clear the peaks to win.
/// </summary>
public sealed class TriPeaksRules : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const int Peaks = 3;
    public const int Rows = 4;
    public const int Cards = 28;

    /// <summary>The width of the bottom row in cards; the layout is this many cards wide.</summary>
    public const int Width = 10;

    public const string DrawTag = "draw";
    public const string PlayTag = "play";
    public const string RevealTag = "reveal";

    // Per position: its row (0 at the top) and its centre in half-card widths from the left edge.
    private static readonly (int Row, int HalfX)[] Layout = BuildLayout();

    // Per position: the positions in the row below that cover it.
    private static readonly int[][] CoveredBy = Enumerable.Range(0, Cards)
        .Select(i => Enumerable.Range(0, Cards)
            .Where(j => Layout[j].Row == Layout[i].Row + 1 && Math.Abs(Layout[j].HalfX - Layout[i].HalfX) == 1)
            .ToArray())
        .ToArray();

    public static string Position(int index)
    {
        if (index is < 0 or >= Cards)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Positions run from 0 to {Cards - 1}.");
        }

        return $"peak{index}";
    }

    /// <summary>The row of a position, 0 (the three tips) to 3 (the face-up bottom row).</summary>
    public static int RowOf(int index) => Layout[index].Row;

    /// <summary>Where a position sits across the layout, in card widths from the left edge of the bottom row.</summary>
    public static float ColumnOf(int index) => (Layout[index].HalfX - 1) / 2f;

    /// <summary>A shuffled deal: the peaks top row first, the bottom row face up; one card on the waste, 23 in the stock.</summary>
    public static Table Deal(int seed)
    {
        var table = new Table();
        var stock = table.Add(Stock);
        var waste = table.Add(Waste);
        var deck = new Queue<Card>(Deck.Shuffled(Deck.Standard52(), new Random(seed)));
        for (var i = 0; i < Cards; i++)
        {
            table.Add(Position(i)).Deal(deck.Dequeue(), faceUp: Layout[i].Row == Rows - 1);
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
            case DragCards { To.Name: Waste } drag when CanPlay(table, drag.From):
                PlayCard(turn, table, drag.From);
                return true;
            default:
                return false;
        }
    }

    public bool IsWon(Table table) => PeakPiles(table).All(p => p.IsEmpty);

    /// <summary>
    /// Playable cards, those that uncover a face-down card first and then the lower rows; the stock when
    /// nothing plays.
    /// </summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (IsWon(table))
        {
            yield break;
        }

        var playable = Enumerable.Range(0, Cards)
            .Where(i => CanPlay(table, table[Position(i)]))
            .OrderByDescending(i => Uncovers(table, i))
            .ThenBy(i => Layout[i].Row)
            .ToArray();
        foreach (var i in playable)
        {
            yield return new ClickPile(table[Position(i)], 0);
        }

        var stock = table[Stock];
        if (!stock.IsEmpty)
        {
            yield return new ClickPile(stock, stock.Count - 1);
        }
    }

    /// <summary>Whether <paramref name="pile"/> is a peak card with nothing on it (it is then face up).</summary>
    public static bool IsFree(Table table, Pile pile)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(pile);
        return !pile.IsEmpty && IndexOf(pile) is { } index && CoveredBy[index].All(j => table[Position(j)].IsEmpty);
    }

    /// <summary>Whether the card in <paramref name="pile"/> is free and one rank above or below the waste's top card.</summary>
    public static bool CanPlay(Table table, Pile pile)
    {
        ArgumentNullException.ThrowIfNull(table);
        return IsFree(table, pile) && table[Waste].Top is { } top && IsNext(top.Card, pile.Top!.Value.Card);
    }

    /// <summary>One rank apart, with king and ace next to each other.</summary>
    public static bool IsNext(Card a, Card b) => Math.Abs(a.Rank - b.Rank) is 1 or Card.King - Card.Ace;

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

    private static void PlayCard(CardTurnBuilder turn, Table table, Pile pile)
    {
        turn.MoveTop(pile, table[Waste]);
        turn.Tag(PlayTag);

        // Turn up every card this one was the last to cover.
        for (var i = 0; i < Cards; i++)
        {
            var other = table[Position(i)];
            if (other.Top is { FaceUp: false } && IsFree(table, other))
            {
                turn.Flip(other, 0, faceUp: true);
                turn.Tag(RevealTag);
            }
        }
    }

    // How many face-down cards playing position i would turn up.
    private static int Uncovers(Table table, int index) => Enumerable.Range(0, Cards).Count(i =>
        CoveredBy[i].Contains(index)
        && table[Position(i)].Top is { FaceUp: false }
        && CoveredBy[i].All(j => j == index || table[Position(j)].IsEmpty));

    private static int? IndexOf(Pile pile) =>
        pile.Name.StartsWith("peak", StringComparison.Ordinal)
        && int.TryParse(pile.Name.AsSpan("peak".Length), System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? index
            : null;

    private static IEnumerable<Pile> PeakPiles(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Enumerable.Range(0, Cards).Select(i => table[Position(i)]);
    }

    private static (int Row, int HalfX)[] BuildLayout()
    {
        // In half-card widths, a card's centre: the bottom row's ten cards sit at 1, 3, ... 19; each row up
        // sits between the two cards below it, and the three peaks are three card widths apart.
        var layout = new List<(int, int)>();
        for (var peak = 0; peak < Peaks; peak++)
        {
            layout.Add((0, 6 * peak + 4));
        }

        for (var peak = 0; peak < Peaks; peak++)
        {
            for (var j = 0; j < 2; j++)
            {
                layout.Add((1, 6 * peak + 3 + 2 * j));
            }
        }

        for (var i = 0; i < 9; i++)
        {
            layout.Add((2, 2 * i + 2));
        }

        for (var i = 0; i < Width; i++)
        {
            layout.Add((3, 2 * i + 1));
        }

        return [.. layout];
    }
}
