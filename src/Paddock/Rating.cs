namespace Paddock;

/// <summary>A car's RQ and rarity class.</summary>
public static class Rating
{
    /// <summary>The rarity class for an RQ: F below 20, E 20-29, D 30-39, C 40-49, B 50-64, A 65-79, S 80 and up.</summary>
    public static Rarity ClassOf(int rq) => rq switch
    {
        < 20 => Rarity.F,
        < 30 => Rarity.E,
        < 40 => Rarity.D,
        < 50 => Rarity.C,
        < 65 => Rarity.B,
        < 80 => Rarity.A,
        _ => Rarity.S,
    };

    /// <summary>A car's RQ from its stats (worked out in Task 10 of the plan; a placeholder until then).</summary>
    public static int Rq(Stats stats) => throw new NotImplementedException();
}
