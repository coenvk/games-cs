using Jentl.Cards;

namespace AssociateWords;

/// <summary>
/// Associate Words, a solitaire of categories: five columns with only their top card face up, a draw pile that turns
/// one card at a time onto a waste (and over again once empty), and a few slots. A category card opens an empty slot;
/// words of that category then go onto it, and once it holds them all the category is complete, leaves the table and
/// frees its slot. In the columns a word goes on a face-up word of its own category, in any order (the category card
/// too), a run of one category moves together, and any card goes in an empty column. Every move counts against the
/// level's limit: complete every category before the moves run out. A <see cref="CardGame"/> runs it, so moves undo.
/// A joker (★) goes into any open slot as one of its category's words; it can't open a slot, and in the columns it lies
/// only in an empty one, nothing on it. A category completed with a joker leaves one of its words over, which may then
/// go straight to the finished pile.
/// </summary>
public sealed class AssociateGame : ICardRules
{
    public const string Stock = "stock";
    public const string Waste = "waste";
    public const string Done = "done";
    public const string CategoryTag = "category";
    public const string JokerTag = "joker";

    /// <summary>The joker's category: its own, matching none.</summary>
    public const string JokerGroup = "★";

    private const string MovesCounter = "moves";

    public AssociateGame(AssociateLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Level = level;
        Table = new Table();
        var stock = Table.Add(Stock);
        Table.Add(Waste);
        Slots = [.. Enumerable.Range(0, level.Slots).Select(i => Table.Add($"slot{i}"))];
        Columns = [.. Enumerable.Range(0, AssociateLevels.ColumnCount).Select(i => Table.Add($"column{i}"))];
        Table.Add(Done);

        var jokers = Enumerable.Range(0, level.Jokers).Select(_ => new Card(new CardFace("Joker", JokerGroup)
        {
            Label = "★",
            Tags = [JokerTag],
            Fill = "#c9a7f5",
            Code = "★",
        }));
        var cards = new Queue<Card>(Deck.Shuffled(level.Groups.SelectMany(Cards).Concat(jokers), new Random(level.Seed)));
        for (var i = 0; i < Columns.Count; i++)
        {
            for (var n = 0; n < level.Columns[i]; n++)
            {
                Columns[i].Deal(cards.Dequeue(), faceUp: n == level.Columns[i] - 1);
            }
        }

        while (cards.TryDequeue(out var card))
        {
            stock.Deal(card, faceUp: false);
        }

        Game = new CardGame(Table, this);
    }

    public AssociateLevel Level { get; }

    public Table Table { get; }

    /// <summary>The game itself: play inputs through it, undo, and read its history.</summary>
    public CardGame Game { get; }

    public IReadOnlyList<Pile> Slots { get; }

    public IReadOnlyList<Pile> Columns { get; }

    public int MovesUsed => Table.Counter(MovesCounter);

    public int MovesLeft => Level.Moves - MovesUsed;

    public bool IsCleared => IsWon(Table);

    /// <summary>Out of moves with categories left to complete.</summary>
    public bool IsLost => !IsCleared && MovesLeft <= 0;

    /// <summary>Categories completed so far.</summary>
    public int Completed => Table[Done].Cards.Count(c => IsCategory(c.Card));

    /// <summary>Three stars with two fifths of the moves to spare, two with a fifth, one otherwise.</summary>
    public int Stars => MovesLeft * 5 >= Level.Moves * 2 ? 3 : MovesLeft * 5 >= Level.Moves ? 2 : 1;

    public static bool IsCategory(Card card) => card.Face.Has(CategoryTag);

    public static bool IsJoker(Card card) => card.Face.Has(JokerTag);

    /// <summary>Whether a category has been completed and left the table.</summary>
    public bool IsFinished(string group) => Table[Done].Cards.Any(c => IsCategory(c.Card) && GroupOf(c.Card) == group);

    /// <summary>The category a card belongs to (a category card to its own).</summary>
    public static string GroupOf(Card card) => card.Face.Suit!;

    public bool IsSlot(Pile pile) => Slots.Contains(pile);

    public bool IsColumn(Pile pile) => Columns.Contains(pile);

    /// <summary>The category open in a slot, or null when it is empty.</summary>
    public static string? OpenIn(Pile slot) => slot.IsEmpty ? null : GroupOf(slot[0].Card);

    /// <summary>How many of its category's cards a slot holds, and how many it needs.</summary>
    public (int Held, int Needed) Progress(Pile slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return OpenIn(slot) is { } group ? (slot.Count, Level.Groups.Single(g => g.Name == group).Words.Count + 1) : (0, 0);
    }

    /// <summary>Every category complete and every word gone; a joker left unused doesn't matter.</summary>
    public bool IsWon(Table table) => Table[Done].Cards.Count(c => !IsJoker(c.Card)) == Level.CardCount - Level.Jokers;

    /// <summary>Whether the cards from <paramref name="index"/> up may be picked up: the waste's top, or a run of one category on top of a column.</summary>
    public bool CanTake(Pile pile, int index)
    {
        ArgumentNullException.ThrowIfNull(pile);
        if (index < 0 || index >= pile.Count || !pile[index].FaceUp)
        {
            return false;
        }

        if (pile.Name == Waste)
        {
            return index == pile.Count - 1;
        }

        var group = GroupOf(pile[index].Card);
        return IsColumn(pile) && Enumerable.Range(index, pile.Count - index).All(i => pile[i].FaceUp && GroupOf(pile[i].Card) == group);
    }

    /// <summary>Whether the cards from <paramref name="index"/> up in <paramref name="from"/> may go on <paramref name="to"/>.</summary>
    public bool Fits(Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from == to || !CanTake(from, index))
        {
            return false;
        }

