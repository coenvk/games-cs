namespace Paddock;

/// <summary>
/// A kind of car's silhouette in the 64x24 side view (front to the right), in pixels: how long it is, where the bonnet,
/// cabin and roof sit, how high it rides and where the wheels go. The sprites and the 3D cars are both made from it.
/// </summary>
/// <param name="Length">Body length.</param>
/// <param name="Hood">Top of the bonnet and boot (y).</param>
/// <param name="Sill">Bottom of the body (y); higher cars have more ground clearance.</param>
/// <param name="CabinFrom">Where the cabin starts, as a share of the length from the back.</param>
/// <param name="CabinTo">Where the cabin ends (the windscreen's foot).</param>
/// <param name="Roof">Top of the roof (y); 0 for no roof.</param>
/// <param name="Wheel">Wheel radius.</param>
/// <param name="RearAxle">Rear axle, as a share of the length.</param>
/// <param name="FrontAxle">Front axle, as a share of the length.</param>
/// <param name="TopLength">Length in the 18x10 top view.</param>
/// <param name="TopWidth">Width in the top view.</param>
public sealed record BodyShape(
    int Length, int Hood, int Sill, double CabinFrom, double CabinTo, int Roof, int Wheel, double RearAxle, double FrontAxle,
    int TopLength, int TopWidth)
{
    public static BodyShape Of(Body body) => body switch
    {
        Body.Hatchback => new(44, 12, 19, 0.06, 0.70, 6, 4, 0.17, 0.83, 13, 7),
        Body.Saloon => new(52, 12, 19, 0.26, 0.70, 6, 4, 0.18, 0.80, 15, 8),
        Body.Estate => new(54, 12, 19, 0.06, 0.70, 6, 4, 0.18, 0.80, 15, 8),
        Body.Coupe => new(50, 13, 19, 0.28, 0.68, 8, 4, 0.18, 0.80, 15, 8),
        Body.Roadster => new(44, 14, 19, 0.40, 0.60, 0, 4, 0.18, 0.82, 13, 7),
        Body.Muscle => new(56, 12, 18, 0.30, 0.62, 7, 5, 0.17, 0.80, 16, 8),
        Body.Suv => new(52, 10, 16, 0.06, 0.66, 3, 5, 0.18, 0.80, 15, 9),
        Body.Pickup => new(58, 10, 16, 0.42, 0.64, 3, 5, 0.17, 0.82, 17, 9),
        Body.Rally => new(46, 12, 18, 0.20, 0.70, 6, 4, 0.17, 0.83, 13, 7),
        Body.Supercar => new(54, 15, 20, 0.35, 0.64, 10, 4, 0.18, 0.80, 16, 8),
        Body.Hypercar => new(58, 16, 21, 0.38, 0.62, 11, 4, 0.18, 0.82, 17, 8),
        _ => new(54, 12, 18, 0.00, 0.82, 2, 4, 0.17, 0.82, 16, 9), // Van
    };
}

/// <summary>Styling by decade: chrome and round lamps, then wedges and black plastic, then smooth modern lines.</summary>
public enum Era
{
    Classic,
    Wedge,
    Modern,
}

public static class EraExtensions
{
    extension(Era)
    {
        public static Era Of(int year) => year < 1975 ? Era.Classic : year < 1990 ? Era.Wedge : Era.Modern;
    }
}
