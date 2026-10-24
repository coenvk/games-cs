namespace Paddock;

/// <summary>A car's RQ and rarity class.</summary>
public static class Rating
{
    // Four fixed tracks that together ask for everything: a launch and top speed, a fast circuit, a wet twisty
    // road and gravel. Defined here rather than in the data so RQs never move when the game's tracks change.
    private static readonly (TrackDef Track, Condition Condition)[] Reference =
    [
        (new TrackDef("rq-drag", "Drag", TrackKind.Drag, [new TrackPiece(Length: 402)], [Condition.Dry]), Condition.Dry),
        (new TrackDef("rq-circuit", "Circuit", TrackKind.Circuit,
        [
            new TrackPiece(Length: 400), new TrackPiece(Radius: 80, Degrees: 90), new TrackPiece(Length: 150),
            new TrackPiece(Radius: 40, Degrees: 90), new TrackPiece(Length: 400), new TrackPiece(Radius: 80, Degrees: 90),
            new TrackPiece(Length: 150), new TrackPiece(Radius: 40, Degrees: 90),
        ], [Condition.Dry]), Condition.Dry),
        (new TrackDef("rq-twisty", "Twisty", TrackKind.Twisty,
        [
            new TrackPiece(Length: 50), new TrackPiece(Radius: 25, Degrees: 90), new TrackPiece(Length: 40),
            new TrackPiece(Radius: 20, Degrees: -120), new TrackPiece(Length: 30), new TrackPiece(Radius: 35, Degrees: 140),
            new TrackPiece(Length: 60), new TrackPiece(Radius: 20, Degrees: -90), new TrackPiece(Length: 50),
        ], [Condition.Wet]), Condition.Wet),
        (new TrackDef("rq-gravel", "Gravel", TrackKind.OffRoad,
        [
            new TrackPiece(Length: 120), new TrackPiece(Radius: 40, Degrees: 80), new TrackPiece(Length: 100),
            new TrackPiece(Radius: 30, Degrees: -100), new TrackPiece(Length: 150), new TrackPiece(Radius: 50, Degrees: 60),
            new TrackPiece(Length: 100),
        ], [Condition.Gravel]), Condition.Gravel),
    ];

    // A middling car: everything is measured against it.
    private static readonly Stats Yardstick = new(200, 8.0, 60, 1300, Drive.Rwd, Tyres.Standard, Clearance.Mid);

    private static readonly double[] YardstickTimes = [.. Reference.Select(r => RaceSimulator.Run(Yardstick, r.Track, r.Condition).Time)];

    /// <summary>How quick a car is overall: the mean of the yardstick's time over the car's on the reference tracks (1 = as quick).</summary>
    public static double Score(Stats stats) =>
        Reference.Select((r, i) => YardstickTimes[i] / RaceSimulator.Run(stats, r.Track, r.Condition).Time).Average();

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

    /// <summary>
    /// A car's RQ from its stats: its <see cref="Score"/> read off a curve calibrated on the roster, so the classes
    /// spread as intended (about 20 F, 18 E, 16 D, 15 C, 12 B, 11 A and 8 S cars). 1 to 120.
    /// </summary>
    public static int Rq(Stats stats)
    {
        var score = Score(stats);
        var curve = Balance.RqCurve;
        if (score <= curve[0].Score)
        {
            return (int)curve[0].Rq;
        }

        for (var i = 1; i < curve.Length; i++)
        {
            if (score <= curve[i].Score)
            {
                var (s0, r0) = curve[i - 1];
                var (s1, r1) = curve[i];
                return Math.Clamp((int)Math.Round(r0 + ((r1 - r0) * (score - s0) / (s1 - s0)), MidpointRounding.AwayFromZero), 1, 120);
            }
        }

        var (sa, ra) = curve[^2];
        var (sb, rb) = curve[^1];
        return Math.Clamp((int)Math.Round(rb + ((rb - ra) * (score - sb) / (sb - sa)), MidpointRounding.AwayFromZero), 1, 120);
    }
}
