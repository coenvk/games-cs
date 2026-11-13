namespace CheeseFarm;

/// <summary>
/// Someone at the farm shop, wanting a wheel of a certain ripeness and flavour. They take a wheel of that flavour
/// one ripeness either side, paying most for exactly what they asked; <see cref="Generosity"/> is how freely they
/// spend.
/// </summary>
public sealed record Customer(string Name, Ripeness Wants, Flavour Flavour, float Generosity)
{
    public bool Accepts(Wheel wheel, int day)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        return wheel.Flavour == Flavour && Math.Abs((int)wheel.Ripeness(day) - (int)Wants) <= 1;
    }

    /// <summary>
    /// What they pay for a wheel, in whole euros: its ripeness's price, scaled by its stars (four is the list price),
    /// a tenth more for a flavour, a quarter more for exactly the ripeness asked, and their generosity; 0 when they
    /// don't want it.
    /// </summary>
    public int Offer(Wheel wheel, int day)
    {
        if (!Accepts(wheel, day))
        {
            return 0;
        }

        var ripeness = wheel.Ripeness(day);
        var price = Ripening.Price(ripeness) * (0.6f + (0.1f * wheel.Stars));
        price *= Flavour == Flavour.Plain ? 1 : 1.1f;
        price *= ripeness == Wants ? 1.25f : 1;
        price *= Sizes.PriceFactor(wheel.Size);
        return (int)MathF.Round(price * Generosity);
    }
}

/// <summary>Who comes to the shop on a day: the same people for the same farm and day, so a reload changes nothing.</summary>
public static class Customers
{
    private static readonly string[] Names =
    [
        "Anna", "Bram", "Daan", "Eva", "Fenna", "Gijs", "Hanna", "Jesse", "Lotte", "Mees",
        "Noor", "Pim", "Roos", "Sem", "Tess", "Thijs", "Vera", "Wout", "Yara", "Lieke",
    ];

    /// <param name="flavours">The flavours the farm makes; people mostly ask for those, and for plain most of all.</param>
    public static IReadOnlyList<Customer> For(int seed, int day, int count, IReadOnlyCollection<Flavour> flavours)
    {
        ArgumentNullException.ThrowIfNull(flavours);
        var random = new Random(Stable.Seed(seed, day, 7919));
        var known = flavours.Where(f => f != Flavour.Plain).ToList();

        // Early on there is only young cheese: wishes grow older with the farm.
        var oldest = (int)Ripening.At(Math.Max(0, day - 1));
        var names = Names.OrderBy(_ => random.Next()).ToList();
        return
        [
            .. Enumerable.Range(0, count).Select(i => new Customer(
                names[i % names.Count],
                (Ripeness)random.Next(0, Math.Min(oldest + 2, (int)Ripeness.VeryOld + 1)),
                known.Count > 0 && random.NextDouble() < 0.35 ? known[random.Next(known.Count)] : Flavour.Plain,
                0.9f + ((float)random.NextDouble() * 0.3f))),
        ];
    }
}
