namespace CheeseFarm;

/// <summary>
/// A seed made of a few numbers that is the same every time the game runs: <see cref="HashCode.Combine{T1, T2}"/> is
/// salted afresh in every process, so a farm's customers, fair and market would change across a reload.
/// </summary>
internal static class Stable
{
    public static int Seed(params ReadOnlySpan<int> parts)
    {
        // FNV-1a over the numbers' bytes.
        var hash = 2166136261u;
        foreach (var part in parts)
        {
            for (var shift = 0; shift < 32; shift += 8)
            {
                hash = (hash ^ (byte)(part >> shift)) * 16777619u;
            }
        }

        return (int)hash;
    }
}
