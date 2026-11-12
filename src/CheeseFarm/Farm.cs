using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheeseFarm;

/// <summary>What can be bought for the farm.</summary>
public enum Upgrade
{
    /// <summary>Another cow: more milk each day.</summary>
    Cow,

    /// <summary>Another rack in the cellar: room for more wheels.</summary>
    Rack,

    Cumin,
    Nettle,
    Herbs,

    /// <summary>A kettle that holds the milk steadier: more room in the warming band.</summary>
    Kettle,

    /// <summary>A sign by the road: one more customer a day.</summary>
    Sign,
}

/// <summary>What happened overnight: the milk the herd gave, what went to the dairy, and the wheels left unturned.</summary>
public sealed record DayReport(int Day, int Milked, int ToDairy, int DairyPay, int Neglected);

/// <summary>
/// A cheese farm, day by day. The herd gives milk every morning; a wheel takes <see cref="MilkPerWheel"/> litres
/// and a place on a cellar rack; wheels ripen as the days pass and want turning; the shop's customers buy them; the
/// money buys cows, racks, flavours, a better kettle and signs. Milk beyond the tank goes to the dairy for little.
/// </summary>
public sealed class Farm
{
    public const int MilkPerCow = 25;
    public const int MilkPerWheel = 100;
    public const int Tank = 400;
    public const int WheelsPerRack = 4;
    public const int MaxCows = 16;
    public const int MaxRacks = 8;
    public const int MaxSigns = 3;

    /// <summary>Euros a litre the dairy pays for milk the tank can't hold.</summary>
    public const float DairyPrice = 0.15f;

    private readonly List<Wheel> _wheels = [];
    private readonly HashSet<Flavour> _flavours = [Flavour.Plain];
    private readonly HashSet<int> _served = [];

    public Farm(int seed) => Seed = seed;

    public int Seed { get; }

    public int Day { get; private set; } = 1;

    public int Money { get; private set; } = 80;

    public int Cows { get; private set; } = 4;

    public int Milk { get; private set; } = MilkPerWheel;

    public int Racks { get; private set; } = 2;

    public bool GoodKettle { get; private set; }

    public int Signs { get; private set; }

    public int WheelsMade { get; private set; }

    public int WheelsSold { get; private set; }

    public int Earned { get; private set; }

    public IReadOnlyList<Wheel> Wheels => _wheels;

    public IReadOnlyCollection<Flavour> Flavours => _flavours;

    public int Room => Racks * WheelsPerRack;

    public bool CanMake => Milk >= MilkPerWheel && _wheels.Count < Room;

    public IReadOnlyList<Customer> Customers => CheeseFarm.Customers.For(Seed, Day, 3 + Signs, _flavours);

    /// <summary>Whether today's customer at <paramref name="index"/> has been served.</summary>
    public bool Served(int index) => _served.Contains(index);

    public int Due => _wheels.Count(w => w.NeedsTurning(Day));

    /// <summary>Starts a cheese of a flavour the farm has; null when there is no milk or room for it.</summary>
    public Making? StartMaking(Flavour flavour) =>
        CanMake && _flavours.Contains(flavour) ? new Making(flavour, GoodKettle, HashCode.Combine(Seed, Day, WheelsMade)) : null;

    /// <summary>Puts the finished cheese on a rack, using its milk.</summary>
    public Wheel Finish(Making making)
    {
        ArgumentNullException.ThrowIfNull(making);
        if (!making.IsDone || !CanMake)
        {
            throw new InvalidOperationException("The cheese isn't finished, or there is no milk or room for it.");
        }

        Milk -= MilkPerWheel;
        WheelsMade++;
        var wheel = new Wheel(NextId(), Day, making.Flavour, making.Made);
        _wheels.Add(wheel);
        return wheel;
    }

