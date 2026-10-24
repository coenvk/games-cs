namespace Paddock.Tests;

public class CarsTests
{
    private const string TwoCars = """
        {
          "cars": [
            {
              "id": "vantelli-corvo", "name": "Corvo 1300 GL", "maker": "Vantelli", "country": "Italy", "year": 1972,
              "body": "Hatchback", "paint": "#e0a030", "trim": "#222222",
              "stats": { "topSpeedKmh": 152, "zeroTo100": 13.9, "handling": 61, "weightKg": 890,
                         "drive": "Fwd", "tyres": "Standard", "clearance": "Mid" }
            },
            {
              "id": "halvard-tundra", "name": "Tundra XR", "maker": "Halvard", "country": "Sweden", "year": 1994,
              "body": "Suv", "paint": "#3c7a4a", "trim": "#333333",
              "stats": { "topSpeedKmh": 178, "zeroTo100": 9.8, "handling": 55, "weightKg": 1740,
                         "drive": "Awd", "tyres": "OffRoad", "clearance": "High" }
            }
          ]
        }
        """;

    [Fact]
    public void FromJson_ReadsEveryField()
    {
        var roster = Roster.FromJson(TwoCars);

        roster.All.Should().HaveCount(2);
        var corvo = roster.Find("vantelli-corvo")!;
        corvo.Should().Be(new CarModel(
            "vantelli-corvo", "Corvo 1300 GL", "Vantelli", "Italy", 1972, Body.Hatchback,
            new Stats(152, 13.9, 61, 890, Drive.Fwd, Tyres.Standard, Clearance.Mid), "#e0a030", "#222222"));
        roster.Find("halvard-tundra")!.Stats.Tyres.Should().Be(Tyres.OffRoad);
    }

    [Fact]
    public void Find_UnknownId_IsNull() => Roster.FromJson(TwoCars).Find("nope").Should().BeNull();

    [Fact]
    public void FromJson_RejectsDuplicateIds()
    {
        var doubled = TwoCars.Replace("halvard-tundra", "vantelli-corvo", StringComparison.Ordinal);

        var load = () => Roster.FromJson(doubled);

        load.Should().Throw<FormatException>().WithMessage("*vantelli-corvo*");
    }

    [Fact]
    public void RqAndClass_ComeFromTheRating()
    {
        var roster = Roster.FromJson(TwoCars, stats => (int)stats.TopSpeedKmh / 4);

        roster.Rq("vantelli-corvo").Should().Be(38);
        roster.Class("vantelli-corvo").Should().Be(Rarity.D);
        roster.Class("halvard-tundra").Should().Be(Rarity.C);
    }
}
