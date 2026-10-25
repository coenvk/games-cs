using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker;
using Poker.Arena;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>What the player has chosen so far for a match or a batch; the setup and the seats screens both edit it.</summary>
internal sealed class MatchSetup
{
    public const int MaxSeats = 9;
    private static readonly int[] FirstBots = [3, 4, 5, 2, 0, 1, 3, 4, 5];

    public MatchSetup(IReadOnlyList<BotEntry> bots, bool batch, DealSeed seed)
    {
        // A bot whose description cannot be read has no command and cannot be seated.
        Bots = [.. bots.Where(b => b.Kind == BotKind.House || b.Command is not null)];
        IsBatch = batch;
        Seed = seed;
        Size = batch ? 2 : 4;
        HandsIndex = batch ? 4 : 3;
        Seats = [.. FirstBots.Select(i => i % Bots.Count)];
    }

    /// <summary>The numbers of hands a match or a duplicate batch can have.</summary>
    public static IReadOnlyList<int> HandChoices { get; } = [10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

    /// <summary>The numbers of tournaments a sit-and-go series can have; each takes many hands.</summary>
    public static IReadOnlyList<int> SeriesChoices { get; } = [5, 10, 20, 50, 100];

    public static IReadOnlyList<TimeSpan> TimeLimits { get; } = [.. new[] { 0.25, 0.5, 1, 2, 5 }.Select(TimeSpan.FromSeconds)];

    public IReadOnlyList<BotEntry> Bots { get; }

    public bool IsBatch { get; }

    public int Size { get; set; }

    /// <summary>The bot at each seat: an index into <see cref="Bots"/>, or <c>Bots.Count</c> for the player.</summary>
    public int[] Seats { get; }

    /// <summary>The choice for the format row: sit-and-go or fixed stacks, or a <see cref="BatchFormat"/>.</summary>
    public int Format { get; set; }

    public int HandsIndex { get; set; }

    public int SeriesIndex { get; set; } = 2;

    /// <summary>What the hands row offers: tournaments for a series, hands otherwise.</summary>
    public IReadOnlyList<int> CountChoices => IsSeries ? SeriesChoices : HandChoices;

    /// <summary>The choice in <see cref="CountChoices"/>; the series and the hands each keep their own.</summary>
    public int CountIndex
    {
        get => IsSeries ? SeriesIndex : HandsIndex;
        set
        {
            if (IsSeries)
            {
                SeriesIndex = value;
            }
            else
            {
                HandsIndex = value;
            }
        }
    }

    public int TimeIndex { get; set; } = 2;

    public DealSeed Seed { get; set; }

    public int You => Bots.Count;

    public bool IsHeadsUp => IsBatch && (BatchFormat)Format == BatchFormat.DuplicateHeadsUp;

    public bool IsSeries => IsBatch && (BatchFormat)Format == BatchFormat.SitAndGoSeries;

    public BotEntry? Bot(int seat) => Seats[seat] == You ? null : Bots[Seats[seat]];

    /// <summary>Gives <paramref name="seat"/> a bot; the player takes at most one seat.</summary>
    public void Choose(int seat, int choice, int direction)
    {
        if (choice == You && Enumerable.Range(0, Size).Any(s => s != seat && Seats[s] == You))
        {
            choice = (choice + direction + Bots.Count + 1) % (Bots.Count + 1);
        }

        Seats[seat] = choice;
    }

    /// <summary>Sets the table size; heads-up duplicate needs two seats, so a larger table changes it to the ring.</summary>
    public void Resize(int size)
    {
        Size = size;
        if (IsHeadsUp && size != 2)
        {
            Format = (int)BatchFormat.DuplicateRing;
        }

        // A player who was seated beyond the new size is no longer there; a smaller table keeps the seats in front.
        for (var s = size; s < MaxSeats; s++)
        {
            if (Seats[s] == You)
            {
                Seats[s] = FirstBots[s] % Bots.Count;
            }
        }
    }

    /// <summary>Picks a format; heads-up duplicate sits two.</summary>
    public void SetFormat(int format)
    {
        Format = format;
        if (IsHeadsUp)
        {
            Size = 2;
        }
    }

    public MatchRequest Match() => new(
        [.. Enumerable.Range(0, Size).Select(Bot)],
        new ArenaSettings((TableFormat)Format, HandChoices[HandsIndex], Seed, TimeLimits[TimeIndex]));

    public BatchRequest Batch() => new(
        [.. Enumerable.Range(0, Size).Select(s => Bots[Seats[s]])],
        new BatchSettings((BatchFormat)Format, CountChoices[CountIndex], Seed, TimeLimits[TimeIndex]));
}

/// <summary>
/// The form for a match to watch or a batch to run: table size, who sits where, the format, the number of hands (or
/// tournaments), the time a bot gets for a decision and the seed. Start hands the choices to the workshop's
/// <see cref="WorkshopContext.WatchMatch"/> or <see cref="WorkshopContext.RunBatch"/>.
/// </summary>
internal sealed class MatchSetupScene : Scene
{
    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly MatchSetup _setup;
    private readonly Form _form;

