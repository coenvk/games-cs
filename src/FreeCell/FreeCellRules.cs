using Jentl.Cards;

namespace FreeCell;

/// <summary>
/// FreeCell: every card is dealt face up into eight cascades. Build the four foundations up by suit;
/// cascades build down in alternating colours, and any card may start an empty cascade. Each of the
/// four free cells holds one card. A run moves as a unit only if there is room to move it card by
/// card through the empty free cells and cascades (the "supermove" rule).
/// </summary>
public sealed class FreeCellRules : ICardRules
{
    public const int FreeCells = 4;
    public const int Foundations = 4;
    public const int Cascades = 8;

    public const string FoundationTag = "foundation";

    public static string FreeCell(int index) => $"freecell{index}";

    public static string Foundation(int index) => $"foundation{index}";

    public static string Cascade(int index) => $"cascade{index}";

    /// <summary>
    /// Deals game <paramref name="number"/> exactly as Microsoft FreeCell does, so deal numbers match the
    /// classic game (deal 1 starts with JD 2D 9H JC 5D 7H 7C 5H). Cards go round the eight cascades.
    /// </summary>
    public static Table Deal(int number)
    {
        var table = new Table();
        for (var i = 0; i < FreeCells; i++)
        {
            table.Add(FreeCell(i));
        }

        for (var i = 0; i < Foundations; i++)
        {
            table.Add(Foundation(i));
        }

        var cascades = Enumerable.Range(0, Cascades).Select(i => table.Add(Cascade(i))).ToArray();
        var dealt = 0;
        foreach (var card in MicrosoftOrder(number))
        {
            cascades[dealt++ % Cascades].Deal(card, faceUp: true);
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

    public bool IsWon(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Enumerable.Range(0, Foundations).All(i => table[Foundation(i)].Count == Card.King);
    }

    /// <summary>Moves worth making, best first: home, onto other cascades, into an empty cascade, then into a free cell.</summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sources = FreeCellsOf(table).Concat(CascadesOf(table)).Where(p => !p.IsEmpty).ToArray();

        foreach (var source in sources)
        {
            if (FoundationFor(table, source.Top!.Value.Card) is { } home)
            {
                yield return new DragCards(source, source.Count - 1, home);
            }
        }

        // Onto a card: from a free cell first (frees space), then the longest movable runs.
        foreach (var source in sources)
        {
            for (var index = RunStart(source); index < source.Count; index++)
            {
                foreach (var target in CascadesOf(table).Where(c => !c.IsEmpty && c != source))
                {
                    if (CanMove(table, source, index, target))
                    {
                        yield return new DragCards(source, index, target);
                    }
                }
            }
        }

        // Into an empty cascade, only when that does not just move a whole cascade across.
        foreach (var source in sources)
        {
            var index = RunStart(source);
            if (IsCascade(source) && index == 0)
            {
                continue;
            }

            if (CascadesOf(table).FirstOrDefault(c => c.IsEmpty) is { } empty && CanMove(table, source, index, empty))
            {
                yield return new DragCards(source, index, empty);
            }
        }

        if (FreeCellsOf(table).FirstOrDefault(c => c.IsEmpty) is { } cell)
        {
            foreach (var source in CascadesOf(table).Where(c => c.Count > 1))
            {
                yield return new DragCards(source, source.Count - 1, cell);
            }
        }
    }

    /// <summary>
    /// Sends home a card nothing can still need: aces and twos always; otherwise a card once both
    /// foundations of the other colour have reached the rank below it (so no lower card of the other
    /// colour could still want to be built on it).
    /// </summary>
    public CardInput? AutoMove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var source in FreeCellsOf(table).Concat(CascadesOf(table)))
        {
            if (source.IsEmpty)
            {
                continue;
            }

            var card = source.Top!.Value.Card;
            if (FoundationFor(table, card) is { } home && IsSafeToSendHome(table, card))
            {
                return new DragCards(source, source.Count - 1, home);
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
        if (from == to || index < 0 || index >= from.Count || IsFoundation(from) || !IsRun(from, index))
        {
            return false;
        }

        var count = from.Count - index;
        var moving = from[index].Card;
        if (IsFoundation(to))
        {
            return count == 1 && (to.IsEmpty ? moving.Rank == Card.Ace : to.Top!.Value.Card.Suit == moving.Suit && to.Top.Value.Card.Rank == moving.Rank - 1);
        }

        if (IsFreeCell(to))
        {
            return count == 1 && to.IsEmpty;
        }

        var fits = to.IsEmpty || (to.Top!.Value.Card.Color != moving.Color && to.Top.Value.Card.Rank == moving.Rank + 1);
        return fits && count <= Capacity(table, to);
    }

    /// <summary>
    /// The longest run that can move onto <paramref name="target"/>: one more than the empty free
    /// cells, doubled for every empty cascade other than the target.
    /// </summary>
    public static int Capacity(Table table, Pile target)
    {
        ArgumentNullException.ThrowIfNull(table);
        var cells = FreeCellsOf(table).Count(c => c.IsEmpty);
        var cascades = CascadesOf(table).Count(c => c.IsEmpty && c != target);
        return (cells + 1) << cascades;
    }

    /// <summary>Whether the cards from <paramref name="index"/> up form a run: alternating colours, each one lower.</summary>
    public static bool IsRun(Pile pile, int index)
    {
        ArgumentNullException.ThrowIfNull(pile);
        for (var i = index + 1; i < pile.Count; i++)
        {
            var below = pile[i - 1].Card;
            var above = pile[i].Card;
            if (above.Color == below.Color || above.Rank != below.Rank - 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Microsoft's deal: a linear congruential generator picks cards from a deck ordered
    /// AC AD AH AS 2C ... KS, swapping the pick with the last remaining card.
    /// </summary>
    private static IEnumerable<Card> MicrosoftOrder(int number)
    {
        var deck = Enumerable.Range(0, 52).ToArray();
        var seed = (uint)number;
        for (var left = 52; left > 0; left--)
        {
            seed = (seed * 214013 + 2531011) & 0x7FFFFFFF;
            var pick = (int)(seed >> 16) % left;
            var card = deck[pick];
            deck[pick] = deck[left - 1];
            yield return new Card(card / 4 + 1, (card % 4) switch
            {
                0 => Suit.Clubs,
                1 => Suit.Diamonds,
                2 => Suit.Hearts,
                _ => Suit.Spades,
            });
        }
    }

    private static void Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
        turn.Move(from, index, to);
        if (IsFoundation(to))
        {
            turn.Tag(FoundationTag);
        }
    }

    /// <summary>A tap: home if it can go, else onto a card, else an empty cascade, else a free cell.</summary>
    private static Pile? BestTarget(Table table, Pile from, int index)
    {
        if (index == from.Count - 1 && FoundationFor(table, from[index].Card) is { } home && CanMove(table, from, index, home))
        {
            return home;
        }

        return CascadesOf(table).Where(c => !c.IsEmpty).FirstOrDefault(c => CanMove(table, from, index, c))
            ?? (IsCascade(from) && index == 0 ? null : CascadesOf(table).FirstOrDefault(c => c.IsEmpty && CanMove(table, from, index, c)))
            ?? (IsFreeCell(from) ? null : FreeCellsOf(table).FirstOrDefault(c => CanMove(table, from, index, c)));
    }

    private static bool IsSafeToSendHome(Table table, Card card)
    {
        if (card.Rank <= 2)
        {
            return true;
        }

        var otherColour = Enumerable.Range(0, Foundations)
            .Select(i => table[Foundation(i)])
            .Where(f => !f.IsEmpty && f.Top!.Value.Card.Color != card.Color)
            .Select(f => f.Top!.Value.Card.Rank)
            .ToArray();
        return otherColour.Length == 2 && otherColour.All(rank => rank >= card.Rank - 1);
    }

    private static Pile? FoundationFor(Table table, Card card) =>
        Enumerable.Range(0, Foundations)
            .Select(i => table[Foundation(i)])
            .FirstOrDefault(f => f.IsEmpty ? card.Rank == Card.Ace : f.Top!.Value.Card.Suit == card.Suit && f.Top.Value.Card.Rank == card.Rank - 1);

    /// <summary>The lowest index from which the top of the pile is a run.</summary>
    private static int RunStart(Pile pile)
    {
        var index = pile.Count - 1;
        while (index > 0 && IsRun(pile, index - 1))
        {
            index--;
        }

        return index;
    }

    private static IEnumerable<Pile> FreeCellsOf(Table table) => Enumerable.Range(0, FreeCells).Select(i => table[FreeCell(i)]);

    private static IEnumerable<Pile> CascadesOf(Table table) => Enumerable.Range(0, Cascades).Select(i => table[Cascade(i)]);

    private static bool IsFoundation(Pile pile) => pile.Name.StartsWith("foundation", StringComparison.Ordinal);

    private static bool IsFreeCell(Pile pile) => pile.Name.StartsWith("freecell", StringComparison.Ordinal);

    private static bool IsCascade(Pile pile) => pile.Name.StartsWith("cascade", StringComparison.Ordinal);
}
