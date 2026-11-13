using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Traffic;

namespace Roadworks;

/// <summary>A crossing of the grid's lines, where roads can meet.</summary>
public readonly record struct Spot(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A stretch of road between two neighbouring spots, the lower one first.</summary>
public readonly record struct Stretch
{
    public Stretch(Spot a, Spot b) => (A, B) = a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);

    public Spot A { get; }

    public Spot B { get; }

    public bool Touches(Spot spot) => A == spot || B == spot;

    public Spot Other(Spot spot) => A == spot ? B : A;
}

/// <summary>How a junction is run: give way to the right, traffic lights (following the traffic), or a roundabout.</summary>
public enum Junction
{
    Plain,
    Lights,
    Roundabout,
}

/// <summary>A way into the town at the edge of the grid: traffic comes in here and leaves by the other gates.</summary>
public sealed record Gate(string Name, Spot At, float PerHour);

/// <summary>A bus line a level asks for: from one gate to another, a bus every so many seconds, stopping at its stops' spots.</summary>
public sealed record BusRoute(string From, string To, float Every, IReadOnlyList<Spot> Stops);

/// <summary>
/// A level: the grid (spots from 0 to <see cref="Width"/> across and <see cref="Height"/> down), the gates, the spots
/// where nothing can be built (houses, a river), the budget, and the goal: average delay at most
/// <see cref="TargetDelay"/> seconds over <see cref="Minutes"/> minutes of traffic.
/// </summary>
public sealed record TownLevel(string Name, int Width, int Height, IReadOnlyList<Gate> Gates, IReadOnlyList<Spot> Blocked, int Budget, float TargetDelay, int Minutes = 10)
{
    /// <summary>The bus line the town wants, if any: the roads must take it past its stops.</summary>
    public BusRoute? Bus { get; init; }

    public bool Inside(Spot spot) => spot.X >= 0 && spot.X <= Width && spot.Y >= 0 && spot.Y <= Height;

    public bool IsBlocked(Spot spot) => Blocked.Contains(spot);

    /// <summary>Reads the levels file: <c>{ "levels": [ { "name", "width", "height", "budget", "delay", "gates": [{ "name", "x", "y", "perHour" }], "blocked": [[x, y]] } ] }</c>.</summary>
    /// <exception cref="InvalidDataException">The file can't be read as levels.</exception>
    public static IReadOnlyList<TownLevel> FromJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return [.. root!["levels"]!.AsArray().Select(level => new TownLevel(
                (string)level!["name"]!,
                (int)level["width"]!,
                (int)level["height"]!,
                [.. level["gates"]!.AsArray().Select(g => new Gate((string)g!["name"]!, new Spot((int)g["x"]!, (int)g["y"]!), (float)g["perHour"]!))],
                [.. (level["blocked"]?.AsArray() ?? []).Select(b => new Spot((int)b![0]!, (int)b[1]!))],
                (int)level["budget"]!,
                (float)level["delay"]!,
                (int?)level["minutes"] ?? 10)
            {
                Bus = level["bus"] is { } bus
                    ? new BusRoute((string)bus["from"]!, (string)bus["to"]!, (float?)bus["every"] ?? 120, [.. bus["stops"]!.AsArray().Select(s => new Spot((int)s![0]!, (int)s[1]!))])
                    : null,
            })];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new InvalidDataException($"The levels can't be read: {e.Message}", e);
        }
    }
}

/// <summary>
/// What the player has built on a level's grid: stretches of road and how each junction is run. Laid one stretch at a
/// time between neighbouring spots, never on blocked ones; <see cref="Build"/> turns it into a road network, roads
/// running from junction to junction (a spot where two stretches meet is just a bend) and every gate given a road out
/// of town.
/// </summary>
public sealed class Plan(TownLevel level)
{
    /// <summary>Metres between neighbouring spots.</summary>
    public const float Cell = 60;

    public const int StretchCost = 100;

    /// <summary>What a second lane each way adds to a stretch.</summary>
    public const int WideCost = 100;
    public const int LightsCost = 300;
    public const int RoundaboutCost = 500;

