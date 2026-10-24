namespace Paddock;

/// <summary>A car's upgrades: points bought in engine, weight and chassis, over all stages.</summary>
/// <param name="Engine">Engine points: top speed and acceleration.</param>
/// <param name="Weight">Weight points: acceleration and handling.</param>
/// <param name="Chassis">Chassis points: handling.</param>
public readonly record struct Tune(int Engine, int Weight, int Chassis)
{
    /// <summary>All points bought.</summary>
    public int Total => Engine + Weight + Chassis;

    /// <summary>The points in one area.</summary>
    public int this[Area area] => area switch
    {
        Area.Engine => Engine,
        Area.Weight => Weight,
        _ => Chassis,
    };

    /// <summary>As on the card: "3.3.2".</summary>
    public override string ToString() => $"{Engine}.{Weight}.{Chassis}";
}

/// <summary>Where an upgrade point goes.</summary>
public enum Area { Engine, Weight, Chassis }

/// <summary>A car in the player's garage (or an opponent's hand): a model with its own stage and tune.</summary>
/// <param name="Id">This copy's id; two copies of a model are different cars.</param>
/// <param name="ModelId">The roster car it is.</param>
/// <param name="Stage">1 to 3; fusing raises it.</param>
/// <param name="Tune">The upgrade points bought.</param>
public sealed record CarInstance(Guid Id, string ModelId, int Stage, Tune Tune)
{
    /// <summary>A new car: stage 1, nothing bought.</summary>
    public static CarInstance New(string modelId) => new(Guid.NewGuid(), modelId, 1, default);
}