    public MatchSetupScene(WorkshopContext workshop, bool batch)
    {
        _workshop = workshop;
        _context = workshop.Game;
        _setup = new MatchSetup(workshop.Bots(), batch, workshop.Game.Deal is { } deal ? DealSeed.FromNumber(deal) : DealSeed.New());
        var setup = _setup;
        string[] formats = batch
            ? [T("Duplicate heads-up"), T("Duplicate ring"), T("Sit-and-go series")]
            : [T("Sit-and-go"), T("Fixed stacks")];
        string[] sizes = [.. Enumerable.Range(2, MatchSetup.MaxSeats - 1).Select(n => n.ToString(CultureInfo.CurrentCulture))];
        // The row's choices are swapped in place when the format changes between hands and tournaments.
        var counts = Counts(setup);
        var hands = new ChoiceRow(HandsLabel(), counts, () => setup.CountIndex, index => setup.CountIndex = index);
        _form = new Form(
            _context.Sounds,
            new ChoiceRow(T("Table size"), sizes, () => setup.Size - 2, index => setup.Resize(index + 2)),
            new ButtonRow(T("Bots at the table"), () => Scenes.Push(new SeatsScene(workshop, setup)), () => Summary(setup)),
            new ChoiceRow(
                T("Format"),
                formats,
                () => setup.Format,
                index =>
                {
                    setup.SetFormat(index);
                    hands.Label = HandsLabel();
                    counts.Clear();
                    counts.AddRange(Counts(setup));
                }),
            hands,
            new ChoiceRow(
                T("Time per decision"),
                [.. MatchSetup.TimeLimits.Select(t => T("{0} s", t.TotalSeconds.ToString("0.##", Tr.Culture)))],
                () => setup.TimeIndex,
                index => setup.TimeIndex = index),
            new ButtonRow(T("New seed"), () => setup.Seed = DealSeed.New(), () => setup.Seed.ToString()),
            new ButtonRow(T("Start"), Start),
            new ButtonRow(T("Back"), () => Scenes.Pop()));
        string HandsLabel() => setup.IsSeries ? T("Tournaments") : T("Hands");
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _form.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, _setup.IsBatch ? T("Run a batch") : T("Watch a match"), [], _form);

    private static List<string> Counts(MatchSetup setup) => [.. setup.CountChoices.Select(n => n.ToString("N0", Tr.Culture))];

    private static string Summary(MatchSetup setup)
    {
        var names = Enumerable.Range(0, setup.Size).Select(s => setup.Bot(s)?.Name ?? T("You")).ToList();
        return names.Count <= 2 ? string.Join(", ", names) : $"{names[0]}, {names[1]} +{names.Count - 2}";
    }

    private void Start() => Scenes.Push(_setup.IsBatch ? _workshop.RunBatch(_setup.Batch()) : _workshop.WatchMatch(_setup.Match()));
}

/// <summary>Who sits at each seat: a bot from the list, or (in a match to watch, once) the player.</summary>
internal sealed class SeatsScene : Scene
{
    private readonly GameContext _context;
    private readonly Form _form;

    public SeatsScene(WorkshopContext workshop, MatchSetup setup)
    {
        _context = workshop.Game;
        string[] choices = [.. setup.Bots.Select(b => b.Name), .. setup.IsBatch ? [] : new[] { T("You") }];
        List<FormRow> rows = [];
        for (var seat = 0; seat < setup.Size; seat++)
        {
            var at = seat;
            rows.Add(new ChoiceRow(
                T("Seat {0}", at + 1),
                choices,
                () => setup.Seats[at],
                index => setup.Choose(at, index, index == (setup.Seats[at] + 1) % choices.Length ? 1 : -1)));
        }

        rows.Add(new ButtonRow(T("Done"), () => Scenes.Pop()));
        _form = new Form(_context.Sounds, [.. rows]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _form.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Bots at the table"), [], _form);
}
