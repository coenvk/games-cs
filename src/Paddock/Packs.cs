using Jentl.Collect;

namespace Paddock;

/// <summary>A pack of random cars for sale.</summary>
/// <param name="Id">Stable id (also its English name, lower case).</param>
/// <param name="Name">Shown name (English; translated through the language file).</param>
/// <param name="Price">Cash it costs.</param>
/// <param name="Cars">How many cars it holds.</param>
/// <param name="Weights">How likely each class is, per car.</param>
public sealed record PackDef(string Id, string Name, long Price, int Cars, IReadOnlyDictionary<Rarity, double> Weights);

/// <summary>The packs on sale, and opening them.</summary>
public static class Packs
{
    /// <summary>Cheap cars: mostly F, some E, a few D.</summary>
    public static readonly PackDef Starter = new("starter", "Starter pack", 2_000, 3,
        new Dictionary<Rarity, double> { [Rarity.F] = 60, [Rarity.E] = 30, [Rarity.D] = 10 });

    /// <summary>The middle classes, with a small chance of an A.</summary>
    public static readonly PackDef Classic = new("classic", "Classic pack", 8_000, 3,
        new Dictionary<Rarity, double> { [Rarity.D] = 40, [Rarity.C] = 35, [Rarity.B] = 20, [Rarity.A] = 5 });

    /// <summary>The best cars: B and A, and the only pack with S.</summary>
    public static readonly PackDef Elite = new("elite", "Elite pack", 25_000, 3,
        new Dictionary<Rarity, double> { [Rarity.B] = 50, [Rarity.A] = 38, [Rarity.S] = 12 });

    /// <summary>Every pack, cheapest first.</summary>
    public static IReadOnlyList<PackDef> All { get; } = [Starter, Classic, Elite];

    /// <summary>Draws a pack's cars: a class by the weights, then any car of that class.</summary>
    public static IReadOnlyList<CarInstance> Open(PackDef pack, Roster roster, Random random)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(random);
        var byClass = roster.All.GroupBy(c => roster.Class(c.Id)).ToDictionary(g => g.Key, g => g.ToList());
        var classes = new WeightedTable<Rarity>(pack.Weights.Where(w => byClass.ContainsKey(w.Key)).Select(w => (w.Key, w.Value)));
        var cars = new List<CarInstance>();
        for (var i = 0; i < pack.Cars; i++)
        {
            var pool = byClass[classes.Draw(random)];
#pragma warning disable CA5394 // Game randomness, not security relevant.
            cars.Add(CarInstance.New(pool[random.Next(pool.Count)].Id));
#pragma warning restore CA5394
        }

        return cars;
    }

    /// <summary>Buys and opens a pack if the player can pay; the cars go into the garage.</summary>
    public static bool Buy(Profile profile, PackDef pack, Roster roster, Random random, out IReadOnlyList<CarInstance> cars)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(pack);
        if (profile.Cash < pack.Price)
        {
            cars = [];
            return false;
        }

        profile.Cash -= pack.Price;
        cars = Open(pack, roster, random);
        profile.Garage.AddRange(cars);
        return true;
    }
}
