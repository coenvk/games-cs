namespace CheeseFarm;

/// <summary>What a field of the meadow is kept as.</summary>
public enum Field
{
    /// <summary>Grazed grass: feeds the most cows.</summary>
    Grass,

    /// <summary>Sown with herbs and flowers: feeds fewer cows, but their milk makes richer cheese.</summary>
    Herbs,

    /// <summary>Left unmown for meadow birds to nest in: feeds no cows, but raises chicks and brings walkers.</summary>
    Birds,
}

/// <summary>
/// The meadow's numbers: how many cows a field feeds, what sowing herbs costs, what bought feed costs, how much
/// richer the herbs make the milk, and how often a bird field raises chicks.
/// </summary>
public static class Meadow
{
    public const int Fields = 6;

    /// <summary>Euros to sow a field with herbs and flowers.</summary>
    public const int SowCost = 80;

    /// <summary>Euros a day for each cow the meadow can't feed.</summary>
    public const int FeedPrice = 2;

    /// <summary>What the richest milk (every cow on herbs) adds to how well a wheel was made.</summary>
    public const float RichMilk = 0.06f;

    /// <summary>Days a bird field is left before its chicks fledge, and again every as many days after.</summary>
    public const int NestingDays = 7;

    /// <summary>Chicks a bird field raises each time.</summary>
    public const int Brood = 3;

    /// <summary>How many cows a field feeds.</summary>
    public static int Feeds(Field field) => field switch
    {
        Field.Grass => 3,
        Field.Herbs => 2,
        _ => 0,
    };

    /// <summary>Walkers out to see the birds who stop at the shop: one for the first bird field, one more for every two after.</summary>
    public static int Walkers(int birdFields) => (birdFields + 1) / 2;
}