    private readonly HashSet<Stretch> _roads = [];
    private readonly Dictionary<Spot, Junction> _junctions = [];
    private readonly HashSet<Stretch> _wide = [];

    public TownLevel Level { get; } = level ?? throw new ArgumentNullException(nameof(level));

    public IReadOnlyCollection<Stretch> Roads => _roads;

    /// <summary>The stretches with two lanes each way.</summary>
    public IReadOnlyCollection<Stretch> Wide => _wide;

    /// <summary>Gives a stretch a second lane each way, or takes it away again; false where there is no road.</summary>
    public bool Widen(Stretch stretch)
    {
        if (!_roads.Contains(stretch))
        {
            return false;
        }

        if (!_wide.Remove(stretch))
        {
            _wide.Add(stretch);
        }

        return true;
    }

    /// <summary>What it all costs: every stretch, and lights and roundabouts where three or more roads meet.</summary>
    public int Cost => (_roads.Count * StretchCost) + (_wide.Count * WideCost) + Junctions.Sum(j => JunctionAt(j) switch
    {
        Junction.Lights => LightsCost,
        Junction.Roundabout => RoundaboutCost,
        _ => 0,
    });

    public bool OverBudget => Cost > Level.Budget;

    /// <summary>The spots where three or more roads meet (counting a gate's road out of town).</summary>
    public IEnumerable<Spot> Junctions => _roads.SelectMany(r => new[] { r.A, r.B }).Distinct().Where(s => Degree(s) >= 3);

    /// <summary>How many roads meet at a spot, a gate's road out counting as one.</summary>
    public int Degree(Spot spot) => _roads.Count(r => r.Touches(spot)) + (Level.Gates.Any(g => g.At == spot) ? 1 : 0);

    public Junction JunctionAt(Spot spot) => _junctions.GetValueOrDefault(spot);

