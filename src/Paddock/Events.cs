using System.Globalization;
using Jentl.Collect;

namespace Paddock;

/// <summary>Which cars may enter an event.</summary>
/// <param name="HandRqLimit">The hand's RQ limit for this event.</param>
/// <param name="MaxCarRq">The highest RQ one car may have.</param>
/// <param name="Classes">Allowed rarity classes.</param>
/// <param name="YearFrom">Earliest model year.</param>
/// <param name="YearTo">Latest model year.</param>
/// <param name="Tyres">Allowed tyres.</param>
/// <param name="Bodies">Allowed kinds of car.</param>
/// <param name="Countries">Allowed makers' countries.</param>
public sealed record EntryRules(
    int HandRqLimit, int? MaxCarRq = null, IReadOnlyList<Rarity>? Classes = null, int? YearFrom = null, int? YearTo = null,
    IReadOnlyList<Tyres>? Tyres = null, IReadOnlyList<Body>? Bodies = null, IReadOnlyList<string>? Countries = null)
{
    /// <summary>Whether a car may enter.</summary>
    public bool Allows(CarModel model, int rq)
    {
        ArgumentNullException.ThrowIfNull(model);
        return (MaxCarRq is not { } max || rq <= max)
            && (Classes is null || Classes.Contains(Rating.ClassOf(rq)))
            && (YearFrom is not { } from || model.Year >= from)
            && (YearTo is not { } to || model.Year <= to)
            && (Tyres is null || Tyres.Contains(model.Stats.Tyres))
            && (Bodies is null || Bodies.Contains(model.Body))
            && (Countries is null || Countries.Contains(model.Country));
    }

    /// <summary>The rules as lines for the screen, each made from an English template passed through <paramref name="translate"/>; values (tyres, bodies, classes, countries) are named by <paramref name="name"/>.</summary>
    public IReadOnlyList<string> Describe(Func<string, string> translate, Func<object, string>? name = null)
    {
        ArgumentNullException.ThrowIfNull(translate);
        name ??= value => value.ToString() ?? string.Empty;
        string Names<T>(IEnumerable<T> values) => string.Join(", ", values.Select(v => name(v!)));
        string Line(string template, params object[] values) => string.Format(CultureInfo.InvariantCulture, translate(template), values);
        var lines = new List<string> { Line("Hand RQ up to {0}", HandRqLimit) };
        if (MaxCarRq is { } max)
        {
            lines.Add(Line("Cars up to RQ {0}", max));
        }

        if (Classes is not null)
        {
            lines.Add(Line("Classes: {0}", Names(Classes)));
        }

        lines.Add((YearFrom, YearTo) switch
        {
            ({ } from, { } to) => Line("Cars from {0} to {1}", from, to),
            ({ } from, null) => Line("Cars from {0} on", from),
            (null, { } to) => Line("Cars up to {0}", to),
            _ => string.Empty,
        });
        if (Tyres is not null)
        {
            lines.Add(Line("Tyres: {0}", Names(Tyres)));
        }

        if (Bodies is not null)
        {
            lines.Add(Line("Bodies: {0}", Names(Bodies)));
        }

        if (Countries is not null)
        {
            lines.Add(Line("From: {0}", Names(Countries)));
        }

        return [.. lines.Where(l => l.Length > 0)];
    }
}

/// <summary>An event template: its rules, tracks and prize car.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Shown name (English; translated through the language file).</param>
/// <param name="Rules">Which cars may enter.</param>
/// <param name="PrizeCarId">The car won at the top of the ladder.</param>
/// <param name="TrackIds">The tracks its rounds are raced on.</param>
public sealed record EventDef(string Id, string Name, EntryRules Rules, string PrizeCarId, IReadOnlyList<string> TrackIds)
{
    /// <summary>Reads <c>events.json</c>.</summary>
    public static IReadOnlyList<EventDef> ListFromJson(string json) =>
        (System.Text.Json.JsonSerializer.Deserialize(json, PaddockJson.Default.EventsFile) ?? throw new FormatException("No events.")).Events;
}

/// <summary>The contents of <c>events.json</c>.</summary>
internal sealed record EventsFile(IReadOnlyList<EventDef> Events);

/// <summary>An event running now.</summary>
/// <param name="Def">Its template.</param>
/// <param name="InstanceId">This run of it (template and period): progress is kept per run.</param>
/// <param name="Ends">When it closes.</param>
public sealed record LiveEvent(EventDef Def, string InstanceId, DateTimeOffset Ends);

/// <summary>
/// Which events run when: three slots, each changing every three days, a day apart, so one event changes every day.
/// Worked out from the date (UTC), so every device shows the same events without a server.
/// </summary>
public sealed class EventCalendar(IReadOnlyList<EventDef> templates)
{
    /// <summary>The number of events running at once.</summary>
    public const int Slots = 3;

