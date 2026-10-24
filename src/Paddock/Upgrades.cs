namespace Paddock;

/// <summary>
/// Upgrading, as in the spec: three stages; each stage takes 8 points with at most 3 per area per stage reached (so
/// an area can hold 3, 6 or 9 points); a full stage needs another car of the same class fused in to go on.
/// </summary>
public static class Upgrades
{
    /// <summary>Points one stage takes.</summary>
    public const int PointsPerStage = 8;

    /// <summary>The most points per area per stage reached.</summary>
    public const int PointsPerAreaPerStage = 3;

    /// <summary>The last stage.</summary>
    public const int MaxStage = 3;

    /// <summary>Points bought in the car's current stage (0 to 8).</summary>
    public static int PointsInStage(CarInstance car) => car.Tune.Total - (PointsPerStage * (car.Stage - 1));

    /// <summary>Whether the current stage has all its points.</summary>
    public static bool IsStageFull(CarInstance car) => PointsInStage(car) >= PointsPerStage;

    /// <summary>Whether a point can go into <paramref name="area"/> now.</summary>
    public static bool CanUpgrade(CarInstance car, Area area) =>
        !IsStageFull(car) && car.Tune[area] < PointsPerAreaPerStage * car.Stage;

    /// <summary>Buys one point in <paramref name="area"/> (the caller pays <see cref="Cost"/>).</summary>
    public static CarInstance Upgrade(CarInstance car, Area area)
    {
        if (!CanUpgrade(car, area))
        {
            throw new InvalidOperationException($"No more {area} points fit in stage {car.Stage}.");
        }

        var t = car.Tune;
        return car with
        {
            Tune = area switch
            {
                Area.Engine => t with { Engine = t.Engine + 1 },
                Area.Weight => t with { Weight = t.Weight + 1 },
                _ => t with { Chassis = t.Chassis + 1 },
            },
        };
    }

    /// <summary>
    /// The price of the next point: the class's base price, times the stage the point belongs to, plus a quarter for
    /// every point already bought in that stage.
    /// </summary>
    public static long Cost(CarInstance car, Area area, Rarity rarity)
    {
        var bought = car.Tune.Total;
        var stage = (bought / PointsPerStage) + 1;
        var inStage = bought % PointsPerStage;
        return (long)Math.Round(Balance.UpgradeBase(rarity) * stage * (1 + (0.25 * inStage)));
    }

    /// <summary>Whether <paramref name="donor"/> can be fused into <paramref name="target"/>: another car of the same class, into a full stage below the last.</summary>
    public static bool CanFuse(CarInstance target, CarInstance donor, Rarity targetClass, Rarity donorClass) =>
        target.Id != donor.Id && targetClass == donorClass && IsStageFull(target) && target.Stage < MaxStage;

    /// <summary>The target after a fuse: the next stage, its tune kept (the caller removes the donor).</summary>
    public static CarInstance Fuse(CarInstance target) => target with { Stage = target.Stage + 1 };

    /// <summary>Takes every point back (they are paid for again); the stage stays.</summary>
    public static CarInstance Reset(CarInstance car) => car with { Tune = default };

    /// <summary>The stats with a tune's points applied.</summary>
    public static Stats Apply(Stats basis, Tune tune) => basis with
    {
        TopSpeedKmh = basis.TopSpeedKmh * (1 + (Balance.EngineTopSpeed * tune.Engine)),
        ZeroTo100 = basis.ZeroTo100 * (1 - (Balance.EngineAcceleration * tune.Engine)) * (1 - (Balance.WeightAcceleration * tune.Weight)),
        WeightKg = basis.WeightKg * (1 - (Balance.WeightSaving * tune.Weight)),
        Handling = Math.Min(100, basis.Handling + (Balance.WeightHandling * tune.Weight) + (Balance.ChassisHandling * tune.Chassis)),
    };
}