    /// <summary>Lays a stretch between two neighbouring spots inside the grid, neither blocked; false if it can't be.</summary>
    public bool Lay(Spot a, Spot b)
    {
        if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) != 1 || !Level.Inside(a) || !Level.Inside(b) || Level.IsBlocked(a) || Level.IsBlocked(b))
        {
            return false;
        }

        return _roads.Add(new Stretch(a, b));
    }

    public bool Remove(Stretch stretch)
    {
        var removed = _roads.Remove(stretch);
        _wide.Remove(stretch);
        foreach (var spot in new[] { stretch.A, stretch.B }.Where(s => Degree(s) < 3))
        {
            _junctions.Remove(spot);
        }

        return removed;
    }

    /// <summary>Changes how a junction is run: plain, lights, roundabout, plain again. Only where three or more roads meet.</summary>
    public bool Cycle(Spot spot)
    {
        if (Degree(spot) < 3)
        {
            return false;
        }

        _junctions[spot] = (Junction)(((int)JunctionAt(spot) + 1) % 3);
        return true;
    }

    public void Set(Spot spot, Junction junction)
    {
        if (Degree(spot) >= 3)
        {
            _junctions[spot] = junction;
        }
    }

    public void Clear()
    {
        _roads.Clear();
        _junctions.Clear();
        _wide.Clear();
    }

    /// <summary>Where a spot is in the network, in metres: across to the east, down to the south.</summary>
    public static Vector2 World(Spot spot) => new(spot.X * Cell, -spot.Y * Cell);

    /// <summary>The spot just outside the grid beyond a gate, where its road out of town ends.</summary>
    public Spot Outside(Gate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var at = gate.At;
        return at.X == 0 ? at with { X = -1 } : at.X == Level.Width ? at with { X = Level.Width + 1 } : at.Y == 0 ? at with { Y = -1 } : at with { Y = Level.Height + 1 };
    }

    /// <summary>The plan as a road network, with the edge node of each gate.</summary>
    public (RoadNetwork Network, IReadOnlyDictionary<Gate, Node> Gates) Build()
    {
        var builder = new NetworkBuilder();
        var links = _roads.ToList();
        var outside = Level.Gates.ToDictionary(g => g, Outside);
        links.AddRange(outside.Select(o => new Stretch(o.Key.At, o.Value)));

        // Roads run between spots that aren't bends: junctions, dead ends and the ends of the roads out.
        int Count(Spot s) => links.Count(l => l.Touches(s));
        var nodes = new Dictionary<Spot, Node>();
        Node NodeAt(Spot s)
        {
            if (!nodes.TryGetValue(s, out var node))
            {
                var at = World(s);
                nodes[s] = node = builder.Node(at.X, at.Y);
            }

            return node;
        }

        var used = new HashSet<Stretch>();
        var ends = links.SelectMany(l => new[] { l.A, l.B }).Distinct().Where(s => Count(s) != 2).ToHashSet();
        foreach (var start in ends)
        {
            foreach (var first in links.Where(l => l.Touches(start) && !used.Contains(l)).ToList())
            {
                // Walk along the bends to the next end, noting the spots on the way.
                var path = new List<Spot>();
                var (at, link) = (start, first);
                used.Add(link);
                var stretches = new List<Stretch> { link };
                var next = link.Other(at);
                while (!ends.Contains(next))
                {
                    path.Add(next);
                    (at, link) = (next, links.First(l => l.Touches(next) && l != link));
                    used.Add(link);
                    stretches.Add(link);
                    next = link.Other(at);
                }

                // Two lanes each way where every stretch laid on it was widened (a road out of town goes along).
                var laid = stretches.Where(_roads.Contains).ToList();
                var lanes = laid.Count > 0 && laid.All(_wide.Contains) ? 2 : 1;
                if (next != start)
                {
                    builder.Road(NodeAt(start), NodeAt(next), lanes, 50, [.. path.Select(World)]);
                }
                else if (path.Count >= 3)
                {
                    // Round and back to the same junction: two roads, meeting halfway.
                    var middle = path.Count / 2;
                    builder.Road(NodeAt(start), NodeAt(path[middle]), lanes, 50, [.. path.Take(middle).Select(World)]);
                    builder.Road(NodeAt(path[middle]), NodeAt(start), lanes, 50, [.. path.Skip(middle + 1).Select(World)]);
                }
            }
        }

        // A loop on its own, with no junction or gate, carries no one: it is left out.
        foreach (var (spot, node) in nodes)
        {
            if (node.Roads.Count >= 3)
            {
                switch (JunctionAt(spot))
                {
                    case Junction.Lights:
                        builder.Signals(node, green: 25, actuated: true);
                        break;
                    case Junction.Roundabout:
                        builder.Roundabout(node, radius: 13);
                        break;
                }
            }
        }

        var network = builder.Build();
        return (network, outside.ToDictionary(o => o.Key, o => nodes[o.Value]));
    }

    public string ToJson() => new JsonObject
    {
        ["roads"] = new JsonArray([.. _roads.Select(r => (JsonNode?)new JsonArray(r.A.X, r.A.Y, r.B.X, r.B.Y))]),
        ["junctions"] = new JsonArray([.. _junctions.Where(j => j.Value != Junction.Plain).Select(j => (JsonNode?)new JsonArray(j.Key.X, j.Key.Y, j.Value.ToString()))]),
        ["wide"] = new JsonArray([.. _wide.Select(r => (JsonNode?)new JsonArray(r.A.X, r.A.Y, r.B.X, r.B.Y))]),
    }.ToJsonString();

    public static Plan FromJson(TownLevel level, string json)
    {
        var plan = new Plan(level);
        var root = JsonNode.Parse(json);
        foreach (var road in root?["roads"]?.AsArray() ?? [])
        {
            plan.Lay(new Spot((int)road![0]!, (int)road[1]!), new Spot((int)road[2]!, (int)road[3]!));
        }

        foreach (var wide in root?["wide"]?.AsArray() ?? [])
        {
            plan.Widen(new Stretch(new Spot((int)wide![0]!, (int)wide[1]!), new Spot((int)wide[2]!, (int)wide[3]!)));
        }

        foreach (var junction in root?["junctions"]?.AsArray() ?? [])
        {
            if (Enum.TryParse<Junction>((string?)junction![2], out var kind))
            {
                plan.Set(new Spot((int)junction[0]!, (int)junction[1]!), kind);
            }
        }

        return plan;
    }
}
