namespace CheeseFarm;

/// <summary>The stages of making a cheese, in order; each scored from 0 to 1.</summary>
public enum Stage
{
    /// <summary>Warm the milk into the band where the rennet works (30 to 33 °C, wider with a better kettle).</summary>
    Warm,

    /// <summary>Wait for the curd to set, and test for a clean break at the right moment.</summary>
    Set,

    /// <summary>Cut the curd with the harp as the knife crosses each mark: even cuts drain evenly.</summary>
    Cut,

    /// <summary>Choose how long the pressed wheel lies in the brine: too short is bland, too long salty.</summary>
    Brine,
}

/// <summary>
/// One cheese being made: the flavour chosen, then the stages' scores as they are done. <see cref="Made"/> weighs them
/// into how well the wheel was made; the warmth and the set count most, as they make or break the curd.
/// </summary>
public sealed class Making(Flavour flavour, bool goodKettle = false, int seed = 0)
{
    public const float LowWarmth = 30;
    public const float HighWarmth = 33;

    /// <summary>Days in the brine that suit a farm wheel best.</summary>
    public const int BestBrine = 3;

    /// <summary>Marks to cut along.</summary>
    public const int Cuts = 4;

    private static readonly float[] Weights = [0.3f, 0.3f, 0.2f, 0.2f];
    private readonly float?[] _scores = new float?[4];

    public Flavour Flavour { get; } = flavour;

    /// <summary>How big the cheese is made.</summary>
    public CheeseSize Size { get; init; } = CheeseSize.Wheel;

    /// <summary>A better kettle holds the milk steadier: a degree more room either side.</summary>
    public bool GoodKettle { get; } = goodKettle;

    /// <summary>Seconds after the rennet goes in that the curd gives a clean break (between 6 and 10).</summary>
    public float SetsAfter { get; } = 6 + ((float)new Random(seed).NextDouble() * 4);

    public Stage? Next => Enum.GetValues<Stage>().Cast<Stage?>().FirstOrDefault(s => _scores[(int)s!.Value] is null);

    public bool IsDone => Next is null;

    public float? Score(Stage stage) => _scores[(int)stage];

    /// <summary>How well the wheel was made, from the stages done (an undone one counts as nothing).</summary>
    public float Made => Weights.Select((weight, i) => weight * (_scores[i] ?? 0)).Sum();

    public float Warm(float temperature) => Record(Stage.Warm, WarmScore(temperature, GoodKettle));

    public float Set(float testedAt) => Record(Stage.Set, SetScore(testedAt, SetsAfter));

    /// <param name="misses">How far each cut fell from its mark, as a share of the vat's width.</param>
    public float Cut(IEnumerable<float> misses) => Record(Stage.Cut, CutScore(misses));

    public float Brine(int days) => Record(Stage.Brine, BrineScore(days));

    /// <summary>Full marks in the band, a fifth less for each degree outside it.</summary>
    public static float WarmScore(float temperature, bool goodKettle = false)
    {
        var room = goodKettle ? 1 : 0;
        var (low, high) = (LowWarmth - room, HighWarmth + room);
        var off = temperature < low ? low - temperature : temperature > high ? temperature - high : 0;
        return Math.Clamp(1 - (off * 0.2f), 0, 1);
    }

    /// <summary>Full marks within a second of the set; nothing four seconds either side.</summary>
    public static float SetScore(float testedAt, float setsAfter)
    {
        var off = MathF.Abs(testedAt - setsAfter);
        return Math.Clamp(1 - (MathF.Max(0, off - 1) / 3), 0, 1);
    }

    /// <summary>Full marks for cuts on their marks; a miss of a tenth of the vat or more scores nothing.</summary>
    public static float CutScore(IEnumerable<float> misses)
    {
        var list = misses?.ToList() ?? throw new ArgumentNullException(nameof(misses));
        return list.Count == 0 ? 0 : list.Average(m => Math.Clamp(1 - (MathF.Abs(m) * 10), 0, 1));
    }

    public static float BrineScore(int days) => Math.Clamp(1 - (Math.Abs(days - BestBrine) * 0.3f), 0, 1);

    private float Record(Stage stage, float score)
    {
        if (Next != stage)
        {
            throw new InvalidOperationException($"The next stage is {Next}, not {stage}.");
        }

        _scores[(int)stage] = score;
        return score;
    }
}
