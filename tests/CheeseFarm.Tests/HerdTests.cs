using CheeseFarm;

namespace CheeseFarm.Tests;

public class HerdTests
{
    [Fact]
    public void ANewFarm_HasFourNamedCows()
    {
        var farm = new Farm(1);
        farm.Herd.Select(c => c.Name).Should().Equal("Bella", "Klaartje", "Mientje", "Roosje");
        farm.Cows.Should().Be(4);
        farm.Herd.Should().OnlyContain(c => farm.IsGrown(c));
    }

    [Fact]
    public void ABoughtCow_GetsTheNextName_AndGivesMilkAtOnce()
    {
        var farm = Farm.FromJson(new Farm(1).ToJson().Replace("\"money\": 80", "\"money\": 1000", StringComparison.Ordinal));
        farm.Buy(Upgrade.Cow).Should().BeTrue();
        farm.Herd[^1].Name.Should().Be("Bertha");
        farm.Cows.Should().Be(5);
    }

    [Fact]
    public void Names_RunOut_IntoNumberedOnes()
    {
        var herd = Herd.Names.Select(n => new Cow(n, 0)).ToList();
        Herd.NameFor(herd).Should().Be("Bella 2");
    }

    [Fact]
    public void CalvesAreBorn_AndGrowIntoCowsThatGiveMilk()
    {
        // Long enough that some cow has a calf, and the calf grows up.
        var farm = new Farm(3);
        string? calf = null;
        var bornOn = 0;
        string? grown = null;
        for (var day = 0; day < 400 && grown is null; day++)
        {
            var report = farm.NextDay();
            if (calf is null && report.Calf is { } name)
            {
                (calf, bornOn) = (name, farm.Day);
                farm.Herd.Should().Contain(c => c.Name == name && !farm.IsGrown(c));
                farm.Cows.Should().Be(farm.Herd.Count - 1, "a calf gives no milk");
            }

            if (calf is not null && report.Grown == calf)
            {
                grown = calf;
                (farm.Day - bornOn).Should().Be(Herd.CalfDays);
            }
        }

        grown.Should().NotBeNull("a calf is born in time and grows up");
        farm.Herd.Count.Should().BeLessThanOrEqualTo(Farm.MaxCows);
    }

    [Fact]
    public void TheHerd_IsSaved_AndAnOldSaveGetsNames()
    {
        var farm = new Farm(5);
        for (var i = 0; i < 200; i++)
        {
            farm.NextDay();
        }

        Farm.FromJson(farm.ToJson()).Herd.Should().Equal(farm.Herd);

        var old = System.Text.Json.Nodes.JsonNode.Parse(new Farm(5).ToJson())!.AsObject();
        old.Remove("herd");
        old["cows"] = 6;
        var read = Farm.FromJson(old.ToJsonString());
        read.Herd.Should().HaveCount(6);
        read.Cows.Should().Be(6);
    }
}