    public void Turn(Wheel wheel)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        wheel.Turn(Day);
    }

    /// <summary>Sells a wheel to today's customer; the price paid, or 0 when they don't want it or have been served.</summary>
    public int Sell(int customer, Wheel wheel)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        var customers = Customers;
        if (customer < 0 || customer >= customers.Count || _served.Contains(customer) || !_wheels.Contains(wheel))
        {
            return 0;
        }

        var price = customers[customer].Offer(wheel, Day);
        if (price > 0)
        {
            _wheels.Remove(wheel);
            _served.Add(customer);
            Money += price;
            Earned += price;
            WheelsSold++;
        }

        return price;
    }

    /// <summary>The best price any of today's unserved customers would pay for a wheel, and who; null when no one wants it.</summary>
    public (int Customer, int Price)? BestOffer(Wheel wheel)
    {
        var customers = Customers;
        return Enumerable.Range(0, customers.Count)
            .Where(i => !_served.Contains(i))
            .Select(i => (Customer: i, Price: customers[i].Offer(wheel, Day)))
            .Where(o => o.Price > 0)
            .OrderByDescending(o => o.Price)
            .Cast<(int, int)?>()
            .FirstOrDefault();
    }

    /// <summary>Goes to the next morning: the herd is milked, the tank's overflow sold, unturned wheels suffer.</summary>
    public DayReport NextDay()
    {
        Day++;
        _served.Clear();
        var milked = Cows * MilkPerCow;
        var kept = Math.Min(milked, Tank - Milk);
        Milk += kept;
        var toDairy = milked - kept;
        var pay = (int)MathF.Round(toDairy * DairyPrice);
        Money += pay;
        Earned += pay;

        var neglected = 0;
        foreach (var wheel in _wheels.Where(w => Day - w.LastTurned > Ripening.TurnEvery))
        {
            wheel.Neglect++;
            neglected++;
        }

        return new DayReport(Day, milked, toDairy, pay, neglected);
    }

    public int Cost(Upgrade upgrade) => upgrade switch
    {
        Upgrade.Cow => 120 + (20 * (Cows - 4)),
        Upgrade.Rack => 100 + (50 * (Racks - 2)),
        Upgrade.Cumin => 60,
        Upgrade.Nettle => 90,
        Upgrade.Herbs => 120,
        Upgrade.Kettle => 200,
        Upgrade.Sign => 150 + (100 * Signs),
        _ => throw new ArgumentOutOfRangeException(nameof(upgrade)),
    };

    /// <summary>Whether the farm can still have it (more cows, racks or signs; a flavour or the kettle once).</summary>
    public bool Available(Upgrade upgrade) => upgrade switch
    {
        Upgrade.Cow => Cows < MaxCows,
        Upgrade.Rack => Racks < MaxRacks,
        Upgrade.Cumin => !_flavours.Contains(Flavour.Cumin),
        Upgrade.Nettle => !_flavours.Contains(Flavour.Nettle),
        Upgrade.Herbs => !_flavours.Contains(Flavour.Herbs),
        Upgrade.Kettle => !GoodKettle,
        Upgrade.Sign => Signs < MaxSigns,
        _ => false,
    };

    public bool CanBuy(Upgrade upgrade) => Available(upgrade) && Money >= Cost(upgrade);

    public bool Buy(Upgrade upgrade)
    {
        if (!CanBuy(upgrade))
        {
            return false;
        }

        Money -= Cost(upgrade);
        switch (upgrade)
        {
            case Upgrade.Cow:
                Cows++;
                break;
            case Upgrade.Rack:
                Racks++;
                break;
            case Upgrade.Cumin:
                _flavours.Add(Flavour.Cumin);
                break;
            case Upgrade.Nettle:
                _flavours.Add(Flavour.Nettle);
                break;
            case Upgrade.Herbs:
                _flavours.Add(Flavour.Herbs);
                break;
            case Upgrade.Kettle:
                GoodKettle = true;
                break;
            case Upgrade.Sign:
                Signs++;
                break;
        }

        return true;
    }

    private int NextId() => _wheels.Count == 0 ? WheelsMade : Math.Max(WheelsMade, _wheels.Max(w => w.Id) + 1);

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["seed"] = Seed,
        ["day"] = Day,
        ["money"] = Money,
        ["cows"] = Cows,
        ["milk"] = Milk,
        ["racks"] = Racks,
        ["kettle"] = GoodKettle,
        ["signs"] = Signs,
        ["made"] = WheelsMade,
        ["sold"] = WheelsSold,
        ["earned"] = Earned,
        ["flavours"] = new JsonArray([.. _flavours.Order().Select(f => (JsonNode?)f.ToString())]),
        ["served"] = new JsonArray([.. _served.Order().Select(s => (JsonNode?)s)]),
        ["wheels"] = new JsonArray([.. _wheels.Select(w => (JsonNode?)new JsonObject
        {
            ["id"] = w.Id,
            ["madeOn"] = w.MadeOn,
            ["flavour"] = w.Flavour.ToString(),
            ["made"] = Math.Round(w.Made, 3),
            ["turned"] = w.LastTurned,
            ["neglect"] = w.Neglect,
        })]),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <exception cref="InvalidDataException">The text isn't a saved farm.</exception>
    public static Farm FromJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json)!;
            var farm = new Farm((int)root["seed"]!)
            {
                Day = (int)root["day"]!,
                Money = (int)root["money"]!,
                Cows = (int)root["cows"]!,
                Milk = (int)root["milk"]!,
                Racks = (int)root["racks"]!,
                GoodKettle = (bool?)root["kettle"] ?? false,
                Signs = (int?)root["signs"] ?? 0,
                WheelsMade = (int?)root["made"] ?? 0,
                WheelsSold = (int?)root["sold"] ?? 0,
                Earned = (int?)root["earned"] ?? 0,
            };
            foreach (var flavour in root["flavours"]?.AsArray() ?? [])
            {
                farm._flavours.Add(Enum.Parse<Flavour>((string)flavour!));
            }

            foreach (var served in root["served"]?.AsArray() ?? [])
            {
                farm._served.Add((int)served!);
            }

            foreach (var node in root["wheels"]?.AsArray() ?? [])
            {
                farm._wheels.Add(new Wheel((int)node!["id"]!, (int)node["madeOn"]!, Enum.Parse<Flavour>((string)node["flavour"]!), (float)node["made"]!)
                {
                    LastTurned = (int)node["turned"]!,
                    Neglect = (int?)node["neglect"] ?? 0,
                });
            }

            return farm;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or FormatException or ArgumentException)
        {
            throw new InvalidDataException($"The farm can't be read: {e.Message}", e);
        }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Day {Day}: €{Money}, {Cows} cows, {Milk} L, {_wheels.Count}/{Room} wheels");
}
