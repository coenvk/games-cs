namespace CheeseFarm;

/// <summary>
/// How far a cheese has ripened, as Dutch farmhouse cheese is sold: young, young matured, matured, extra matured, old
/// and very old. A day on the farm stands for a few weeks in the cellar.
/// </summary>
public enum Ripeness
{
    Young,
    YoungMatured,
    Matured,
    ExtraMatured,
    Old,
    VeryOld,
}

/// <summary>What goes into the curd besides milk. Plain from the start; the others are bought for the kitchen.</summary>
public enum Flavour
{
    Plain,
    Cumin,
    Nettle,
    Herbs,
}

/// <summary>How big a cheese is made: a baby cheese, a farm wheel, or a big wheel.</summary>
public enum CheeseSize
{
    Baby,
    Wheel,
    Big,
}

/// <summary>
/// What a cheese's size means: the milk it takes, how fast it ripens (a baby cheese twice as fast, a big wheel half),
/// and what it fetches against a farm wheel of the same ripeness.
/// </summary>
public static class Sizes
{
    public static int Milk(CheeseSize size) => size switch
    {
        CheeseSize.Baby => 50,
        CheeseSize.Big => 200,
        _ => 100,
    };

    /// <summary>Days of ripening a day in the cellar counts for.</summary>
    public static float Pace(CheeseSize size) => size switch
    {
        CheeseSize.Baby => 2,
        CheeseSize.Big => 0.5f,
        _ => 1,
    };

    public static float PriceFactor(CheeseSize size) => size switch
    {
        CheeseSize.Baby => 0.55f,
        CheeseSize.Big => 2.3f,
        _ => 1,
    };
}

public static class Ripening
{
    /// <summary>The first day of age each ripeness starts at (young from the day it is made).</summary>
    private static readonly int[] Starts = [0, 3, 6, 10, 15, 25];

    /// <summary>The most a wheel of each ripeness fetches at four stars, in euros.</summary>
    private static readonly int[] Prices = [12, 15, 19, 24, 30, 38];

    /// <summary>A wheel wants turning every this many days; left longer, its rind cracks and it loses quality.</summary>
    public const int TurnEvery = 2;

    public static Ripeness At(int age)
    {
        var ripeness = Ripeness.Young;
        for (var i = 0; i < Starts.Length; i++)
        {
            if (age >= Starts[i])
            {
                ripeness = (Ripeness)i;
            }
        }

        return ripeness;
    }

    /// <summary>Days of age at which a cheese becomes <paramref name="ripeness"/>.</summary>
    public static int StartOf(Ripeness ripeness) => Starts[(int)ripeness];

    public static int Price(Ripeness ripeness) => Prices[(int)ripeness];
}

/// <summary>
/// A wheel of cheese on a cellar shelf: when it was made, with what, how well (0 to 1), and how it has been looked
/// after since. Each day it goes unturned past <see cref="Ripening.TurnEvery"/> counts against it.
/// </summary>
public sealed class Wheel(int id, int madeOn, Flavour flavour, float made)
{
    public int Id { get; } = id;

    public int MadeOn { get; } = madeOn;

    public Flavour Flavour { get; } = flavour;

    /// <summary>How well it was made: 0 (a disaster) to 1 (perfect).</summary>
    public float Made { get; } = Math.Clamp(made, 0, 1);

    public int LastTurned { get; set; } = madeOn;

    /// <summary>Days it lay unturned when it needed turning.</summary>
    public int Neglect { get; set; }

    /// <summary>A baby cheese, a farm wheel or a big wheel.</summary>
    public CheeseSize Size { get; init; } = CheeseSize.Wheel;

    public int Age(int day) => Math.Max(0, day - MadeOn);

    /// <summary>How far it has ripened, in days of a farm wheel: faster for a baby cheese, slower for a big wheel.</summary>
    public int Ripened(int day) => (int)(Age(day) * Sizes.Pace(Size));

    public Ripeness Ripeness(int day) => Ripening.At(Ripened(day));

    /// <summary>Days until it is <paramref name="ripeness"/>, 0 when it already is.</summary>
    public int DaysTo(Ripeness ripeness, int day) =>
        Math.Max(0, (int)MathF.Ceiling((Ripening.StartOf(ripeness) - Ripened(day)) / Sizes.Pace(Size)));

    public bool NeedsTurning(int day) => day - LastTurned >= Ripening.TurnEvery;

    /// <summary>Care so far: 1 when always turned in time, less for every day of neglect.</summary>
    public float Care => MathF.Max(0, 1 - (Neglect * 0.12f));

    /// <summary>Its quality now: mostly how it was made, the rest how it was cared for.</summary>
    public float Quality => (Made * 0.8f) + (Care * 0.2f);

    /// <summary>
    /// One to five stars for <see cref="Quality"/>: five takes nine tenths (near enough every step done well and the
    /// wheel cared for), four three quarters, three three fifths, two two fifths.
    /// </summary>
    public int Stars => Quality switch
    {
        >= 0.9f => 5,
        >= 0.75f => 4,
        >= 0.6f => 3,
        >= 0.4f => 2,
        _ => 1,
    };

    public void Turn(int day) => LastTurned = Math.Max(LastTurned, day);
}
