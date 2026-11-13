namespace CheeseFarm;

/// <summary>A cow's two front teats, squeezed in turn when milking by hand.</summary>
public enum Teat
{
    Left,
    Right,
}

/// <summary>
/// Milking a cow by hand, for a little extra milk each morning: squeeze the teats in turn, left, right, left, in a
/// steady rhythm. Each good squeeze puts milk in the pail; the same teat twice, or squeezing in a hurry, makes the cow
/// restless, and a cow restless enough kicks the pail over, spilling half. It ends when the time is up, the pail is
/// full or the pail is kicked.
/// </summary>
public sealed class Milking
{
    /// <summary>Seconds of milking.</summary>
    public const float Length = 20;

    /// <summary>Litres the pail holds.</summary>
    public const float Pail = 20;

    /// <summary>Litres a good squeeze gives.</summary>
    public const float PerSqueeze = 0.3f;

    /// <summary>Seconds between squeezes quicker than this hurry the cow.</summary>
    public const float Hurried = 0.18f;

    private Teat? _last;
    private float _lastAt = float.NegativeInfinity;

    /// <summary>Seconds milked so far.</summary>
    public float Time { get; private set; }

    public float Litres { get; private set; }

    /// <summary>How restless the cow is, 0 calm to 1, when she kicks.</summary>
    public float Restless { get; private set; }

    public bool Kicked { get; private set; }

    public int Good { get; private set; }

    public int Bad { get; private set; }

    public bool IsDone => Time >= Length || Litres >= Pail || Kicked;

    /// <summary>Time passing: the cow calms a little.</summary>
    public void Advance(float seconds)
    {
        if (IsDone)
        {
            return;
        }

        Time = MathF.Min(Length, Time + seconds);
        Restless = MathF.Max(0, Restless - (0.04f * seconds));
    }

    /// <summary>Squeezes a teat: true when it was a good squeeze that gave milk.</summary>
    public bool Squeeze(Teat teat)
    {
        if (IsDone)
        {
            return false;
        }

        var sameTeat = teat == _last;
        var good = !sameTeat && Time - _lastAt >= Hurried;
        (_last, _lastAt) = (teat, Time);
        if (good)
        {
            Good++;
            Litres = MathF.Min(Pail, Litres + PerSqueeze);
            Restless = MathF.Max(0, Restless - 0.01f);
            return true;
        }

        Bad++;
        Restless += sameTeat ? 0.12f : 0.08f;
        if (Restless >= 1)
        {
            (Restless, Kicked) = (1, true);
            Litres /= 2;
        }

        return false;
    }
}
