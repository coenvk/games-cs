using System.Globalization;
using Jentl.Cards;
using Jentl.Multiplayer;
using PokerBots;

namespace Poker.Arena;

/// <summary>How a match at one table is played.</summary>
public enum TableFormat
{
    /// <summary>Everyone starts with the same chips and the blinds go up until one player has them all.</summary>
    SitAndGo,

    /// <summary>Every hand starts from the same stacks with the same blinds, so each hand counts on its own.</summary>
    FixedStacks,
}

/// <summary>The rules of a match at one table.</summary>
/// <param name="Hands">For a sit-and-go the most hands to play; for fixed stacks the number of hands.</param>
/// <param name="Seed">The match seed: each hand's seed is derived from it (see <see cref="DealSeed.Hand"/>), and so is
/// each seat's own seed (see <see cref="ArenaTable.SeatSeed"/>).</param>
/// <param name="TimeLimit">How long a bot gets for each decision.</param>
/// <param name="StartingStack">The chips everyone starts a sit-and-go with.</param>
/// <param name="SmallBlind">The small blind (at the start, for a sit-and-go).</param>
/// <param name="StackBigBlinds">For fixed stacks: the big blinds every seat holds at the start of every hand.</param>
public sealed record ArenaSettings(
    TableFormat Format,
    int Hands,
    DealSeed Seed,
    TimeSpan TimeLimit,
    int StartingStack = 1000,
    int SmallBlind = 10,
    int StackBigBlinds = 100);

/// <summary>How one seat did in a match.</summary>
/// <param name="Net">The chips it won (or, when negative, lost).</param>
/// <param name="Hands">The hands it was dealt into.</param>
/// <param name="Late">Its decisions that were not answered in time.</param>
/// <param name="Illegal">Its decisions answered with nothing or with a move that was not allowed.</param>
/// <param name="Gone">Whether the bot stopped playing (it crashed, exited or never started).</param>
/// <param name="Problem">What is wrong with the bot, or null.</param>
/// <param name="Place">Its finishing place in a sit-and-go, 1 for the winner; null for fixed stacks.</param>
public sealed record SeatResult(int Seat, string Name, int Net, int Hands, int Late, int Illegal, bool Gone, string? Problem, int? Place);

/// <summary>
/// A match of No-Limit Hold'em between bots at one table. Each bot is asked for its moves under the time limit (a
/// seat without one, a person, is waited for); a late or illegal answer is replaced by a check when that is free and a
/// fold otherwise, and counted; a bot that is gone is never asked again. Every hand is recorded. The table owns its
/// seats and disposes them.
/// </summary>
/// <remarks>
/// Drive it with <see cref="StartAsync"/> and then <see cref="StepAsync"/> until <see cref="IsOver"/>, or with
/// <see cref="RunAsync"/>. Steps must not overlap: await each before starting the next.
/// </remarks>
public sealed class ArenaTable : IDisposable
{
    /// <summary>How long a bot gets to get ready for the match.</summary>
    public static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IArenaSeat> _seats;
    private readonly ArenaSettings _settings;
    private readonly string[] _names;
    private readonly HandSeries? _series;
    private readonly HoldemTable? _table;
    private readonly List<HandRecord> _records = [];
    private readonly int[] _net;
    private readonly int[] _handsPlayed;
    private readonly int[] _late;
    private readonly int[] _illegal;
    private readonly bool[] _gone;
    private readonly int?[] _places;
    private readonly int _run;

    private int _stepping;
    private bool _started;
    private int _decisionId;
    private int _dealt;

    // The hand being played.
    private bool _playing;
    private int _handNumber;
    private int _level;
    private DealSeed _handSeed;
    private int[] _startStacks = [];
    private int[] _recipients = [];
    private Street _street;
    private readonly List<ActionRecord> _actions = [];
    private readonly List<(int Seat, Fault Fault)> _faults = [];
    private readonly HashSet<int> _shown = [];
    private int _smallPosted;
    private int _bigPosted;

