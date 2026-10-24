namespace Paddock;

/// <summary>A car's rarity class, from its RQ: F (common, weak) up to S.</summary>
public enum Rarity { F, E, D, C, B, A, S }

/// <summary>Which wheels drive the car.</summary>
public enum Drive { Fwd, Rwd, Awd }

/// <summary>The tyres a car runs on; each grips differently on each surface.</summary>
public enum Tyres { Slick, Performance, Standard, AllSurface, OffRoad }

/// <summary>Ground clearance; low cars slow down on rough surfaces.</summary>
public enum Clearance { Low, Mid, High }

/// <summary>The surface and weather a round is raced in.</summary>
public enum Condition { Dry, Wet, Dirt, Gravel, Snow, Ice, Sand }

/// <summary>The kind of car, which decides how it is drawn.</summary>
public enum Body { Hatchback, Saloon, Estate, Coupe, Roadster, Muscle, Suv, Pickup, Rally, Supercar, Hypercar, Van }

/// <summary>A car's performance.</summary>
/// <param name="TopSpeedKmh">Top speed in km/h.</param>
/// <param name="ZeroTo100">Seconds from standstill to 100 km/h.</param>
/// <param name="Handling">Cornering ability, 0 to 100.</param>
/// <param name="WeightKg">Weight in kg.</param>
/// <param name="Drive">Driven wheels.</param>
/// <param name="Tyres">Tyres.</param>
/// <param name="Clearance">Ground clearance.</param>
public sealed record Stats(double TopSpeedKmh, double ZeroTo100, double Handling, double WeightKg, Drive Drive, Tyres Tyres, Clearance Clearance);

/// <summary>One car of the roster: an invented model from an invented maker.</summary>
/// <param name="Id">Stable id, used in saves.</param>
/// <param name="Name">The model's name.</param>
/// <param name="Maker">The maker's name.</param>
/// <param name="Country">The maker's country (for event rules).</param>
/// <param name="Year">Model year.</param>
/// <param name="Body">Kind of car.</param>
/// <param name="Stats">Performance, before upgrades.</param>
/// <param name="Paint">Body colour for the art, "#rrggbb".</param>
/// <param name="Trim">Trim colour for the art, "#rrggbb".</param>
public sealed record CarModel(string Id, string Name, string Maker, string Country, int Year, Body Body, Stats Stats, string Paint, string Trim);

/// <summary>Every car in the game, with each one's RQ and rarity class.</summary>
public sealed class Roster
{
    private readonly Dictionary<string, CarModel> _byId;
    private readonly Dictionary<string, int> _rq = [];
    private readonly Func<Stats, int> _rate;

    private Roster(IReadOnlyList<CarModel> cars, Func<Stats, int> rate)
    {
        All = cars;
        _byId = [];
        foreach (var car in cars)
        {
            if (!_byId.TryAdd(car.Id, car))
            {
                throw new FormatException($"The car id '{car.Id}' is used twice.");
            }
        }

        _rate = rate;
    }

    /// <summary>Every car, in roster order.</summary>
    public IReadOnlyList<CarModel> All { get; }

    /// <summary>Reads the roster from <c>cars.json</c>.</summary>
    /// <param name="json">The file's contents.</param>
    /// <param name="rate">Works out a car's RQ from its stats; <see cref="Rating.Rq"/> by default.</param>
    public static Roster FromJson(string json, Func<Stats, int>? rate = null)
    {
        var file = System.Text.Json.JsonSerializer.Deserialize(json, PaddockJson.Default.RosterFile)
            ?? throw new FormatException("The roster is empty.");
        return new Roster(file.Cars, rate ?? Rating.Rq);
    }

    /// <summary>The car with this id, or null if there is none (e.g. in an old save).</summary>
    public CarModel? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The car's RQ: its strength rating, which counts towards a hand's limit. Upgrades don't change it.</summary>
    public int Rq(string id)
    {
        if (!_rq.TryGetValue(id, out var rq))
        {
            rq = _rq[id] = _rate(_byId[id].Stats);
        }

        return rq;
    }

    /// <summary>The car's rarity class.</summary>
    public Rarity Class(string id) => Rating.ClassOf(Rq(id));
}

/// <summary>The contents of <c>cars.json</c>.</summary>
internal sealed record RosterFile(IReadOnlyList<CarModel> Cars);
