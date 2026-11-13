using Jentl.Traffic;

namespace Roadworks;

/// <summary>How a plan did: whether every gate reaches every other, the traffic's delay and flow, the cost, and the stars.</summary>
public sealed record Verdict(string? Unreachable, float AverageDelay, int Arrived, int Expected, int Cost, int Stars)
{
    /// <summary>Within the budget, every gate connected, the delay on target and most of the traffic through.</summary>
    public bool Passed => Unreachable is null && Stars > 0;
}

/// <summary>
/// Runs a plan's traffic: the gates send their traffic per hour to the other gates, drivers going round jams, for the
/// level's minutes. Step it a frame at a time to watch (<see cref="Advance"/>), or <see cref="RunToEnd"/>.
/// </summary>
public sealed class Trial
{
    public Trial(Plan plan, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        (Network, Gates) = plan.Build();
        Simulation = new TrafficSimulation(Network, seed) { AvoidJams = true, LaneChanges = true, Destinations = [.. Gates.Values] };
        foreach (var (gate, node) in Gates)
        {
            Simulation.SetDemand(node, gate.PerHour);
        }

        Unreachable = FindUnreachable() ?? StartBus();
    }

    /// <summary>A bus stop's spot with no road through it (a junction there doesn't count), if there is one.</summary>
    public Spot? BusStopUnbuilt { get; private set; }

    /// <summary>Whether the roads past the stops give the bus no way from its gate to the other.</summary>
    public bool BusHasNoWay { get; private set; }

    /// <summary>The bus line's stops as laid out on the built roads, if it runs.</summary>
    public IReadOnlyList<BusStop> BusStops { get; private set; } = [];

    /// <summary>
    /// Starts the level's bus line, its stops on the lanes through their spots that make a way from gate to gate; what
    /// is wrong when there is none ("bus past (4, 2)"), or null.
    /// </summary>
    private string? StartBus()
    {
        if (Plan.Level.Bus is not { } bus)
        {
            return null;
        }

        var from = Gates.Single(g => g.Key.Name == bus.From).Value;
        var to = Gates.Single(g => g.Key.Name == bus.To).Value;

        // Every lane through each stop's spot, then each choice of them in turn till one makes a way.
        var choices = bus.Stops.Select(spot => Network.Lanes.Where(l => !l.IsJunction).Select(l => (Lane: l, At: Along(l, Plan.World(spot)))).Where(c => c.At is not null).Select(c => BusStop.On(c.Lane, c.At!.Value / c.Lane.Length)).ToList()).ToList();
        if (choices.FindIndex(c => c.Count == 0) is var missing and >= 0)
        {
            BusStopUnbuilt = bus.Stops[missing];
            return $"bus past ({bus.Stops[missing].X}, {bus.Stops[missing].Y})";
        }

        foreach (var stops in Combinations(choices))
        {
            try
            {
                var line = new BusLine("bus", from, to, stops, bus.Every);
                Simulation.AddLine(line);
                BusStops = stops;
                return null;
            }
            catch (ArgumentException)
            {
                // No way through these lanes: try the next.
            }
        }

        BusHasNoWay = true;
        return $"bus {bus.From} → {bus.To}";
    }

    /// <summary>How far along a lane a point lies, when the lane passes within a few metres of it.</summary>
    private static float? Along(Lane lane, System.Numerics.Vector2 point)
    {
        var points = lane.Path.Points;
        var travelled = 0f;
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var length = System.Numerics.Vector2.Distance(a, b);
            var t = length == 0 ? 0 : Math.Clamp(System.Numerics.Vector2.Dot(point - a, b - a) / (length * length), 0, 1);
            if (System.Numerics.Vector2.Distance(a + ((b - a) * t), point) < 6 && travelled + (t * length) is var at && at > 5 && at < lane.Length - 5)
            {
                return at;
            }

            travelled += length;
        }

        return null;
    }

    private static IEnumerable<List<BusStop>> Combinations(List<List<BusStop>> choices, int index = 0)
    {
        if (index == choices.Count)
        {
            yield return [];
            yield break;
        }

        foreach (var choice in choices[index])
        {
            foreach (var rest in Combinations(choices, index + 1))
            {
                yield return [choice, .. rest];
            }
        }
    }

    public Plan Plan { get; }

    public RoadNetwork Network { get; }

    public IReadOnlyDictionary<Gate, Node> Gates { get; }

    public TrafficSimulation Simulation { get; }

    /// <summary>"A to B" for the first pair of gates with no way between them, or null when all connect.</summary>
    public string? Unreachable { get; }

    public float Length => Plan.Level.Minutes * 60;

    public bool IsDone => Simulation.Time >= Length;

    /// <summary>Moves the traffic on by <paramref name="seconds"/> of town time, up to the end.</summary>
    public void Advance(float seconds) => Simulation.Step(MathF.Min(seconds, MathF.Max(0, Length - Simulation.Time)));

    public Verdict RunToEnd()
    {
        while (!IsDone)
        {
            Advance(10);
        }

        return Verdict();
    }

    /// <summary>
    /// The verdict so far. Delay counts finished trips, and the vehicles still on the road for as long as they have
    /// been late, so a gridlock can't hide; three stars at half the target delay or less, two at three quarters, one
    /// on target, none over it, over budget, or with less than four in five of the expected trips done.
    /// </summary>
    public Verdict Verdict()
    {
        var level = Plan.Level;
        var stats = Simulation.Stats();
        var hours = Simulation.Time / 3600;
        var expected = (int)(level.Gates.Sum(g => g.PerHour) * hours);

        // Trips still under way, and traffic still waiting to get in, count as late as they are so far.
        var late = Simulation.Vehicles.Sum(v => MathF.Max(0, Simulation.Time - v.StartTime - v.FreeTime));
        var waiting = Simulation.Stats().Queued;
        var trips = stats.Completed + Simulation.Vehicles.Count + waiting;
        var delay = trips == 0 ? 0 : ((stats.AverageDelay * stats.Completed) + late + (waiting * 60)) / trips;
        var throughput = expected == 0 ? 1 : stats.Completed / (float)Math.Max(1, expected - Simulation.Vehicles.Count);
        var stars = Unreachable is not null || Plan.OverBudget || throughput < 0.8f ? 0
            : delay <= level.TargetDelay * 0.5f ? 3
            : delay <= level.TargetDelay * 0.75f ? 2
            : delay <= level.TargetDelay ? 1
            : 0;
        return new Verdict(Unreachable, delay, stats.Completed, expected, Plan.Cost, stars);
    }

    private string? FindUnreachable()
    {
        foreach (var (from, a) in Gates)
        {
            foreach (var (to, b) in Gates)
            {
                if (from != to && Simulation.Route(a, b) is null)
                {
                    return $"{from.Name} → {to.Name}";
                }
            }
        }

        return null;
    }
}
