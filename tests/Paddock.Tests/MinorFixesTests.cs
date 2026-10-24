namespace Paddock.Tests;

public class MinorFixesTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));
    private static readonly IReadOnlyList<EventDef> Templates = EventDef.ListFromJson(GameData.Read("events.json"));
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1e-9)]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2.718281828459045)]
    [InlineData(1234.5)]
    public void Log_MatchesTheLibrary_ToAboutTheLastBit(double x) =>
        Arith.Log(x).Should().BeApproximately(Math.Log(x), Math.Max(1e-15, Math.Abs(Math.Log(x)) * 1e-15));

    [Fact]
    public void Log_OfNothing_IsNegativeInfinity_AndOfNegative_IsNaN()
    {
        Arith.Log(0).Should().Be(double.NegativeInfinity);
        double.IsNaN(Arith.Log(-1)).Should().BeTrue();
    }

    [Fact]
    public void EndedEvent_CannotBeEntered()
    {
        var ev = new LiveEvent(Templates.Single(t => t.Id == "budget-battle"), "budget@old", Now.AddMinutes(-1));
        var profile = Profile.New(Cars);

        EventPlay.CanEnter(profile, ev, Cars, Now, out var why).Should().BeFalse();
        why.Should().Be("Event ended");
    }

    [Fact]
    public void Prune_ForgetsEventsNoLongerRunning()
    {
        var profile = Profile.New(Cars);
        var live = new EventCalendar(Templates).Live(Now);
        profile.Events["old@0.1"] = new EventProgress(3, new Jentl.Collect.RechargeState(5, Now), false);
        profile.Events[live[0].InstanceId] = new EventProgress(1, new Jentl.Collect.RechargeState(5, Now), false);

        EventPlay.Prune(profile, live);

        profile.Events.Keys.Should().Equal(live[0].InstanceId);
    }

    [Fact]
    public void Campaign_CanEnter_NeedsFiveCarsWithinTheLimit()
    {
        var regions = RegionDef.ListFromJson(GameData.Read("campaign.json"));
        var first = regions[0].Stages[0];
        var profile = Profile.New(Cars);

        CampaignPlay.CanEnter(profile, regions, first, Cars, out var why).Should().BeTrue();
        why.Should().BeNull();

        profile.Garage = [.. profile.Garage.Take(4)];
        CampaignPlay.CanEnter(profile, regions, first, Cars, out why).Should().BeFalse();
        why.Should().Be("Not enough cars that may enter");

        CampaignPlay.CanEnter(Profile.New(Cars), regions, regions[0].Stages[1], Cars, out why).Should().BeFalse();
        why.Should().Be("Win the stage before this one first.");
    }

    [Fact]
    public void Opponents_LimitTooLowForFiveCars_Throws()
    {
        var pick = () => Opponents.Pick(Cars, _ => true, rqLimit: 20, strength: 1, upgrades: 0, new Random(1));

        pick.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Roster_NoTwoCarsAreNearCopies()
    {
        static bool Near(double a, double b) => Math.Abs(a - b) <= 0.06 * Math.Max(a, b);
        var copies = from a in Cars.All
                     from b in Cars.All
                     where string.CompareOrdinal(a.Id, b.Id) < 0
                         && a.Body == b.Body && a.Stats.Drive == b.Stats.Drive && a.Stats.Tyres == b.Stats.Tyres && a.Stats.Clearance == b.Stats.Clearance
                         && Near(a.Stats.TopSpeedKmh, b.Stats.TopSpeedKmh) && Near(a.Stats.ZeroTo100, b.Stats.ZeroTo100)
                         && Near(a.Stats.Handling, b.Stats.Handling) && Near(a.Stats.WeightKg, b.Stats.WeightKg)
                     select $"{a.Id} ~ {b.Id}";

        copies.Should().BeEmpty();
    }
}
