namespace Paddock.Tests;

public class UpgradeTests
{
    private static CarInstance NewCar(string model = "m") => new(Guid.NewGuid(), model, 1, default);

    private static CarInstance Buy(CarInstance car, int engine, int weight, int chassis)
    {
        for (var i = 0; i < engine; i++)
        {
            car = Upgrades.Upgrade(car, Area.Engine);
        }

        for (var i = 0; i < weight; i++)
        {
            car = Upgrades.Upgrade(car, Area.Weight);
        }

        for (var i = 0; i < chassis; i++)
        {
            car = Upgrades.Upgrade(car, Area.Chassis);
        }

        return car;
    }

    [Fact]
    public void Stage_Allows_EightPoints_AtMostThreePerArea()
    {
        var car = Buy(NewCar(), 3, 3, 2);

        car.Tune.ToString().Should().Be("3.3.2");
        Upgrades.PointsInStage(car).Should().Be(8);
        Upgrades.CanUpgrade(Buy(NewCar(), 3, 0, 0), Area.Engine).Should().BeFalse();
        var fourth = () => Upgrades.Upgrade(Buy(NewCar(), 3, 0, 0), Area.Engine);
        fourth.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FullStage_NeedsFuse()
    {
        var car = Buy(NewCar(), 2, 3, 3);

        Upgrades.IsStageFull(car).Should().BeTrue();
        Enum.GetValues<Area>().Should().OnlyContain(a => !Upgrades.CanUpgrade(car, a));
    }

    [Fact]
    public void Fuse_SameClass_RaisesStage_KeepsTune()
    {
        var target = Buy(NewCar(), 3, 3, 2);
        var donor = NewCar();

        Upgrades.CanFuse(target, donor, Rarity.C, Rarity.C).Should().BeTrue();
        var fused = Upgrades.Fuse(target);

        fused.Stage.Should().Be(2);
        fused.Tune.Should().Be(target.Tune);
        Upgrades.PointsInStage(fused).Should().Be(0);
        Upgrades.CanUpgrade(fused, Area.Engine).Should().BeTrue();
    }

    [Fact]
    public void Fuse_RejectsSelf_OtherClass_UnfullStage_AndLastStage()
    {
        var full = Buy(NewCar(), 3, 3, 2);
        var donor = NewCar();

        Upgrades.CanFuse(full, full, Rarity.C, Rarity.C).Should().BeFalse();
        Upgrades.CanFuse(full, donor, Rarity.C, Rarity.D).Should().BeFalse();
        Upgrades.CanFuse(Buy(NewCar(), 3, 3, 1), donor, Rarity.C, Rarity.C).Should().BeFalse();
        var stage3Full = Buy(Upgrades.Fuse(Buy(Upgrades.Fuse(full), 3, 3, 2)), 3, 3, 2);
        Upgrades.CanFuse(stage3Full, donor, Rarity.C, Rarity.C).Should().BeFalse();
    }

    [Fact]
    public void FullyUpgraded_CanBe_NineNineSix()
    {
        var car = Buy(NewCar(), 3, 3, 2);
        car = Buy(Upgrades.Fuse(car), 3, 3, 2);
        car = Buy(Upgrades.Fuse(car), 3, 3, 2);

        car.Tune.ToString().Should().Be("9.9.6");
        car.Stage.Should().Be(3);
        Upgrades.IsStageFull(car).Should().BeTrue();
    }

    [Fact]
    public void Stage3_AllowsAnyMixUpToNinePerArea()
    {
        // Two stages as 3.3.2 give 6.6.4; stage 3 can then go all into weight and chassis.
        var car = Upgrades.Fuse(Buy(Upgrades.Fuse(Buy(NewCar(), 3, 3, 2)), 3, 3, 2));

        var mixed = Buy(car, 0, 3, 5);

        mixed.Tune.ToString().Should().Be("6.9.9");
        Upgrades.IsStageFull(mixed).Should().BeTrue();
        Upgrades.CanUpgrade(Buy(car, 0, 3, 0), Area.Weight).Should().BeFalse();
    }

    [Fact]
    public void Reset_KeepsStage_AndFreesThePoints()
    {
        var car = Upgrades.Fuse(Buy(NewCar(), 3, 3, 2));

        var reset = Upgrades.Reset(car);

        reset.Stage.Should().Be(2);
        reset.Tune.Should().Be(default(Tune));
        Upgrades.IsStageFull(reset).Should().BeFalse();
        Upgrades.IsStageFull(Buy(reset, 6, 6, 4)).Should().BeTrue();
    }

    [Fact]
    public void Apply_ImprovesStats()
    {
        var stats = TestCars.Hatch;

        var engine = Upgrades.Apply(stats, new Tune(1, 0, 0));
        var weight = Upgrades.Apply(stats, new Tune(0, 1, 0));
        var chassis = Upgrades.Apply(stats, new Tune(0, 0, 1));

        engine.TopSpeedKmh.Should().BeApproximately(170 * 1.012, 1e-9);
        engine.ZeroTo100.Should().BeApproximately(10 * 0.98, 1e-9);
        weight.WeightKg.Should().BeApproximately(950 * 0.985, 1e-9);
        weight.ZeroTo100.Should().BeApproximately(10 * 0.99, 1e-9);
        weight.Handling.Should().Be(60.5);
        chassis.Handling.Should().Be(61.5);
        Upgrades.Apply(stats with { Handling = 99 }, new Tune(0, 0, 3)).Handling.Should().Be(100);
    }

    [Fact]
    public void Cost_RisesWithinStage_AndWithStage()
    {
        var car = NewCar();
        var first = Upgrades.Cost(car, Area.Engine, Rarity.F);
        var second = Upgrades.Cost(Buy(car, 1, 0, 0), Area.Weight, Rarity.F);
        var stage2 = Upgrades.Cost(Upgrades.Fuse(Buy(car, 3, 3, 2)), Area.Engine, Rarity.F);

        first.Should().Be(300);
        second.Should().Be(375);
        stage2.Should().Be(600);
        Upgrades.Cost(car, Area.Engine, Rarity.S).Should().Be(12_000);
    }
}
