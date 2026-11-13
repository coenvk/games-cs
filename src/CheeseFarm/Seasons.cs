namespace CheeseFarm;

public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}

/// <summary>
/// The farm's year: four seasons of <see cref="Days"/> days, starting in spring. In winter the herd stays in the barn
/// and eats hay (or bought feed when the hay runs out); in summer the fields can be mown once for hay; meadow birds
/// nest in spring and summer.
/// </summary>
public static class Seasons
{
    /// <summary>Days in a season: short, so a year goes round in a few weeks of play.</summary>
    public const int Days = 14;

    /// <summary>Bales the barn holds.</summary>
    public const int Barn = 200;

    public static Season Of(int day) => (Season)((Math.Max(1, day) - 1) / Days % 4);

    /// <summary>The year a day falls in, the first being 1.</summary>
    public static int YearOf(int day) => ((Math.Max(1, day) - 1) / (Days * 4)) + 1;

    /// <summary>Whether a day is the first of its season.</summary>
    public static bool Starts(int day) => day > 1 && (day - 1) % Days == 0;

    /// <summary>Whether the herd grazes outside: all but winter.</summary>
    public static bool Grazing(Season season) => season != Season.Winter;

    /// <summary>Whether meadow birds nest: spring and summer.</summary>
    public static bool Nesting(Season season) => season is Season.Spring or Season.Summer;

    /// <summary>Bales of hay a field gives when mown: long grass left for the birds the most.</summary>
    public static int Bales(Field field) => field switch
    {
        Field.Grass => 30,
        Field.Herbs => 20,
        _ => 40,
    };
}
