using static Paddock.Tests.TestCars;

namespace Paddock.Tests;

public class RaceSimulatorTests
{
    [Fact]
    public void Drag_TopSpeedAndLaunchWin() =>
        Time(Muscle, Drag402, Condition.Dry).Should().BeLessThan(Time(Rally, Drag402, Condition.Dry));

    [Fact]
    public void Twisty_HandlingWins() =>
        Time(Rally, Twisty, Condition.Dry).Should().BeLessThan(Time(Muscle, Twisty, Condition.Dry));

    [Fact]
    public void Gravel_RightTyresBeatPower() =>
        Time(Rally, Circuit, Condition.Gravel).Should().BeLessThan(Time(Muscle, Circuit, Condition.Gravel));

    [Fact]
    public void Dry_PowerWinsTheFastCircuit() =>
        Time(Muscle, Circuit, Condition.Dry).Should().BeLessThan(Time(Hatch, Circuit, Condition.Dry));

    [Fact]
    public void QuarterMile_IsPlausible() => Time(Muscle, Drag402, Condition.Dry).Should().BeInRange(11, 15);

    [Fact]
    public void ZeroTo100_MatchesTheStat()
    {
        var straight = new TrackDef("km", "Kilometre", TrackKind.Drag, [new TrackPiece(Length: 1000)], [Condition.Dry]);

        foreach (var car in new[] { Muscle, Rally, Hatch })
        {
            RaceSimulator.TimeTo(car, straight, Condition.Dry, kmh: 100).Should().BeApproximately(car.ZeroTo100, 0.15);
        }
    }

    [Fact]
    public void Wet_IsSlowerThanDry() =>
        Time(Hatch, Twisty, Condition.Wet).Should().BeGreaterThan(Time(Hatch, Twisty, Condition.Dry));

    [Fact]
    public void SameInputs_SameBits() =>
        Run(Hatch, Circuit, Condition.Gravel).Time.Should().Be(Run(Hatch, Circuit, Condition.Gravel).Time);

    [Fact]
    public void DistanceAt_IsMonotonic_AndEndsAtLength()
    {
        var run = Run(Hatch, Twisty, Condition.Dry);
        var last = 0.0;

        for (var t = 0.0; t <= run.Time + 1; t += 0.01)
        {
            var d = run.DistanceAt(t);
            d.Should().BeGreaterThanOrEqualTo(last);
            last = d;
        }

        run.DistanceAt(0).Should().Be(0);
        run.DistanceAt(run.Time + 1).Should().Be(run.Length);
        run.Length.Should().BeApproximately(Twisty.Length, 1);
    }

    [Fact]
    public void Uphill_IsSlower()
    {
        var flat = new TrackDef("flat", "Flat", TrackKind.HillClimb, [new TrackPiece(Length: 800)], [Condition.Dry]);
        var hill = new TrackDef("hill", "Hill", TrackKind.HillClimb, [new TrackPiece(Length: 800, Gradient: 0.08)], [Condition.Dry]);

        Time(Hatch, hill, Condition.Dry).Should().BeGreaterThan(Time(Hatch, flat, Condition.Dry));
    }

    [Fact]
    public void LowClearance_SlowsOnRoughSurface()
    {
        var low = Rally with { Clearance = Clearance.Low };

        Time(low, Circuit, Condition.Gravel).Should().BeGreaterThan(Time(Rally, Circuit, Condition.Gravel));
        Time(low, Circuit, Condition.Dry).Should().Be(Time(Rally, Circuit, Condition.Dry));
    }

    [Fact]
    public void TrackPieces_BecomeAPath()
    {
        var path = Circuit.ToPath();

        path.IsClosed.Should().BeTrue();
        path.Length.Should().BeApproximately(Circuit.Length, 1e-6);
    }
}
