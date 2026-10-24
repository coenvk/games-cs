namespace Paddock.Tests;

public class RosterTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));

    [Fact]
    public void Roster_HasHundredUniqueCars()
    {
        Cars.All.Should().HaveCount(100);
        Cars.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        Cars.All.Select(c => $"{c.Maker} {c.Name}").Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(Rarity.F, 20)]
    [InlineData(Rarity.E, 18)]
    [InlineData(Rarity.D, 16)]
    [InlineData(Rarity.C, 15)]
    [InlineData(Rarity.B, 12)]
    [InlineData(Rarity.A, 11)]
    [InlineData(Rarity.S, 8)]
    public void Roster_SpreadsOverClasses(Rarity rarity, int target) =>
        Cars.All.Count(c => Cars.Class(c.Id) == rarity).Should().BeInRange(target - 3, target + 3);

    [Fact]
    public void Roster_UsesEveryBody() =>
        Cars.All.Select(c => c.Body).Distinct().Should().BeEquivalentTo(Enum.GetValues<Body>());

    [Fact]
    public void Rq_RisesWithScore()
    {
        var byScore = Cars.All.OrderBy(c => Rating.Score(c.Stats)).Select(c => Cars.Rq(c.Id)).ToList();

        byScore.Should().BeInAscendingOrder();
        byScore.Should().OnlyContain(rq => rq >= 1 && rq <= 120);
    }

    [Fact]
    public void Rq_IgnoresUpgrades()
    {
        var model = Cars.All[40];
        var before = Cars.Rq(model.Id);
        var car = CarInstance.New(model.Id) with { Stage = 3, Tune = new Tune(9, 9, 6) };

        Matches.StatsOf(car, Cars).Should().NotBe(model.Stats);
        Cars.Rq(car.ModelId).Should().Be(before);
    }

    [Fact]
    public void Rq_IsStable()
    {
        // A snapshot: a change here means every saved hand's RQ total moved. Change it on purpose only.
        string[] ids = ["vantelli-corvo-1300", "brannock-drifter-v8", "nakamori-tsubasa-r", "kestrel-gyrfalcon", "calder-apex-1000"];

        ids.Select(Cars.Rq).Should().Equal(Snapshot);
    }

    private static readonly int[] Snapshot = [19, 21, 53, 70, 82];

    [Fact]
    public void Roster_NoRealMakers()
    {
        string[] real =
        [
            "ferrari", "porsche", "bmw", "ford", "toyota", "nissan", "honda", "mazda", "subaru", "audi", "mercedes",
            "volkswagen", "volvo", "saab", "fiat", "alfa", "lancia", "lamborghini", "maserati", "bugatti", "mclaren",
            "aston", "jaguar", "lotus", "renault", "peugeot", "citroen", "chevrolet", "dodge", "skoda", "seat",
            "mitsubishi", "lexus", "tesla", "koenigsegg", "pagani", "mustang", "corvette", "camaro", "viper", "skyline",
        ];

        // Whole words: "Ashford" is a town, not a Ford.
        var offenders = Cars.All.Where(c => $"{c.Maker} {c.Name}".ToLowerInvariant().Split([' ', '-']).Any(real.Contains));
        offenders.Should().BeEmpty();
    }
}
