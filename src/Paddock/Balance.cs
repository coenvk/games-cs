namespace Paddock;

/// <summary>Every tunable number of the game in one place.</summary>
public static class Balance
{
    /// <summary>Gravity, m/s².</summary>
    public const double G = 9.81;

    /// <summary>Grip of each kind of tyre on each condition (Dry, Wet, Dirt, Gravel, Snow, Ice, Sand).</summary>
    private static readonly double[,] TyreGripTable =
    {
        { 1.10, 0.55, 0.45, 0.45, 0.30, 0.15, 0.35 }, // Slick
        { 1.00, 0.75, 0.55, 0.55, 0.40, 0.20, 0.40 }, // Performance
        { 0.90, 0.80, 0.65, 0.65, 0.50, 0.25, 0.50 }, // Standard
        { 0.85, 0.80, 0.80, 0.80, 0.65, 0.35, 0.65 }, // AllSurface
        { 0.75, 0.70, 0.90, 0.90, 0.75, 0.40, 0.85 }, // OffRoad
    };

    /// <summary>How well <paramref name="tyres"/> grip in <paramref name="condition"/> (1 = a good dry road tyre).</summary>
    public static double TyreGrip(Tyres tyres, Condition condition) => TyreGripTable[(int)tyres, (int)condition];

    /// <summary>Cornering grip: handling (0-100) scales the tyres' grip.</summary>
    public static double CorneringGrip(double handling, Tyres tyres, Condition condition) =>
        (0.3 + (handling / 100)) * TyreGrip(tyres, condition);

    /// <summary>How much of the tyres' grip can push the car off the line.</summary>
    public static double DriveTraction(Drive drive) => drive switch
    {
        Drive.Fwd => 0.72,
        Drive.Rwd => 0.8,
        _ => 1.04,
    };

    /// <summary>Whether a condition is rough enough for ground clearance to matter.</summary>
    public static bool IsRough(Condition condition) => condition is Condition.Dirt or Condition.Gravel or Condition.Sand or Condition.Snow;

    /// <summary>The share of top speed a car keeps on a rough surface.</summary>
    public static double RoughSpeed(Clearance clearance) => clearance switch
    {
        Clearance.Low => 0.80,
        Clearance.Mid => 0.93,
        _ => 1.0,
    };

    /// <summary>The share of grip a car keeps on a rough surface: a low car scrapes and bounces.</summary>
    public static double RoughGrip(Clearance clearance) => clearance switch
    {
        Clearance.Low => 0.85,
        Clearance.Mid => 0.95,
        _ => 1.0,
    };

    /// <summary>Top speed gained per engine point (share).</summary>
    public const double EngineTopSpeed = 0.012;

    /// <summary>0-100 time saved per engine point (share).</summary>
    public const double EngineAcceleration = 0.02;

    /// <summary>Weight saved per weight point (share).</summary>
    public const double WeightSaving = 0.015;

    /// <summary>0-100 time saved per weight point (share).</summary>
    public const double WeightAcceleration = 0.01;

    /// <summary>Handling gained per weight point.</summary>
    public const double WeightHandling = 0.5;

    /// <summary>Handling gained per chassis point.</summary>
    public const double ChassisHandling = 1.5;

    /// <summary>The price of a stage-1 upgrade point for a car of this class; later points and stages cost more.</summary>
    public static long UpgradeBase(Rarity rarity) => rarity switch
    {
        Rarity.F => 300,
        Rarity.E => 500,
        Rarity.D => 900,
        Rarity.C => 1_600,
        Rarity.B => 3_000,
        Rarity.A => 6_000,
        _ => 12_000,
    };

    /// <summary>A car of this weight (kg) brakes as its grip allows; lighter cars brake harder, heavier ones less.</summary>
    public const double ReferenceWeight = 1300;

    /// <summary>Braking as a share of cornering grip.</summary>
    public const double Braking = 0.9;
}
