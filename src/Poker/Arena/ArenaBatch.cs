using System.Globalization;
using Jentl.Collect;
using Jentl.Multiplayer;

namespace Poker.Arena;

/// <summary>
/// Plays many hands between bots without showing them, to find out which bot is better. Every run gets fresh seats
/// from the bots' factories and disposes them when it ends.
/// </summary>
/// <remarks>
/// <para>
/// In a duplicate batch every run is a fixed-stacks match with the same seed and number of hands, so hand <c>i</c>
/// has the same cards in every run, and the runs seat the bots by <see cref="Seatings.Duplicate"/> so each bot plays
/// each deal from every seat. A bot's sample for deal <c>i</c> is the sum, over the runs, of what it won on that deal
/// in big blinds; the luck of the cards cancels out in that sum. Its result is the mean sample × 100 / runs big
/// blinds per 100 hands, with the 95% margin scaled the same way.
/// </para>
/// <para>
/// A series of sit-and-gos turns the seating one seat further each game, so no bot always sits in seat 0, and deals each
/// game from its own seed (see <see cref="GameSeed"/>). Its result is the bots' places; big blinds per 100 hands mean
/// nothing there and are reported as 0.
/// </para>
/// <para>
/// A cancelled batch stops between hands and returns what was played. In a duplicate batch only the deals played in
/// every run count towards the results; a deal some run did not reach is left out (its hands are still recorded).
/// </para>
/// <para>
/// With <see cref="BatchSettings.Parallel"/> up to one run per processor plays at the same time. Bots that run in
/// their own processes then compete for the processors, and a slow one may be counted late where it would have
/// answered in time on its own.
/// </para>
/// </remarks>
public sealed class ArenaBatch
{
    /// <summary>The most hands one sit-and-go of a series may take; the blinds go up long before that.</summary>
    public const int MaxSitAndGoHands = 1000;

    private readonly IReadOnlyList<Func<IArenaSeat>> _bots;
    private readonly BatchSettings _settings;
    private readonly TimeProvider _time;

    /// <param name="bots">A way to make a fresh seat for each bot; each bot sits in one seat per run.</param>
    /// <param name="settings">How the batch is played.</param>
    /// <param name="time">The clock the result is dated by.</param>
    /// <exception cref="ArgumentException">There are too few or too many bots for the format.</exception>
    public ArenaBatch(IReadOnlyList<Func<IArenaSeat>> bots, BatchSettings settings, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Hands);
        if (bots.Any(b => b is null))
        {
            throw new ArgumentException("Every bot needs a way to make its seat.", nameof(bots));
        }

        var (least, most) = settings.Format == BatchFormat.DuplicateHeadsUp ? (2, 2) : (2, 9);
        if (bots.Count < least || bots.Count > most)
        {
            throw new ArgumentException(
                least == most
                    ? $"{settings.Format} needs exactly {least} bots, not {bots.Count}."
                    : $"{settings.Format} needs {least} to {most} bots, not {bots.Count}.",
                nameof(bots));
        }

        _bots = bots;
        _settings = settings;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>What <paramref name="seat"/> won (or, when negative, lost) on <paramref name="hand"/>, in chips.</summary>
    public static int HandNet(HandRecord hand, int seat)
    {
        ArgumentNullException.ThrowIfNull(hand);
        var putIn = hand.Actions.Where(a => a.Seat == seat).Sum(a => a.Amount);
        if (seat == hand.SmallBlindSeat)
        {
            putIn += hand.SmallBlindPosted;
        }

        if (seat == hand.BigBlindSeat)
        {
            putIn += hand.BigBlindPosted;
        }

        return hand.Won.GetValueOrDefault(seat) - putIn;
    }

    /// <summary>
    /// Plays the batch and returns how each bot did, in the order the bots were given. Reports the part done (0 to
    /// 1) after every hand of a duplicate batch and after every game of a series that is played to the end, also when
    /// the runs are played side by side. When <paramref name="cancel"/> is
    /// cancelled, stops between hands and returns what was played.
    /// </summary>
    public async Task<MatchResult> RunAsync(IProgress<double> progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var series = _settings.Format == BatchFormat.SitAndGoSeries;
        var rotations = Seatings.Duplicate(_bots.Count);
        var runs = series ? _settings.Hands : rotations.Count;
        var table = series
            ? new ArenaSettings(TableFormat.SitAndGo, MaxSitAndGoHands, _settings.Seed, _settings.TimeLimit)
            : new ArenaSettings(TableFormat.FixedStacks, _settings.Hands, _settings.Seed, _settings.TimeLimit);
        var total = series ? runs : (double)_settings.Hands * runs;
        var done = 0;
        void Report() => progress.Report(Interlocked.Increment(ref done) / total);

        async Task<Played> Play(int run)
        {
            var game = await PlayAsync(
                rotations[run % rotations.Count],
                series ? table with { Seed = GameSeed(_settings.Seed, run) } : table,
                run,
                series ? null : Report,
                cancel);
            if (series && game.Finished)
            {
                Report();
            }

            return game;
        }

        // The first run is always played, if only for the bots' names; no other run starts once cancelled.
        var played = new Played?[runs];
        if (_settings.Parallel)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            await Parallel.ForEachAsync(Enumerable.Range(0, runs), options, async (run, _) =>
            {
                if (run == 0 || !cancel.IsCancellationRequested)
                {
                    played[run] = await Play(run);
                }
            });
        }
        else
        {
            for (var run = 0; run < runs && (run == 0 || !cancel.IsCancellationRequested); run++)
            {
                played[run] = await Play(run);
            }
        }