    private static readonly DateTimeOffset Epoch = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The events running at <paramref name="now"/>.</summary>
    public IReadOnlyList<LiveEvent> Live(DateTimeOffset now) =>
    [
        .. Enumerable.Range(0, Slots).Select(slot =>
        {
            var rotation = new Rotation(Epoch.AddDays(slot), TimeSpan.FromDays(3));
            var period = rotation.PeriodAt(now);
            var def = rotation.Pick(templates, now, offset: slot * (templates.Count / Slots));
            return new LiveEvent(def, $"{def.Id}@{slot}.{period}", rotation.EndOf(period));
        }),
    ];
}

/// <summary>A player's progress in one run of an event.</summary>
/// <param name="Rung">Matches won so far (0 to <see cref="EventPlay.Rungs"/>).</param>
/// <param name="Tickets">The event's tickets.</param>
/// <param name="PrizeTaken">Whether the prize car was given.</param>
public sealed record EventProgress(int Rung, RechargeState Tickets, bool PrizeTaken);

/// <summary>What a raced event match brought.</summary>
/// <param name="Result">The match.</param>
/// <param name="NewCars">Cars won (a pack's, or the prize car).</param>
/// <param name="Cash">Cash paid.</param>
/// <param name="Xp">Experience gained.</param>
public sealed record EventOutcome(MatchResult Result, IReadOnlyList<CarInstance> NewCars, long Cash, int Xp);

/// <summary>Playing events: the ladder, tickets and rewards.</summary>
public static class EventPlay
{
    /// <summary>Matches on an event's ladder.</summary>
    public const int Rungs = 12;

    /// <summary>Every event's tickets: 5, one back every 30 minutes.</summary>
    public static readonly RechargingCounter Tickets = new(5, TimeSpan.FromMinutes(30));

    /// <summary>The player's progress in <paramref name="ev"/> as of <paramref name="now"/> (tickets brought up to date).</summary>
    public static EventProgress Progress(Profile profile, LiveEvent ev, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ev);
        if (!profile.Events.TryGetValue(ev.InstanceId, out var progress))
        {
            return new EventProgress(0, Tickets.Full(now), false);
        }

        return progress with { Tickets = Tickets.Current(progress.Tickets, now) };
    }

    /// <summary>Forgets progress in events that are no longer running, so the save doesn't grow by one a day.</summary>
    public static void Prune(Profile profile, IReadOnlyList<LiveEvent> live)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(live);
        var running = live.Select(e => e.InstanceId).ToHashSet();
        foreach (var gone in profile.Events.Keys.Where(k => !running.Contains(k)).ToList())
        {
            profile.Events.Remove(gone);
        }
    }

    /// <summary>The cars of the garage that may enter.</summary>
    public static IEnumerable<CarInstance> Eligible(Profile profile, LiveEvent ev, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(roster);
        return profile.Garage.Where(c => roster.Find(c.ModelId) is { } m && ev.Def.Rules.Allows(m, roster.Rq(c.ModelId)));
    }

    /// <summary>Whether the player can race the next match now; if not, <paramref name="why"/> says why (English, for translation).</summary>
    public static bool CanEnter(Profile profile, LiveEvent ev, Roster roster, DateTimeOffset now, out string? why)
    {
        var progress = Progress(profile, ev, now);
        var cheapest = Eligible(profile, ev, roster).Select(c => roster.Rq(c.ModelId)).Order().Take(Hands.Size).ToList();
        why = now >= ev.Ends ? "Event ended"
            : progress.Rung >= Rungs ? "Event finished"
            : cheapest.Count < Hands.Size || cheapest.Sum() > ev.Def.Rules.HandRqLimit ? "Not enough cars that may enter"
            : progress.Tickets.Count <= 0 ? "No tickets left"
            : null;
        return why is null;
    }

    /// <summary>The match at <paramref name="rung"/>: five rounds on the event's tracks and an opponent that fits the rules, stronger up the ladder. The same every time.</summary>
    public static MatchSetup Next(LiveEvent ev, int rung, Roster roster, IReadOnlyList<TrackDef> tracks)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(tracks);
        var random = new Random(StableSeed.Of($"{ev.InstanceId}#{rung}"));
        var rules = ev.Def.Rules;
        var strength = 0.6 + (0.4 * Math.Min(rung, Rungs - 1) / (Rungs - 1));
        var opponent = Opponents.Pick(roster, m => rules.Allows(m, roster.Rq(m.Id)), rules.HandRqLimit, strength, upgrades: Math.Min(rung, Upgrades.PointsPerStage), random);
        return new MatchSetup(Opponents.Rounds(ev.Def.TrackIds, tracks, random), opponent);
    }

    /// <summary>
    /// Races the next match with the player's cars in <paramref name="order"/>: spends a ticket, pays cash and
    /// experience, and on a win climbs the ladder (a Starter pack at 4, a Classic pack at 8, the prize car at the top).
    /// </summary>
    /// <exception cref="InvalidOperationException">The player can't enter, or the hand breaks the rules.</exception>
    public static EventOutcome Race(Profile profile, LiveEvent ev, IReadOnlyList<Guid> order, Roster roster, IReadOnlyList<TrackDef> tracks, DateTimeOffset now, Random packRandom)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(roster);
        if (!CanEnter(profile, ev, roster, now, out var why))
        {
            throw new InvalidOperationException(why);
        }

        var hand = order.Select(profile.Car).ToList();
        if (hand.Any(c => c is null) || !Hands.Fits(hand!, roster, ev.Def.Rules.HandRqLimit, m => ev.Def.Rules.Allows(m, roster.Rq(m.Id))))
        {
            throw new InvalidOperationException("This hand may not enter.");
        }

        var progress = Progress(profile, ev, now);
        var tickets = progress.Tickets;
        Tickets.TrySpend(ref tickets, now);
        var setup = Next(ev, progress.Rung, roster, tracks);
        var result = Matches.Play(setup, hand!, roster);
        var (cash, xp) = Progression.Pay(profile, result.PlayerWon, setup.Opponent.Average(c => roster.Rq(c.ModelId)));
        var newCars = new List<CarInstance>();
        var rung = progress.Rung;
        var prizeTaken = progress.PrizeTaken;
        if (result.PlayerWon)
        {
            rung++;
            if (rung == 4)
            {
                newCars.AddRange(Packs.Open(Packs.Starter, roster, packRandom));
            }
            else if (rung == 8)
            {
                newCars.AddRange(Packs.Open(Packs.Classic, roster, packRandom));
            }
            else if (rung == Rungs && !prizeTaken)
            {
                newCars.Add(CarInstance.New(ev.Def.PrizeCarId));
                prizeTaken = true;
            }
        }

        profile.Garage.AddRange(newCars);
        profile.Events[ev.InstanceId] = new EventProgress(rung, tickets, prizeTaken);
        profile.LastHand = [.. order];
        return new EventOutcome(result, newCars, cash, xp);
    }
}

