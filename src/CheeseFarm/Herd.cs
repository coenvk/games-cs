namespace CheeseFarm;

/// <summary>A cow of the herd: her name, and the day she was born (before the farm's first day for those it started with).</summary>
public sealed record Cow(string Name, int Born);

/// <summary>
/// The herd's numbers and names: how long a calf takes to grow into a cow that gives milk, how likely a cow is to have
/// a calf on a night, and the names the farm gives its cows.
/// </summary>
public static class Herd
{
    /// <summary>Days from a calf's birth until she gives milk.</summary>
    public const int CalfDays = 28;

    /// <summary>The chance, each night, that a cow has a calf: about one every couple of months.</summary>
    public const double CalvingChance = 1 / 60.0;

    /// <summary>Names as cows on a Dutch farm have them, given in this order.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Bella", "Klaartje", "Mientje", "Roosje", "Bertha", "Gerda", "Truus", "Anneke", "Jet", "Saartje", "Hennie",
        "Doortje", "Fenna", "Lies", "Marijke", "Dikkie", "Grietje", "Sientje", "Toos", "Wilma", "Aaltje", "Betje",
    ];

    /// <summary>The first name not yet in the herd; once all are taken, the names again with a number.</summary>
    public static string NameFor(IEnumerable<Cow> herd)
    {
        ArgumentNullException.ThrowIfNull(herd);
        var taken = herd.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        for (var round = 1; ; round++)
        {
            foreach (var name in Names)
            {
                var candidate = round == 1 ? name : $"{name} {round}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
