using CheeseFarm;

namespace CheeseFarm.Tests;

public class MeadowTests
{
    private static Farm Rich(int money = 1000)
    {
        var farm = Farm.FromJson(new Farm(1).ToJson().Replace("\"money\": 80", $"\"money\": {money}", StringComparison.Ordinal));
        return farm;
    }

    private static Making Middling(Farm farm)
    {
        var making = farm.StartMaking(Flavour.Plain)!;
        making.Warm(27);
        making.Set(making.SetsAfter);
        making.Cut([0, 0, 0, 0]);
        making.Brine(3);
        return making;
    }

    [Fact]
    public void ANewMeadow_IsAllGrass_AndFeedsTheHerd()
    {
        var farm = new Farm(1);
        farm.Fields.Should().HaveCount(Meadow.Fields).And.OnlyContain(f => f == Field.Grass);
        farm.Grazing.Should().BeGreaterThanOrEqualTo(farm.Cows);
        farm.NextDay().FeedBought.Should().Be(0);
    }

    [Fact]
    public void SowingHerbs_Costs_AndOtherFieldsAreFree()
    {
        var farm = Rich();
        farm.SetField(0, Field.Herbs).Should().BeTrue();
        farm.Money.Should().Be(1000 - Meadow.SowCost);
        farm.SetField(1, Field.Birds).Should().BeTrue();
        farm.SetField(1, Field.Grass).Should().BeTrue();
        farm.Money.Should().Be(1000 - Meadow.SowCost);
        farm.SetField(0, Field.Herbs).Should().BeFalse("it already is");
    }

    [Fact]
    public void HerbsWithoutMoney_CantBeSown() => Rich(Meadow.SowCost - 1).SetField(0, Field.Herbs).Should().BeFalse();

    [Fact]
    public void HerbMilk_MakesRicherCheese()
    {
        var plain = Rich();
        var herbs = Rich();
        herbs.SetField(0, Field.Herbs);
        herbs.SetField(1, Field.Herbs);
        herbs.Richness.Should().Be(1, "two herb fields feed the four cows");

        var made = plain.Finish(Middling(plain)).Made;
        herbs.Finish(Middling(herbs)).Made.Should().BeApproximately(made + Meadow.RichMilk, 0.001f);
    }

    [Fact]
    public void CowsTheMeadowCantFeed_EatBoughtFeed()
    {
        var farm = Rich();
        for (var i = 0; i < 5; i++)
        {
            farm.SetField(i, Field.Birds);
        }

        farm.Grazing.Should().Be(3);
        var report = farm.NextDay();
        report.FeedBought.Should().Be(1 * Meadow.FeedPrice);
        report.Hungry.Should().Be(0);
    }

    [Fact]
    public void WithoutMoneyForFeed_TheCowsGoHungry_AndGiveLess()
    {
        var farm = Rich(0);
        for (var i = 0; i < Meadow.Fields; i++)
        {
            farm.SetField(i, Field.Birds);
        }

        var report = farm.NextDay();
        report.Hungry.Should().Be(4);
        report.Milked.Should().Be(4 * Farm.MilkPerCow / 2);
    }

    [Fact]
    public void ABirdField_RaisesABrood_EveryNestingTime()
    {
        var farm = Rich();
        farm.SetField(0, Field.Birds);
        var fledged = Enumerable.Range(0, Meadow.NestingDays * 2).Sum(_ => farm.NextDay().Fledged);
        fledged.Should().Be(2 * Meadow.Brood);
        farm.Fledged.Should().Be(fledged);
    }

    [Fact]
    public void BirdFields_BringWalkersToTheShop()
    {
        var farm = Rich();
        var customers = farm.Customers.Count;
        farm.SetField(0, Field.Birds);
        farm.Customers.Should().HaveCount(customers + 1);
        farm.SetField(1, Field.Birds);
        farm.SetField(2, Field.Birds);
        farm.Customers.Should().HaveCount(customers + 2);
    }

    [Fact]
    public void TheMeadow_IsSaved()
    {
        var farm = Rich();
        farm.SetField(2, Field.Herbs);
        farm.SetField(5, Field.Birds);
        Enumerable.Range(0, Meadow.NestingDays).ToList().ForEach(_ => farm.NextDay());

        var back = Farm.FromJson(farm.ToJson());
        back.Fields.Should().Equal(farm.Fields);
        back.FieldSince(5).Should().Be(1);
        back.Fledged.Should().Be(Meadow.Brood);
    }
}
