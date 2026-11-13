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

/// <summary>
/// What happened overnight: the milk the herd gave, what went to the dairy, the wheels left unturned, the feed bought
/// for cows the meadow couldn't feed (or the cows that went hungry when there was no money for it), and the meadow
/// birds' chicks that fledged; and the calf born in the night, or the one grown into a cow that gives milk.
/// </summary>
public sealed record DayReport(int Day, int Milked, int ToDairy, int DairyPay, int Neglected, int FeedBought = 0, int Hungry = 0, int Fledged = 0)
{
    /// <summary>Bales of hay the herd ate in the barn (in winter).</summary>
    public int HayEaten { get; init; }

    /// <summary>The season that began this morning, if one did.</summary>
    public Season? NewSeason { get; init; }

    /// <summary>The name of a calf born in the night, if one was.</summary>
    public string? Calf { get; init; }

    /// <summary>The name of a calf grown up this morning, now giving milk, if one did.</summary>
    public string? Grown { get; init; }
}

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
    private readonly List<Cow> _herd = [];
    private readonly Field[] _fields = new Field[Meadow.Fields];
    private readonly int[] _fieldSince = Enumerable.Repeat(1, Meadow.Fields).ToArray();
    private readonly int[] _mown = new int[Meadow.Fields];

    public Farm(int seed)
    {
        Seed = seed;
        for (var i = 0; i < 4; i++)
        {
            _herd.Add(new Cow(CheeseFarm.Herd.NameFor(_herd), 1 - CheeseFarm.Herd.CalfDays));
        }
    }

    public int Seed { get; }

    public int Day { get; private set; } = 1;

    public int Money { get; private set; } = 80;

    /// <summary>The herd: cows that give milk, and calves growing up.</summary>
    public IReadOnlyList<Cow> Herd => _herd;

    /// <summary>The cows giving milk.</summary>
    public int Cows => _herd.Count(IsGrown);

    /// <summary>Whether a cow is grown and gives milk, rather than a calf.</summary>
    public bool IsGrown(Cow cow) => Day - (cow ?? throw new ArgumentNullException(nameof(cow))).Born >= CheeseFarm.Herd.CalfDays;

    public int Milk { get; private set; } = MilkPerWheel;

    public int Racks { get; private set; } = 2;

    public bool GoodKettle { get; private set; }

    public int Signs { get; private set; }

    public int WheelsMade { get; private set; }

    public int WheelsSold { get; private set; }

    public int Earned { get; private set; }

    /// <summary>The day a cow was last milked by hand (0: never).</summary>
    public int HandMilked { get; private set; }

    /// <summary>Whether there is time this morning to milk a cow by hand: once a day.</summary>
    public bool CanMilkByHand => HandMilked != Day && Cows > 0;

    /// <summary>Pours the pail milked by hand into the tank, as much as fits; the litres it took.</summary>
    public int MilkByHand(Milking milking)
    {
        ArgumentNullException.ThrowIfNull(milking);
        if (!CanMilkByHand)
        {
            return 0;
        }

        HandMilked = Day;
        var litres = Math.Min((int)MathF.Round(milking.Litres), Tank - Milk);
        Milk += litres;
        return litres;
    }

    /// <summary>The day a wheel was last entered at the fair (0: never).</summary>
    public int FairEntered { get; private set; }

    /// <summary>Rosettes won at the fair: first, second and third places.</summary>
    public IReadOnlyList<int> Rosettes => _rosettes;

    private readonly int[] _rosettes = new int[3];

    /// <summary>Whether a wheel can go to the fair today: it is on, and none has been entered yet.</summary>
    public bool CanEnterFair => Fair.IsOn(Day) && FairEntered != Day && _wheels.Count > 0;

    /// <summary>
    /// Takes a wheel from the cellar to the fair and has it judged: the prize is paid and a rosette kept for a place
    /// in the first three. Null when it can't go today.
    /// </summary>
    public FairResult? EnterFair(Wheel wheel, string name)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        if (!CanEnterFair || !_wheels.Contains(wheel))
        {
            return null;
        }

        var result = Fair.Judge(name, wheel, Seed, Day);
        _wheels.Remove(wheel);
        FairEntered = Day;
        Money += result.Prize;
        Earned += result.Prize;
        if (result.Place <= _rosettes.Length)
        {
            _rosettes[result.Place - 1]++;
        }

        return result;
    }

    public IReadOnlyList<Wheel> Wheels => _wheels;

    public IReadOnlyCollection<Flavour> Flavours => _flavours;

    public int Room => Racks * WheelsPerRack;

    /// <summary>Whether a cheese of some size can be made: milk for a baby cheese at least, and room on a rack.</summary>
    public bool CanMake => CanMakeSize(CheeseSize.Baby);

    /// <summary>Whether there is the milk for a cheese of <paramref name="size"/>, and room for it.</summary>
    public bool CanMakeSize(CheeseSize size) => Milk >= Sizes.Milk(size) && _wheels.Count < Room;

    public IReadOnlyList<Customer> Customers => CheeseFarm.Customers.For(Seed, Day, 3 + Signs + Meadow.Walkers(FieldsOf(Field.Birds)), _flavours);

    /// <summary>The meadow's fields, each grass, herbs or left for the birds.</summary>
    public IReadOnlyList<Field> Fields => _fields;

    /// <summary>The day a field became what it is.</summary>
    public int FieldSince(int field) => _fieldSince[field];

    public int FieldsOf(Field kind) => _fields.Count(f => f == kind);

    /// <summary>How many cows the meadow feeds (none in winter, the herd in the barn); the rest eat hay or bought feed.</summary>
    public int Grazing => Seasons.Grazing(Season) ? _fields.Sum(Meadow.Feeds) : 0;

    public Season Season => Seasons.Of(Day);

    public int Year => Seasons.YearOf(Day);

    /// <summary>Bales of hay in the barn, for the winter.</summary>
    public int Hay { get; private set; }

    /// <summary>Whether a field can be mown for hay now: in summer, once a year, while the barn has room.</summary>
    public bool CanMow(int field) => field >= 0 && field < _fields.Length && Season == Season.Summer && _mown[field] != Year && Hay < Seasons.Barn;

    /// <summary>Mows a field for hay: the bales it gave (a bird field's nests start over); 0 when it can't be mown.</summary>
    public int Mow(int field)
    {
        if (!CanMow(field))
        {
            return 0;
        }

        var bales = Math.Min(Seasons.Bales(_fields[field]), Seasons.Barn - Hay);
        Hay += bales;
        _mown[field] = Year;
        if (_fields[field] == Field.Birds)
        {
            _fieldSince[field] = Day;
        }

        return bales;
    }

    /// <summary>How rich the milk is, 0 to 1: the share of the herd grazing herbs (the herb fields are grazed first).</summary>
    public float Richness => _herd.Count == 0 || !Seasons.Grazing(Season) ? 0 : Math.Min(1, FieldsOf(Field.Herbs) * Meadow.Feeds(Field.Herbs) / (float)_herd.Count);

    /// <summary>Meadow birds' chicks fledged on the farm's bird fields, all told.</summary>
    public int Fledged { get; private set; }

    /// <summary>What turning a field into <paramref name="kind"/> costs: sowing herbs costs, the rest is free.</summary>
    public static int FieldCost(Field kind) => kind == Field.Herbs ? Meadow.SowCost : 0;

    /// <summary>Makes a field grass, herbs or a bird field; false when it already is, or there is no money to sow it.</summary>
    public bool SetField(int field, Field kind)
    {
        if (field < 0 || field >= _fields.Length || _fields[field] == kind || Money < FieldCost(kind))
        {
            return false;
        }

        Money -= FieldCost(kind);
        _fields[field] = kind;
        _fieldSince[field] = Day;
        return true;
    }

    /// <summary>Whether today's customer at <paramref name="index"/> has been served.</summary>
    public bool Served(int index) => _served.Contains(index);

    public int Due => _wheels.Count(w => w.NeedsTurning(Day));

    /// <summary>Starts a cheese of a flavour the farm has; null when there is no milk or room for it.</summary>
    public Making? StartMaking(Flavour flavour, CheeseSize size = CheeseSize.Wheel) =>
        CanMakeSize(size) && _flavours.Contains(flavour) ? new Making(flavour, GoodKettle, HashCode.Combine(Seed, Day, WheelsMade)) { Size = size } : null;

    /// <summary>Puts the finished cheese on a rack, using its milk.</summary>
    public Wheel Finish(Making making)
    {
        ArgumentNullException.ThrowIfNull(making);
        if (!making.IsDone || !CanMakeSize(making.Size))
        {
            throw new InvalidOperationException("The cheese isn't finished, or there is no milk or room for it.");
        }

        Milk -= Sizes.Milk(making.Size);
        WheelsMade++;
        var wheel = new Wheel(NextId(), Day, making.Flavour, making.Made + (Meadow.RichMilk * Richness)) { Size = making.Size };
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

        // Cows (and calves) the meadow can't feed eat bought feed; without the money for it they go hungry, the cows giving half.
        // In the barn they eat hay first.
        var unfed = Math.Max(0, _herd.Count - Grazing);
        var hay = Math.Min(Hay, unfed);
        Hay -= hay;
        unfed -= hay;
        var feed = unfed * Meadow.FeedPrice;
        var hungry = 0;
        if (Money >= feed)
        {
            Money -= feed;
        }
        else
        {
            (feed, hungry) = (0, unfed);
        }

        var milked = (Cows * MilkPerCow) - (Math.Min(hungry, Cows) * MilkPerCow / 2);
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

        // A bird field left long enough raises a brood, and another every nesting time after.
        var fledged = 0;
        for (var field = 0; field < _fields.Length; field++)
        {
            var left = Day - _fieldSince[field];
            if (_fields[field] == Field.Birds && Seasons.Nesting(Season) && left > 0 && left % Meadow.NestingDays == 0)
            {
                fledged += Meadow.Brood;
            }
        }

        Fledged += fledged;

        // Calves: one grows up after her weeks; a cow may have one in the night, while there is room in the herd.
        var grown = _herd.FirstOrDefault(c => Day - c.Born == CheeseFarm.Herd.CalfDays)?.Name;
        string? calf = null;
        var random = new Random(HashCode.Combine(Seed, Day, 4049));
        foreach (var _ in _herd.Where(IsGrown).ToList())
        {
            if (random.NextDouble() < CheeseFarm.Herd.CalvingChance && _herd.Count < MaxCows && calf is null)
            {
                calf = CheeseFarm.Herd.NameFor(_herd);
                _herd.Add(new Cow(calf, Day));
            }
        }

        return new DayReport(Day, milked, toDairy, pay, neglected, feed, hungry, fledged)
        {
            Calf = calf,
            Grown = grown,
            HayEaten = hay,
            NewSeason = Seasons.Starts(Day) ? Season : null,
        };
    }

    public int Cost(Upgrade upgrade) => upgrade switch
    {
        Upgrade.Cow => 120 + (20 * Math.Max(0, _herd.Count - 4)),
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
        Upgrade.Cow => _herd.Count < MaxCows,
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
                _herd.Add(new Cow(CheeseFarm.Herd.NameFor(_herd), Day - CheeseFarm.Herd.CalfDays));
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
        ["herd"] = new JsonArray([.. _herd.Select(c => (JsonNode?)new JsonArray(c.Name, c.Born))]),
        ["milk"] = Milk,
        ["racks"] = Racks,
        ["kettle"] = GoodKettle,
        ["signs"] = Signs,
        ["made"] = WheelsMade,
        ["sold"] = WheelsSold,
        ["earned"] = Earned,
        ["fair"] = FairEntered,
        ["handMilked"] = HandMilked,
        ["rosettes"] = new JsonArray([.. _rosettes.Select(n => (JsonNode?)n)]),
        ["fields"] = new JsonArray([.. _fields.Select(f => (JsonNode?)f.ToString())]),
        ["fieldSince"] = new JsonArray([.. _fieldSince.Select(d => (JsonNode?)d)]),
        ["fledged"] = Fledged,
        ["hay"] = Hay,
        ["mown"] = new JsonArray([.. _mown.Select(y => (JsonNode?)y)]),
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
            ["size"] = w.Size == CheeseSize.Wheel ? null : w.Size.ToString(),
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
                Milk = (int)root["milk"]!,
                Racks = (int)root["racks"]!,
                GoodKettle = (bool?)root["kettle"] ?? false,
                Signs = (int?)root["signs"] ?? 0,
                WheelsMade = (int?)root["made"] ?? 0,
                WheelsSold = (int?)root["sold"] ?? 0,
                Earned = (int?)root["earned"] ?? 0,
                FairEntered = (int?)root["fair"] ?? 0,
                HandMilked = (int?)root["handMilked"] ?? 0,
                Fledged = (int?)root["fledged"] ?? 0,
                Hay = (int?)root["hay"] ?? 0,
            };
            var mown = root["mown"]?.AsArray() ?? [];
            for (var i = 0; i < Math.Min(mown.Count, farm._mown.Length); i++)
            {
                farm._mown[i] = (int)mown[i]!;
            }

            var fields = root["fields"]?.AsArray() ?? [];
            var since = root["fieldSince"]?.AsArray() ?? [];
            for (var i = 0; i < Math.Min(fields.Count, farm._fields.Length); i++)
            {
                farm._fields[i] = Enum.Parse<Field>((string)fields[i]!);
                farm._fieldSince[i] = i < since.Count ? (int)since[i]! : farm.Day;
            }

            // The herd by name; a farm saved before names, its number of cows.
            farm._herd.Clear();
            if (root["herd"] is JsonArray herd)
            {
                farm._herd.AddRange(herd.Select(c => new Cow((string)c![0]!, (int)c[1]!)));
            }
            else
            {
                for (var i = 0; i < (int)root["cows"]!; i++)
                {
                    farm._herd.Add(new Cow(CheeseFarm.Herd.NameFor(farm._herd), farm.Day - CheeseFarm.Herd.CalfDays));
                }
            }

            var rosettes = root["rosettes"]?.AsArray() ?? [];
            for (var i = 0; i < Math.Min(rosettes.Count, farm._rosettes.Length); i++)
            {
                farm._rosettes[i] = (int)rosettes[i]!;
            }

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
                    Size = node["size"] is { } size ? Enum.Parse<CheeseSize>((string)size!) : CheeseSize.Wheel,
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
        string.Create(CultureInfo.InvariantCulture, $"Day {Day}: €{Money}, {Cows} cows, {_herd.Count - Cows} calves, {Milk} L, {_wheels.Count}/{Room} wheels");
}