    /// <param name="seats">The seats, in order; the table disposes them.</param>
    /// <param name="settings">The rules of the match.</param>
    /// <param name="run">Which run of a batch this match is (0 for a match on its own): the seats' own seeds differ per run.</param>
    public ArenaTable(IReadOnlyList<IArenaSeat> seats, ArenaSettings settings, int run = 0)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(seats.Count, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Hands);
        _seats = seats;
        _settings = settings;
        _run = run;
        _names = [.. seats.Select(s => s.Name)];
        if (settings.Format == TableFormat.FixedStacks)
        {
            _series = new HandSeries(seats.Count, settings.Seed, settings.StackBigBlinds, settings.SmallBlind);
        }
        else
        {
            _table = new HoldemTable(seats.Count, settings.Seed, settings.StartingStack, settings.SmallBlind);
        }

        _net = new int[seats.Count];
        _handsPlayed = new int[seats.Count];
        _late = new int[seats.Count];
        _illegal = new int[seats.Count];
        _gone = new bool[seats.Count];
        _places = new int?[seats.Count];
    }

    /// <summary>Whether the match is over (and every bot has been told the standings).</summary>
    public bool IsOver { get; private set; }

    /// <summary>The hand being played, or the last one played; null before the first.</summary>
    public HoldemHand? Hand { get; private set; }

    /// <summary>Every hand played to the end so far.</summary>
    public IReadOnlyList<HandRecord> Hands => _records;

    /// <summary>The rules the match is played by.</summary>
    public ArenaSettings Settings => _settings;

    /// <summary>The faults so far in <see cref="Hand"/>, the hand being played or the last one played, in order.</summary>
    public IReadOnlyList<(int Seat, Fault Fault)> HandFaults => _faults;

    /// <summary>
    /// The seed a seat gets for its own randomness in run <paramref name="run"/> of a match dealt from
    /// <paramref name="seed"/>: 64 bits of the match seed's numbers for that run and seat (<see cref="DealSeed.Numbers"/>).
    /// Those are one-way, so the seat seed tells a bot nothing about the match seed, the deck or another seat's seed.
    /// </summary>
    public static long SeatSeed(DealSeed seed, int run, int seat) =>
        unchecked((long)seed.Numbers(string.Create(CultureInfo.InvariantCulture, $"seat {run} {seat}")).NextUInt64());

    /// <summary>Tells every bot the match is starting, with its seat and its own seed, and waits until all are ready (or gone).</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            throw new InvalidOperationException("The match has already started.");
        }

        _started = true;
        var stack = _series is not null ? _settings.StackBigBlinds * _series.BigBlind : _settings.StartingStack;
        var format = _series is not null ? "fixed-stacks" : "sit-and-go";
        await Task.WhenAll(_seats.Select((seat, i) => seat.StartAsync(
            new MatchStart(
                i,
                _seats.Count,
                format,
                stack,
                _settings.SmallBlind,
                _settings.SmallBlind * 2,
                (int)_settings.TimeLimit.TotalMilliseconds,
                SeatSeed(_settings.Seed, _run, i)),
            StartLimit)));
    }

    /// <summary>
    /// Does the next thing: deals the next hand when none is being played (returning the blinds), or asks the
    /// player to act and plays its move, or ends the match (returning nothing; <see cref="IsOver"/> is then true).
    /// Returns the engine's events. A decision once asked is finished before <paramref name="cancel"/> is looked at
    /// again, which takes at most the time limit. Never call it again before the previous call has finished.
    /// </summary>
    public async Task<IReadOnlyList<PokerEvent>> StepAsync(CancellationToken cancel)
    {
        if (Interlocked.Exchange(ref _stepping, 1) == 1)
        {
            throw new InvalidOperationException("A step is already running: await it before taking the next.");
        }

        try
        {
            if (!_started)
            {
                throw new InvalidOperationException("Start the match first.");
            }

            cancel.ThrowIfCancellationRequested();
            if (IsOver)
            {
                return [];
            }

            if (_playing)
            {
                return await DecideAsync();
            }

            if (IsMatchDone())
            {
                Finish();
                return [];
            }

            return Deal();
        }
        finally
        {
            Volatile.Write(ref _stepping, 0);
        }
    }

    /// <summary>Starts the match if that has not been done yet and plays it to the end.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        if (!_started)
        {
            await StartAsync();
        }

        while (!IsOver)
        {
            await StepAsync(cancel);
        }
    }

    /// <summary>How every seat did so far.</summary>
    public IReadOnlyList<SeatResult> Results() =>
        [.. _seats.Select((seat, i) => new SeatResult(
            i,
            _names[i],
            Net(i),
            _handsPlayed[i],
            _late[i],
            _illegal[i],
            _gone[i] || seat.IsGone,
            seat.Problem,
            _table is not null ? _places[i] : null))];

    /// <summary>Disposes every seat.</summary>
    public void Dispose()
    {
        foreach (var seat in _seats)
        {
            seat.Dispose();
        }
    }

    private int Net(int seat) => _table is not null ? _table.Stacks[seat] - _settings.StartingStack : _net[seat];

    private bool IsMatchDone() =>
        _table is not null ? _table.Champion is not null || _table.HandNumber >= _settings.Hands : _dealt >= _settings.Hands;

    private List<PokerEvent> Deal()
    {
        HoldemHand hand;
        if (_series is not null)
        {
            _handSeed = _series.SeedOf(_dealt);
            hand = _series.Deal(_dealt);
            _dealt++;
            _handNumber = _dealt;
            _level = 0;
        }
        else
        {
            hand = _table!.DealHand();
            _handSeed = _table.HandSeed;
            _handNumber = _table.HandNumber;
            _level = _table.Level;
        }

        Hand = hand;
        _playing = true;
        _street = Street.Preflop;
        _actions.Clear();
        _faults.Clear();
        _shown.Clear();
        _startStacks = [.. Enumerable.Range(0, hand.Seats).Select(s => hand.Stack(s) + hand.Committed(s))];
        _recipients = [.. Enumerable.Range(0, hand.Seats).Where(hand.InHand)];
        var start = BotView.HandStart(hand, _handNumber, _names);
        foreach (var seat in _recipients)
        {
            _handsPlayed[seat]++;
            _seats[seat].Notify(start);
        }

        return Take(hand);
    }

    private async Task<List<PokerEvent>> DecideAsync()
    {
        var hand = Hand!;
        var seat = hand.CurrentSeat;
        var bot = _seats[seat];
        var id = ++_decisionId;
        var decision = BotView.Decision(hand, seat, id, _handNumber, _level, _names, _actions, _settings.TimeLimit);

        // The answer is turned into a move only once it is known to be in time: a late answer may arrive while the
        // hand is already being played on, and must not look at it. The fallback passed in is only a placeholder.
        PokerAction? move = null;
        var decided = await Decisions.DecideAsync(
            token => bot.AskAsync(decision, token),
            bot.HasTimeLimit ? _settings.TimeLimit : Timeout.InfiniteTimeSpan,
            answer => (move = BotView.ToAction(answer, hand, seat, id)) is not null,
            () => BotAction.Fold,
            () => bot.IsGone);

        switch (decided.Fault)
        {
            case Fault.None:
                break;
            case Fault.Late:
                _late[seat]++;
                break;
            case Fault.Illegal:
                _illegal[seat]++;
                break;
            case Fault.Gone:
                _gone[seat] = true;
                break;
        }

        if (decided.Fault != Fault.None)
        {
            _faults.Add((seat, decided.Fault));
            move = BotView.Fallback(hand, seat);
        }

        hand.Apply(seat, move!);
        return Take(hand);
    }

    /// <summary>The hand's new events, noted for the record; the hand is finished when they end it.</summary>
    private List<PokerEvent> Take(HoldemHand hand)
    {
        var events = hand.TakeEvents();
        foreach (var e in events)
        {
            switch (e)
            {
                case StreetDealt dealt:
                    _street = dealt.Street;
                    break;
                case Acted acted:
                    _actions.Add(new ActionRecord(_street, acted.Seat, acted.Action, acted.Amount, acted.Total));
                    break;
                case BlindPosted blind when blind.Big:
                    _bigPosted = blind.Amount;
                    break;
                case BlindPosted blind:
                    _smallPosted = blind.Amount;
                    break;
                case HandShown shown:
                    _shown.Add(shown.Seat);
                    break;
            }
        }

        if (hand.IsOver)
        {
            EndHand(hand);
        }

        return [.. events];
    }

    private void EndHand(HoldemHand hand)
    {
        _records.Add(new HandRecord(
            _handNumber,
            _handSeed,
            hand.Button,
            hand.SmallBlind,
            hand.BigBlind,
            hand.SmallBlindSeat,
            hand.BigBlindSeat,
            _smallPosted,
            _bigPosted,
            _names,
            _startStacks,
            [.. Enumerable.Range(0, hand.Seats).Select(s => (IReadOnlyList<Card>)[.. hand.Hole(s)])],
            [.. _actions],
            [.. hand.Board],
            hand.Pots,
            new Dictionary<int, int>(hand.Winnings),
            new HashSet<int>(_shown),
            [.. _faults]));

        var end = BotView.HandEnd(hand, _handNumber, _actions);
        foreach (var seat in _recipients)
        {
            _seats[seat].Notify(end);
        }

        if (_table is not null)
        {
            _table.Settle();
            PlaceBusted(hand);
        }
        else
        {
            for (var seat = 0; seat < hand.Seats; seat++)
            {
                _net[seat] += hand.Stack(seat) - _startStacks[seat];
            }
        }

        _playing = false;
    }

    /// <summary>
    /// Places the seats that ran out of chips in this hand below everyone still playing; among them, whoever
    /// started the hand with more chips places higher, then the lower seat.
    /// </summary>
    private void PlaceBusted(HoldemHand hand)
    {
        var remaining = Enumerable.Range(0, hand.Seats).Count(s => hand.Stack(s) > 0);
        var busted = Enumerable.Range(0, hand.Seats)
            .Where(s => _startStacks[s] > 0 && hand.Stack(s) == 0)
            .OrderByDescending(s => _startStacks[s])
            .ThenBy(s => s)
            .ToArray();
        for (var i = 0; i < busted.Length; i++)
        {
            _places[busted[i]] = remaining + 1 + i;
        }
    }

    private void Finish()
    {
        IsOver = true;
        int[] stacks;
        if (_table is not null)
        {
            // Whoever still has chips is placed by stack: the champion alone, or everyone left when the hands ran out.
            stacks = [.. _table.Stacks];
            var left = Enumerable.Range(0, stacks.Length).Where(s => stacks[s] > 0).OrderByDescending(s => stacks[s]).ThenBy(s => s).ToArray();
            for (var i = 0; i < left.Length; i++)
            {
                _places[left[i]] = i + 1;
            }
        }
        else
        {
            // Every hand starts from the same stack, so a seat finishes with nothing of its own: its standing is its
            // net, the chips it won (or, when negative, lost) over the match.
            stacks = [.. _net];
            var order = Enumerable.Range(0, stacks.Length).OrderByDescending(s => stacks[s]).ThenBy(s => s).ToArray();
            for (var i = 0; i < order.Length; i++)
            {
                _places[order[i]] = i + 1;
            }
        }

        var standings = new MatchEnd([.. Enumerable.Range(0, _seats.Count).Select(s => new Standing(s, _names[s], stacks[s], _places[s]!.Value))]);
        foreach (var seat in _seats)
        {
            seat.Notify(standings);
        }
    }
}
