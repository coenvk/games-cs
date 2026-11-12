namespace ConnectWord;

/// <summary>A card on the grid: one word, or several of a category merged into one.</summary>
public sealed class WordCard
{
    internal WordCard(int id, int category, IReadOnlyList<string> words)
    {
        Id = id;
        Category = category;
        Words = words;
    }

    /// <summary>Stays the same while the card moves and grows, so the screen can follow it.</summary>
    public int Id { get; }

    /// <summary>Which of the level's categories it belongs to (hidden from the player until matched).</summary>
    public int Category { get; }

    public IReadOnlyList<string> Words { get; private set; }

    public bool IsMerged => Words.Count > 1;

    internal void Absorb(WordCard other) => Words = [.. Words, .. other.Words];

    public override string ToString() => string.Join(" + ", Words);
}

/// <summary>A place on the grid: column from the left, row from the top.</summary>
public readonly record struct Cell(int Column, int Row);

/// <summary>What a move did.</summary>
public enum MoveKind
{
    /// <summary>Nothing: the same cell, an empty start, or the game is over.</summary>
    None,

    /// <summary>To an empty cell.</summary>
    Moved,

    /// <summary>Two cards of different categories traded places.</summary>
    Swapped,

    /// <summary>Two cards of the same category became one.</summary>
    Merged,
}

/// <summary>Something that happened, for the screen to show.</summary>
public abstract record WordEvent;

public sealed record CardsMerged(WordCard Card, Cell At) : WordEvent;

/// <summary>A category had all its words in one row: its cards left the grid as a solved row.</summary>
public sealed record CategoryMatched(int Category, int Row, IReadOnlyList<WordCard> Cards) : WordEvent;

/// <summary>A new card came in from the deck at the top of a column.</summary>
public sealed record CardDealt(WordCard Card, Cell At) : WordEvent;

public sealed record LevelWon(int Moves, int Stars) : WordEvent;

/// <summary>
/// Connect Word: a grid of word cards (more wait in a deck) from a level's categories. Drop a card on one of the same
/// category and they merge into one card; on one of another category, or an empty cell, and they trade places. When a
/// row holds every word of a category (in separate cards, merged ones, or one card with them all), the category is
/// matched: its cards leave the grid as a solved row. Cards above fall into the gaps and new ones drop in from the
/// deck. Match every category to win. Seeded, so a level and seed always deal the same.
/// </summary>
public sealed class ConnectWordGame
{
    private readonly WordCard?[,] _grid;
    private readonly Queue<WordCard> _deck;
    private readonly List<int> _solved = [];
    private readonly List<WordEvent> _events = [];
    private readonly List<(Cell From, Cell To)> _history = [];