        return Result(series, rotations.Count, table, [.. played.OfType<Played>()]);
    }

    private MatchResult Result(bool series, int rotations, ArenaSettings table, List<Played> played)
    {
        var first = played[0];
        var names = Enumerable.Range(0, _bots.Count).Select(bot => first.Results[first.SeatOf(bot)].Name).ToList();
        var results = Enumerable.Range(0, _bots.Count)
            .Select(bot => series ? SeriesResult(names[bot], bot, played) : DuplicateResult(names[bot], bot, played, rotations, table))
            .ToList();
        return new MatchResult(table, _settings, _time.GetUtcNow(), names, results, [.. played.SelectMany(p => p.Hands)]);
    }

    private static BotResult DuplicateResult(string name, int bot, List<Played> played, int runs, ArenaSettings table)
    {
        // Only the deals every run reached count.
        var deals = played.Count == runs ? played.Min(p => p.Hands.Count) : 0;
        var bigBlind = table.SmallBlind * 2.0;
        var stats = new RunningStats();
        for (var deal = 0; deal < deals; deal++)
        {
            stats.Add(played.Sum(p => HandNet(p.Hands[deal], p.SeatOf(bot))) / bigBlind);
        }

        var faults = played.Select(p => p.Results[p.SeatOf(bot)]).ToList();
        return new BotResult(
            name,
            stats.Mean * 100 / runs,
            stats.Margin95 * 100 / runs,
            deals * runs,
            faults.Sum(r => r.Late),
            faults.Sum(r => r.Illegal),
            faults.Count(r => r.Gone),
            []);
    }

    private static BotResult SeriesResult(string name, int bot, List<Played> played)
    {
        var games = played.Select(p => (p.Finished, Result: p.Results[p.SeatOf(bot)])).ToList();
        return new BotResult(
            name,
            0,
            0,
            games.Sum(g => g.Result.Hands),
            games.Sum(g => g.Result.Late),
            games.Sum(g => g.Result.Illegal),
            games.Count(g => g.Result.Gone),
            [.. games.Where(g => g.Finished).Select(g => g.Result.Place!.Value)]);
    }

    /// <summary>The seed game <paramref name="game"/> (counting from 0) of a sit-and-go series is dealt from.</summary>
    public static DealSeed GameSeed(DealSeed seed, int game) =>
        seed.Derive(string.Create(CultureInfo.InvariantCulture, $"game {game}"));

    /// <summary>Plays run <paramref name="run"/> with the bots in <paramref name="seating"/> (bot by seat), stopping between hands when cancelled.</summary>
    private async Task<Played> PlayAsync(int[] seating, ArenaSettings settings, int run, Action? handPlayed, CancellationToken cancel)
    {
        var seats = new List<IArenaSeat>();
        try
        {
            foreach (var bot in seating)
            {
                seats.Add(_bots[bot]());
            }
        }
        catch
        {
            seats.ForEach(s => s.Dispose());
            throw;
        }

        using var table = new ArenaTable(seats, settings, run);
        await table.StartAsync();
        var hands = 0;
        while (!table.IsOver)
        {
            if (cancel.IsCancellationRequested && (table.Hand is null || table.Hand.IsOver))
            {
                break;
            }

            await table.StepAsync(CancellationToken.None);
            if (table.Hands.Count > hands)
            {
                hands = table.Hands.Count;
                handPlayed?.Invoke();
            }
        }

        return new Played(seating, [.. table.Hands], table.Results(), table.IsOver);
    }

    /// <summary>One run of the batch: who sat where, its hands, how every seat did, and whether it was played to the end.</summary>
    private sealed record Played(int[] Seating, IReadOnlyList<HandRecord> Hands, IReadOnlyList<SeatResult> Results, bool Finished)
    {
        public int SeatOf(int bot) => Array.IndexOf(Seating, bot);
    }
}
