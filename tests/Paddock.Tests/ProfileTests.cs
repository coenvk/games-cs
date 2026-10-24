namespace Paddock.Tests;

public class ProfileTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));

    [Fact]
    public void New_HasCash_AndFiveCarsThatFitLevelOne()
    {
        var profile = Profile.New(Cars);

        profile.Cash.Should().Be(5_000);
        profile.Level.Should().Be(1);
        profile.Garage.Should().HaveCount(8);
        profile.Garage.Should().OnlyContain(c => Cars.Class(c.ModelId) <= Rarity.E);
        var cheapest = profile.Garage.OrderBy(c => Cars.Rq(c.ModelId)).Take(5).ToList();
        Hands.Fits(cheapest, Cars, Progression.RqLimit(1)).Should().BeTrue();
    }

    [Fact]
    public void RqLimit_GrowsAndCaps()
    {
        Progression.RqLimit(1).Should().Be(100);
        Progression.RqLimit(2).Should().Be(120);
        Progression.RqLimit(100).Should().Be(500);
    }

    [Fact]
    public void AddXp_LevelsUp()
    {
        var profile = Profile.New(Cars);

        Progression.AddXp(profile, 49);
        profile.Level.Should().Be(1);
        Progression.AddXp(profile, 1);
        profile.Level.Should().Be(2);
        profile.Xp.Should().Be(0);
        Progression.AddXp(profile, 100 + 150);
        profile.Level.Should().Be(4);
    }

    [Fact]
    public void Payout_WinPaysMoreThanLoss()
    {
        var win = Progression.Payout(won: true, opponentAverageRq: 20);
        var loss = Progression.Payout(won: false, opponentAverageRq: 20);

        win.Should().Be((300L, 10));
        loss.Should().Be((100L, 4));
    }

    [Fact]
    public void Buy_WithoutCash_Fails_NothingChanges()
    {
        var profile = Profile.New(Cars);
        profile.Cash = 1_999;

        Packs.Buy(profile, Packs.Starter, Cars, new Random(1), out var cars).Should().BeFalse();

        cars.Should().BeEmpty();
        profile.Cash.Should().Be(1_999);
        profile.Garage.Should().HaveCount(8);
    }

    [Fact]
    public void Buy_PaysAndAddsThreeCars()
    {
        var profile = Profile.New(Cars);

        Packs.Buy(profile, Packs.Starter, Cars, new Random(1), out var cars).Should().BeTrue();

        profile.Cash.Should().Be(3_000);
        cars.Should().HaveCount(3);
        profile.Garage.Should().HaveCount(11).And.Contain(cars);
    }

    [Fact]
    public void Open_FollowsWeights()
    {
        var random = new Random(5);
        var classes = Enumerable.Range(0, 3_000).SelectMany(_ => Packs.Open(Packs.Starter, Cars, random)).Select(c => Cars.Class(c.ModelId)).ToList();

        classes.Should().OnlyContain(c => c == Rarity.F || c == Rarity.E || c == Rarity.D);
        classes.Count(c => c == Rarity.F).Should().BeInRange(5_000, 5_800); // 60% of 9 000
    }

    [Fact]
    public void Workshop_UpgradeChargesCost()
    {
        var profile = Profile.New(Cars);
        var car = profile.Garage[0];
        var cost = Upgrades.Cost(car, Area.Engine, Cars.Class(car.ModelId));

        Workshop.Upgrade(profile, car.Id, Area.Engine, Cars).Should().BeTrue();

        profile.Cash.Should().Be(5_000 - cost);
        profile.Garage[0].Tune.Engine.Should().Be(1);
    }

    [Fact]
    public void Workshop_UpgradeWithoutCash_Fails()
    {
        var profile = Profile.New(Cars);
        profile.Cash = 0;

        Workshop.Upgrade(profile, profile.Garage[0].Id, Area.Engine, Cars).Should().BeFalse();
        profile.Garage[0].Tune.Should().Be(default(Tune));
    }

    [Fact]
    public void Workshop_FuseConsumesDonor_AndForgetsItInTheHand()
    {
        var profile = Profile.New(Cars);
        var model = profile.Garage[0].ModelId;
        var target = CarInstance.New(model) with { Tune = new Tune(3, 3, 2) };
        var donor = CarInstance.New(model);
        profile.Garage.AddRange([target, donor]);
        profile.LastHand = [donor.Id];

        Workshop.Fuse(profile, target.Id, donor.Id, Cars).Should().BeTrue();

        profile.Garage.Should().NotContain(c => c.Id == donor.Id);
        profile.Garage.Single(c => c.Id == target.Id).Stage.Should().Be(2);
        profile.LastHand.Should().BeEmpty();
    }

    [Fact]
    public void Workshop_FuseIntoItself_Fails_NothingConsumed()
    {
        var profile = Profile.New(Cars);
        var target = CarInstance.New(profile.Garage[0].ModelId) with { Tune = new Tune(3, 3, 2) };
        profile.Garage.Add(target);

        Workshop.Fuse(profile, target.Id, target.Id, Cars).Should().BeFalse();

        profile.Garage.Should().Contain(target);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var profile = Profile.New(Cars);
        profile.Cash = 1234;
        Progression.AddXp(profile, 60);
        profile.LastHand = [profile.Garage[1].Id];

        var back = Profile.FromJson(profile.ToJson(), Cars);

        back.Cash.Should().Be(1234);
        back.Level.Should().Be(2);
        back.Xp.Should().Be(10);
        back.Garage.Should().Equal(profile.Garage);
        back.LastHand.Should().Equal(profile.LastHand);
    }

    [Fact]
    public void FromJson_DropsUnknownCars_KeepsRest()
    {
        var profile = Profile.New(Cars);
        var gone = CarInstance.New("no-such-car");
        profile.Garage.Add(gone);
        profile.LastHand = [gone.Id, profile.Garage[0].Id];

        var back = Profile.FromJson(profile.ToJson(), Cars);

        back.Garage.Should().HaveCount(8);
        back.LastHand.Should().Equal(profile.Garage[0].Id);
    }

    [Fact]
    public void FromJson_OldSave_GetsDefaults()
    {
        var back = Profile.FromJson("""{ "cash": 42 }""", Cars);

        back.Cash.Should().Be(42);
        back.Level.Should().Be(1);
        back.Garage.Should().BeEmpty();
        back.LastHand.Should().BeEmpty();
    }
}

public class CorruptSaveTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{ "cash": "lots" }""")]
    public void FromJson_Corrupt_ThrowsInvalidData(string json)
    {
        var load = () => Profile.FromJson(json, Cars);

        load.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FromJson_CarWithoutModel_IsDropped_AndStagesAreClamped()
    {
        var id = Cars.All[0].Id;
        var json = $$"""{ "garage": [ { "id": "{{Guid.NewGuid()}}", "modelId": null, "stage": 1 }, { "id": "{{Guid.NewGuid()}}", "modelId": "{{id}}" }, { "id": "{{Guid.NewGuid()}}", "modelId": "{{id}}", "stage": 9 } ] }""";

        var profile = Profile.FromJson(json, Cars);

        profile.Garage.Should().HaveCount(2);
        profile.Garage.Select(c => c.Stage).Should().Equal(1, 3);
    }
}
