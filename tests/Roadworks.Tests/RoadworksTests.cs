namespace Roadworks.Tests;

public class RoadworksTests
{
    private static IReadOnlyList<TownLevel> Levels()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "Games.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return TownLevel.FromJson(File.ReadAllText(Path.Combine(folder, "apps", "Roadworks", "Assets", "roadworks", "levels.json")));
    }

    /// <summary>A straight road along a row or column, spot to spot.</summary>
    private static void Line(Plan plan, Spot from, Spot to)
    {
        var (dx, dy) = (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));
        for (var at = from; at != to; at = new Spot(at.X + dx, at.Y + dy))
        {
            plan.Lay(at, new Spot(at.X + dx, at.Y + dy)).Should().BeTrue($"{at} should be free");
        }
    }

    /// <summary>The first level's obvious answer: a crossroads in the middle.</summary>
    private static Plan Cross(TownLevel level, Junction junction)
    {
        var plan = new Plan(level);
        Line(plan, new Spot(0, 2), new Spot(6, 2));
        Line(plan, new Spot(3, 0), new Spot(3, 4));
        plan.Set(new Spot(3, 2), junction);
        return plan;
    }

    [Fact]
    public void TheLevels_ReadAndFit()
    {
        var levels = Levels();

        levels.Should().HaveCount(5);
        levels.Should().OnlyContain(l => l.Gates.All(g => l.Inside(g.At) && !l.IsBlocked(g.At) && (g.At.X == 0 || g.At.Y == 0 || g.At.X == l.Width || g.At.Y == l.Height)));
    }

    [Fact]
    public void Laying_OnlyJoinsNeighbours_AndNeverBlockedSpots()
    {
        var plan = new Plan(Levels()[0]);

        plan.Lay(new Spot(0, 0), new Spot(2, 0)).Should().BeFalse();
        plan.Lay(new Spot(0, 1), new Spot(1, 1)).Should().BeFalse("(1, 1) is blocked");
        plan.Lay(new Spot(0, 2), new Spot(1, 2)).Should().BeTrue();
        plan.Lay(new Spot(1, 2), new Spot(0, 2)).Should().BeFalse("it is there already");
        plan.Cost.Should().Be(Plan.StretchCost);
    }

    [Fact]
    public void Building_MakesRoadsBetweenJunctions_BendsInBetween()
    {
        var plan = Cross(Levels()[0], Junction.Lights);

        var (network, gates) = plan.Build();

        // The crossroads, and four roads out to the gates (each with its road out of town ending at an edge).
        network.Nodes.Count(n => n.Roads.Count == 4).Should().Be(1);
        network.Nodes.Single(n => n.Roads.Count == 4).Signals!.Actuated.Should().BeTrue();
        gates.Values.Should().OnlyContain(n => n.IsEdge);
        plan.Cost.Should().Be((10 * Plan.StretchCost) + Plan.LightsCost);
    }

    [Fact]
    public void ALoopBackToItsJunction_IsTwoRoads()
    {
        // From gate A to a junction, on to gate C, and a little block round from the junction back to it.
        var plan = new Plan(Levels()[0]);
        Line(plan, new Spot(0, 2), new Spot(3, 2));
        Line(plan, new Spot(3, 2), new Spot(3, 0));
        Line(plan, new Spot(3, 2), new Spot(4, 2));
        Line(plan, new Spot(4, 2), new Spot(4, 3));
        Line(plan, new Spot(4, 3), new Spot(3, 3));
        Line(plan, new Spot(3, 3), new Spot(3, 2));

        var (network, _) = plan.Build();

        network.Nodes.Single(n => n.Roads.Count == 4).Should().NotBeNull();
        network.Roads.Should().OnlyContain(r => r.From != r.To);
    }

    [Fact]
    public void AMissingRoad_IsReported()
    {
        var plan = new Plan(Levels()[0]);
        Line(plan, new Spot(0, 2), new Spot(6, 2));

        new Trial(plan).Unreachable.Should().NotBeNull();
        new Trial(plan).RunToEnd().Passed.Should().BeFalse();
    }

    [Fact]
    public void TheFirstLevel_CanBePassed_AndTheVerdictIsTheSameEachTime()
    {
        var level = Levels()[0];

        var verdict = new Trial(Cross(level, Junction.Lights)).RunToEnd();

        verdict.Unreachable.Should().BeNull();
        verdict.Passed.Should().BeTrue($"delay {verdict.AverageDelay:0.0} s, {verdict.Arrived} of {verdict.Expected} arrived");
        new Trial(Cross(level, Junction.Lights)).RunToEnd().Should().Be(verdict);
    }

    [Fact]
    public void APlan_SavesAndLoads()
    {
        var level = Levels()[0];
        var plan = Cross(level, Junction.Roundabout);

        var back = Plan.FromJson(level, plan.ToJson());

        back.Roads.Should().BeEquivalentTo(plan.Roads);
        back.JunctionAt(new Spot(3, 2)).Should().Be(Junction.Roundabout);
    }

    /// <summary>A way through every level, laid as straight lines: proof that each can be passed within its budget.</summary>
    private static Plan Answer(TownLevel level, int index)
    {
        var plan = new Plan(level);
        void L(int x1, int y1, int x2, int y2) => Line(plan, new Spot(x1, y1), new Spot(x2, y2));
        switch (index)
        {
            case 0:
                return Cross(level, Junction.Lights);
            case 1:
                L(0, 2, 8, 2);
                L(2, 0, 2, 2);
                L(6, 2, 6, 4);
                break;
            case 2:
                L(2, 1, 6, 1);
                L(6, 1, 6, 5);
                L(6, 5, 2, 5);
                L(2, 5, 2, 1);
                L(0, 3, 2, 3);
                L(6, 3, 8, 3);
                L(4, 0, 4, 1);
                L(4, 5, 4, 6);
                break;
            case 3:
                L(0, 1, 10, 1);
                L(0, 5, 1, 5);
                L(1, 5, 1, 4);
                L(1, 4, 10, 4);
                L(2, 1, 2, 4);
                L(8, 1, 8, 4);
                break;
            default:
                L(0, 2, 1, 2);
                L(1, 1, 1, 5);
                L(0, 5, 1, 5);
                L(1, 1, 10, 1);
                L(1, 4, 7, 4);
                L(7, 4, 7, 6);
                L(4, 0, 4, 1);
                L(4, 1, 4, 4);
                L(9, 1, 9, 4);
                L(9, 4, 10, 4);
                break;
        }

        foreach (var junction in plan.Junctions.ToList())
        {
            plan.Set(junction, plan.Degree(junction) == 4 ? Junction.Roundabout : Junction.Plain);
        }

        return plan;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryLevel_CanBePassed(int index)
    {
        var level = Levels()[index];
        var plan = Answer(level, index);

        var verdict = new Trial(plan).RunToEnd();

        plan.OverBudget.Should().BeFalse($"it costs {plan.Cost} of {level.Budget}");
        verdict.Passed.Should().BeTrue($"{level.Name}: delay {verdict.AverageDelay:0.0} s of {level.TargetDelay}, {verdict.Arrived} of {verdict.Expected} arrived, {verdict.Unreachable}");
    }
}