/// <summary>Opponents for events and the campaign.</summary>
public static class Opponents
{
    /// <summary>
    /// Five allowed cars whose RQ adds up to about <paramref name="strength"/> of <paramref name="rqLimit"/> (never
    /// over it), each with <paramref name="upgrades"/> points spread over its areas. Ids come from
    /// <paramref name="random"/>, so the same seed gives the same hand.
    /// </summary>
    public static IReadOnlyList<CarInstance> Pick(Roster roster, Func<CarModel, bool> allowed, int rqLimit, double strength, int upgrades, Random random)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(random);
        var pool = roster.All.Where(allowed).OrderBy(m => roster.Rq(m.Id)).ToList();
        if (pool.Count == 0)
        {
            throw new InvalidOperationException("No car may enter.");
        }

        var cheapest = roster.Rq(pool[0].Id);
        if (cheapest * Hands.Size > rqLimit)
        {
            throw new InvalidOperationException($"Five cars can't fit an RQ limit of {rqLimit}.");
        }
        var left = rqLimit;
        var target = rqLimit * strength;
        var hand = new List<CarInstance>();
        var tune = new Tune((upgrades + 2) / 3, (upgrades + 1) / 3, upgrades / 3);
#pragma warning disable CA5394 // Game randomness, not security relevant.
        for (var slot = 0; slot < Hands.Size; slot++)
        {
            var slotsAfter = Hands.Size - slot - 1;
            var most = left - (cheapest * slotsAfter);
            var want = target / (Hands.Size - slot);
            var fits = pool.Where(m => roster.Rq(m.Id) <= most).ToList();
            var near = fits.Where(m => Math.Abs(roster.Rq(m.Id) - want) <= 8).ToList();
            var model = near.Count > 0 ? near[random.Next(near.Count)] : fits.MinBy(m => Math.Abs(roster.Rq(m.Id) - want)) ?? pool[0];
            var id = new byte[16];
            random.NextBytes(id);
            hand.Add(new CarInstance(new Guid(id), model.Id, 1, tune));
            left -= roster.Rq(model.Id);
            target -= roster.Rq(model.Id);
        }

        return hand;
    }

    /// <summary>Five rounds on the given tracks (different ones while there are enough), each in one of its conditions.</summary>
    public static IReadOnlyList<(TrackDef Track, Condition Condition)> Rounds(IReadOnlyList<string> trackIds, IReadOnlyList<TrackDef> tracks, Random random)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(random);
        var pool = trackIds.Select(id => tracks.First(t => t.Id == id)).ToList();
        var shuffled = pool.OrderBy(_ => random.Next()).ToList();
        return
        [
            .. Enumerable.Range(0, Hands.Size).Select(i =>
            {
                var track = shuffled[i % shuffled.Count];
                return (track, track.Conditions[random.Next(track.Conditions.Count)]);
            }),
        ];
#pragma warning restore CA5394
    }
}