        var group = GroupOf(from[index].Card);
        if (IsSlot(to))
        {
            // An empty slot opens with the category card (alone, or with words of its own); an open one takes its
            // words, and a joker for one of them.
            return to.IsEmpty
                ? Enumerable.Range(index, from.Count - index).Any(i => IsCategory(from[i].Card))
                : OpenIn(to) == group || IsJoker(from[index].Card);
        }

        // A word whose category is complete (one a joker stood in for) goes to the finished pile.
        if (to.Name == Done)
        {
            return IsFinished(group);
        }

        // A joker only into an empty column, and nothing on a joker.
        return IsColumn(to) && (to.Top is not { } top ? true : !IsJoker(from[index].Card) && top.FaceUp && GroupOf(top.Card) == group);
    }

    public bool Play(CardTurnBuilder turn, CardInput input)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (IsCleared || IsLost)
        {
            return false;
        }

        switch (input)
        {
            case DragCards drag when Fits(drag.From, drag.Index, drag.To):
                Move(turn, drag.From, drag.Index, drag.To);
                return true;
            case ClickPile { Pile.Name: Stock }:
                return Draw(turn);
            case ClickPile click when CanTake(click.Pile, click.Index) && Target(click.Pile, click.Index) is { } to:
                Move(turn, click.Pile, click.Index, to);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Useful moves, best first: words into their open slot, a category card into an empty one, moves that turn a
    /// face-down card over, putting a waste card with its own kind, then the draw pile.
    /// </summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        if (IsCleared || IsLost)
        {
            return [];
        }

        var moves = new List<(int Score, CardInput Move)>();
        foreach (var from in Columns.Append(Table[Waste]))
        {
            for (var index = 0; index < from.Count; index++)
            {
                if (!CanTake(from, index))
                {
                    continue;
                }

                foreach (var to in Slots.Concat(Columns).Append(Table[Done]))
                {
                    if (!Fits(from, index, to))
                    {
                        continue;
                    }

                    var reveals = index > 0 && !from[index - 1].FaceUp;
                    var empties = index == 0 && IsColumn(from);
                    int score;
                    if (to.Name == Done)
                    {
                        score = 110;
                    }
                    else if (IsSlot(to))
                    {
                        // A joker best goes where it finishes a category, else where most is still missing.
                        score = to.IsEmpty ? 80 : IsJoker(from[index].Card) ? 90 + Progress(to).Held - Progress(to).Needed : 100;
                    }
                    else if (to.IsEmpty)
                    {
                        // Into an empty column only to uncover something.
                        if (!reveals)
                        {
                            continue;
                        }

                        score = 20;
                    }
                    else
                    {
                        score = reveals ? 60 : empties ? 50 : from.Name == Waste ? 40 : 0;
                        if (score == 0)
                        {
                            continue;
                        }
                    }

                    moves.Add((score + (reveals ? 10 : 0), new DragCards(from, index, to)));
                }
            }
        }

        var hints = moves.OrderByDescending(m => m.Score).Select(m => m.Move).ToList();
        var stock = Table[Stock];
        if (!stock.IsEmpty || !Table[Waste].IsEmpty)
        {
            hints.Add(new ClickPile(stock, stock.Count - 1));
        }

        return hints;
    }

    private static IEnumerable<Card> Cards(WordGroup group)
    {
        var category = new CardFace(group.Name, group.Name)
        {
            Label = group.Name,
            Tags = [CategoryTag],
            Fill = "#f6c848",
            Code = $"[{group.Name}]",
        };
        yield return new Card(category);
        foreach (var word in group.Words)
        {
            yield return new Card(new CardFace(word, group.Name) { Label = word, Code = word });
        }
    }

    // Where a tapped card goes: the finished pile for a word left over, its open slot (or an empty one, for a category
    // card; the fullest open one, for a joker), else a column of its kind.
    private Pile? Target(Pile from, int index) =>
        (Fits(from, index, Table[Done]) ? Table[Done] : null)
        ?? Slots.Where(s => !s.IsEmpty && Fits(from, index, s)).OrderByDescending(s => Progress(s).Held - Progress(s).Needed).FirstOrDefault()
        ?? Slots.FirstOrDefault(s => s.IsEmpty && Fits(from, index, s))
        ?? Columns.FirstOrDefault(c => !c.IsEmpty && Fits(from, index, c));

    private void Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
        turn.Move(from, index, to);
        turn.Count(MovesCounter);
        turn.Tag(IsSlot(to) ? "slot" : to.Name == Done ? "complete" : "move");
        if (IsColumn(from) && from.Top is { FaceUp: false })
        {
            turn.Flip(from, from.Count - 1, faceUp: true);
            turn.Tag("reveal");
        }

        // Complete: the category card and all its words; they leave and the slot is free again.
        if (IsSlot(to) && Progress(to) is var (held, needed) && held == needed)
        {
            turn.Move(to, 0, Table[Done]);
            turn.Tag("complete");
        }
    }

    private bool Draw(CardTurnBuilder turn)
    {
        var (stock, waste) = (Table[Stock], Table[Waste]);
        if (!stock.IsEmpty)
        {
            turn.MoveTop(stock, waste);
            turn.Flip(waste, waste.Count - 1, faceUp: true);
            turn.Tag("draw");
        }
        else if (!waste.IsEmpty)
        {
            while (!waste.IsEmpty)
            {
                turn.MoveTop(waste, stock);
                turn.Flip(stock, stock.Count - 1, faceUp: false);
            }

            turn.Tag("recycle");
        }
        else
        {
            return false;
        }

        turn.Count(MovesCounter);
        return true;
    }
}
