namespace CheeseFarm;

/// <summary>A cheese at the fair: whose, and the judges' score out of 100.</summary>
public sealed record FairEntry(string Farm, int Score);

/// <summary>How a farm's cheese did at the fair: its place (1 is best), the prize, and every entry in order.</summary>
public sealed record FairResult(int Place, int Prize, IReadOnlyList<FairEntry> Entries);

/// <summary>
/// The cheese fair in town, every <see cref="Every"/> days: one wheel from the cellar is judged against three other
/// farms'. The judges score a wheel on its stars (most of it) and how long it ripened (an old cheese has more to say);
/// the rivals are the same for the same farm and day, and grow stronger as the weeks go by. The best three win prizes
/// and a rosette.
/// </summary>
public static class Fair
{
    public const int Every = 7;

    private static readonly string[] Rivals = ["De Eikenhoeve", "Hoeve Brinkman", "Kaasboerderij Ter Horst", "De Lindehof", "Hoeve 't Veld", "Boerderij Wolters"];
    private static readonly int[] Prizes = [150, 80, 40];

    /// <summary>Whether the fair is on today.</summary>
    public static bool IsOn(int day) => day > 0 && day % Every == 0;

    /// <summary>The judges' score for a wheel: up to 80 for its stars, up to 20 for its ripeness.</summary>
    public static int Score(Wheel wheel, int day)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        return (wheel.Stars * 16) + ((int)wheel.Ripeness(day) * 4);
    }

    /// <summary>The other farms' entries on a fair day: stronger each fair, never perfect.</summary>
    public static IReadOnlyList<FairEntry> Others(int seed, int day)
    {
        var random = new Random(HashCode.Combine(seed, day, 4243));
        var week = day / Every;
        var names = Rivals.OrderBy(_ => random.Next()).Take(3).ToList();
        return [.. names.Select(name => new FairEntry(name, Math.Clamp(40 + (week * 4) + random.Next(-12, 16), 20, 96)))];
    }

    /// <summary>Judges a wheel against the others: its place, prize, and the entries best first (ties go to the visitor).</summary>
    public static FairResult Judge(string farm, Wheel wheel, int seed, int day)
    {
        var mine = new FairEntry(farm, Score(wheel, day));
        var entries = Others(seed, day).Append(mine).OrderByDescending(e => e.Score).ThenBy(e => e == mine ? 0 : 1).ToList();
        var place = entries.IndexOf(mine) + 1;
        return new FairResult(place, place <= Prizes.Length ? Prizes[place - 1] : 0, entries);
    }
}