    public ConnectWordGame(WordLevel level, int seed, int columns = 4, int rows = 4)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 2);
        Level = level;
        Seed = seed;
        Columns = columns;
        Rows = rows;
        _grid = new WordCard?[columns, rows];
        var id = 0;
        var cards = level.Categories.SelectMany((category, index) => category.Words.Select(word => new WordCard(id++, index, [word]))).ToArray();
        new Random(seed).Shuffle(cards);
        _deck = new Queue<WordCard>(cards);
        Settle();
        _events.Clear();
    }

    public WordLevel Level { get; }

    public int Seed { get; }

    /// <summary>The moves made, in order.</summary>
    public IReadOnlyList<(Cell From, Cell To)> History => _history;

    public int Columns { get; }

    public int Rows { get; }

    public int Moves { get; private set; }

    /// <summary>The categories matched so far, in the order they were.</summary>
    public IReadOnlyList<int> Solved => _solved;

    /// <summary>Cards still waiting to come in.</summary>
    public int DeckCount => _deck.Count;

    public bool IsWon => _solved.Count == Level.Categories.Count;

    /// <summary>A move per merge or swap, if every merge were needed: three stars at or under it.</summary>
    public int Par => Level.WordCount - Level.Categories.Count;

    /// <summary>Three stars at or under <see cref="Par"/>, two within half as many again, one otherwise.</summary>
    public int Stars => Moves <= Par ? 3 : Moves <= Par * 3 / 2 ? 2 : 1;

    public WordCard? this[Cell cell] => _grid[cell.Column, cell.Row];

    /// <summary>Every card on the grid with where it is.</summary>
    public IEnumerable<(Cell Cell, WordCard Card)> Cards
    {
        get
        {
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    if (_grid[column, row] is { } card)
                    {
                        yield return (new Cell(column, row), card);
                    }
                }
            }
        }
    }

    /// <summary>The events since the last call, oldest first.</summary>
    public IReadOnlyList<WordEvent> DrainEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    /// <summary>Drops the card at <paramref name="from"/> on <paramref name="to"/>: merges, swaps or moves it, then matches rows and refills.</summary>
    public MoveKind Drop(Cell from, Cell to)
    {
        if (IsWon || from == to || !Inside(from) || !Inside(to) || this[from] is not { } card)
        {
            return MoveKind.None;
        }

        MoveKind kind;
        if (this[to] is not { } target)
        {
            (_grid[to.Column, to.Row], _grid[from.Column, from.Row]) = (card, null);
            kind = MoveKind.Moved;
        }
        else if (target.Category == card.Category)
        {
            target.Absorb(card);
            _grid[from.Column, from.Row] = null;
            _events.Add(new CardsMerged(target, to));
            kind = MoveKind.Merged;
        }
        else
        {
            (_grid[to.Column, to.Row], _grid[from.Column, from.Row]) = (card, target);
            kind = MoveKind.Swapped;
        }

        Moves++;
        _history.Add((from, to));
        Settle();
        if (IsWon)
        {
            _events.Add(new LevelWon(Moves, Stars));
        }

        return kind;
    }

    /// <summary>Two cards that belong together (the pair that makes the largest card first), or null.</summary>
    public (Cell From, Cell To)? Hint() => Cards
        .GroupBy(c => c.Card.Category)
        .Where(g => g.Count() > 1)
        .Select(g => g.OrderByDescending(c => c.Card.Words.Count).ToList())
        .OrderByDescending(g => g[0].Card.Words.Count + g[1].Card.Words.Count)
        .Select(g => ((Cell, Cell)?)(g[1].Cell, g[0].Cell))
        .FirstOrDefault();

    /// <summary>
    /// The game as it was before the last move: dealt again from the seed and played up to there, so the deck comes
    /// out the same. The same game when nothing has been moved.
    /// </summary>
    public ConnectWordGame Undone()
    {
        if (_history.Count == 0)
        {
            return this;
        }

        var game = new ConnectWordGame(Level, Seed, Columns, Rows);
        foreach (var (from, to) in _history.Take(_history.Count - 1))
        {
            game.Drop(from, to);
        }

        game.DrainEvents();
        return game;
    }

    private bool Inside(Cell cell) => cell.Column >= 0 && cell.Column < Columns && cell.Row >= 0 && cell.Row < Rows;

    /// <summary>Matches complete rows, lets cards fall and new ones in, and again until nothing changes.</summary>
    private void Settle()
    {
        do
        {
            Fall();
        }
        while (Match());
    }

    private bool Match()
    {
        for (var row = 0; row < Rows; row++)
        {
            var inRow = Enumerable.Range(0, Columns).Select(c => _grid[c, row]).OfType<WordCard>().ToList();
            foreach (var group in inRow.GroupBy(c => c.Category))
            {
                if (group.Sum(c => c.Words.Count) != Level.Categories[group.Key].Words.Count)
                {
                    continue;
                }

                for (var column = 0; column < Columns; column++)
                {
                    if (_grid[column, row]?.Category == group.Key)
                    {
                        _grid[column, row] = null;
                    }
                }

                _solved.Add(group.Key);
                _events.Add(new CategoryMatched(group.Key, row, [.. group]));
                return true;
            }
        }

        return false;
    }

    // Cards fall to the bottom of their column; the deck fills the top.
    private void Fall()
    {
        for (var column = 0; column < Columns; column++)
        {
            var stack = Enumerable.Range(0, Rows).Reverse().Select(r => _grid[column, r]).OfType<WordCard>().ToList();
            for (var row = Rows - 1; row >= 0; row--)
            {
                var index = Rows - 1 - row;
                if (index < stack.Count)
                {
                    _grid[column, row] = stack[index];
                }
                else if (_deck.TryDequeue(out var card))
                {
                    _grid[column, row] = card;
                    _events.Add(new CardDealt(card, new Cell(column, row)));
                }
                else
                {
                    _grid[column, row] = null;
                }
            }
        }
    }
}
