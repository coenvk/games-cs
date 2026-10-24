namespace Paddock.Tests;

/// <summary>Cars and tracks with known characters, for testing the rules independently of the game's data.</summary>
internal static class TestCars
{
    public static readonly Stats Muscle = new(260, 5.0, 45, 1600, Drive.Rwd, Tyres.Performance, Clearance.Low);
    public static readonly Stats Rally = new(200, 5.5, 75, 1300, Drive.Awd, Tyres.AllSurface, Clearance.High);
    public static readonly Stats Hatch = new(170, 10.0, 60, 950, Drive.Fwd, Tyres.Standard, Clearance.Mid);

    public static readonly TrackDef Drag402 = new("drag", "Quarter mile", TrackKind.Drag, [new TrackPiece(Length: 402)], [Condition.Dry]);

    public static readonly TrackDef Twisty = new("twisty", "Twisty", TrackKind.Twisty,
    [
        new TrackPiece(Length: 40), new TrackPiece(Radius: 25, Degrees: 90), new TrackPiece(Length: 30),
        new TrackPiece(Radius: 20, Degrees: -120), new TrackPiece(Length: 25), new TrackPiece(Radius: 30, Degrees: 150),
        new TrackPiece(Length: 30), new TrackPiece(Radius: 20, Degrees: -90), new TrackPiece(Length: 40),
        new TrackPiece(Radius: 25, Degrees: 100), new TrackPiece(Length: 35),
    ], [Condition.Dry, Condition.Wet]);

    public static readonly TrackDef Circuit = new("circuit", "Circuit", TrackKind.Circuit,
    [
        new TrackPiece(Length: 300), new TrackPiece(Radius: 60, Degrees: 90), new TrackPiece(Length: 150),
        new TrackPiece(Radius: 40, Degrees: 90), new TrackPiece(Length: 300), new TrackPiece(Radius: 60, Degrees: 90),
        new TrackPiece(Length: 150), new TrackPiece(Radius: 40, Degrees: 90),
    ], [Condition.Dry, Condition.Gravel]);

    public static RaceRun Run(Stats car, TrackDef track, Condition condition) => RaceSimulator.Run(car, track, condition);

    public static double Time(Stats car, TrackDef track, Condition condition) => Run(car, track, condition).Time;
}
