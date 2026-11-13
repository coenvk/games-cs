namespace CheeseFarm;

/// <summary>A wheel sold by the piece at the market, and what its pieces fetched.</summary>
public sealed record MarketSale(Wheel Wheel, int Price);

/// <summary>A day at the market: each wheel's takings, the stall's fee, and what the farm took home.</summary>
public sealed record MarketResult(IReadOnlyList<MarketSale> Sales, int Fee)
{
    public int Total => Sales.Sum(s => s.Price) - Fee;
}

/// <summary>
/// The weekly market in town, midweek: the farm takes a cart of up to <see cref="Cart"/> wheels and sells them by the
/// piece. Cut into pieces a wheel fetches more than whole, whatever its ripeness or flavour, as the crowd that day
/// allows; the stall costs a fee.
/// </summary>
public static class Market
{
    public const int Every = 7;

    /// <summary>Wheels the cart holds.</summary>
    public const int Cart = 4;

    /// <summary>Euros for the stall.</summary>
    public const int Fee = 10;

    /// <summary>What selling by the piece adds over a whole wheel's price.</summary>
    public const float ByThePiece = 1.3f;

    /// <summary>Whether the market is on: midweek, three days after each fair.</summary>
    public static bool IsOn(int day) => day > 0 && day % Every == 3;

    /// <summary>How busy the market is on a day, 0.8 to 1.2: the same for the same farm and day.</summary>
    public static float Crowd(int seed, int day) => 0.8f + (float)(new Random(Stable.Seed(seed, day, 6151)).NextDouble() * 0.4);

    /// <summary>What a wheel's pieces fetch: its ripeness's price, scaled by stars and size, by the piece, and the crowd.</summary>
    public static int Price(Wheel wheel, int day, float crowd)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        var price = Ripening.Price(wheel.Ripeness(day)) * (0.6f + (0.1f * wheel.Stars)) * Sizes.PriceFactor(wheel.Size) * ByThePiece * crowd;
        return (int)MathF.Round(price);
    }
}
