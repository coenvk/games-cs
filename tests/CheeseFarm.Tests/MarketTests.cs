using CheeseFarm;

namespace CheeseFarm.Tests;

public class MarketTests
{
    private static Farm WithWheels(int count)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(new Farm(1).ToJson())!.AsObject();
        json["wheels"] = new System.Text.Json.Nodes.JsonArray([.. Enumerable.Range(0, count).Select(i => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = i,
            ["madeOn"] = 0,
            ["flavour"] = "Plain",
            ["made"] = 1.0,
            ["turned"] = 0,
        })]);
        json["racks"] = 4;
        return Farm.FromJson(json.ToJsonString());
    }

    private static void ToMarket(Farm farm)
    {
        while (!Market.IsOn(farm.Day))
        {
            farm.NextDay();
        }
    }

    [Fact]
    public void TheMarket_IsMidweek_NotOnTheFairsDay()
    {
        Enumerable.Range(1, 21).Where(Market.IsOn).Should().Equal(3, 10, 17);
        Enumerable.Range(1, 21).Where(Market.IsOn).Should().NotIntersectWith(Enumerable.Range(1, 21).Where(Fair.IsOn));
    }

    [Fact]
    public void ACartOfWheels_SellsByThePiece_LessTheStall()
    {
        var farm = WithWheels(6);
        farm.CanGoToMarket.Should().BeFalse("not on day 1");
        ToMarket(farm);
        var money = farm.Money;

        var result = farm.GoToMarket(farm.Wheels)!;

        result.Sales.Should().HaveCount(Market.Cart, "the cart holds four");
        farm.Wheels.Should().HaveCount(2);
        farm.Money.Should().Be(money + result.Total);
        result.Total.Should().Be(result.Sales.Sum(s => s.Price) - Market.Fee);
        farm.CanGoToMarket.Should().BeFalse("once a market day");
        Farm.FromJson(farm.ToJson()).CanGoToMarket.Should().BeFalse("it is saved");
    }

    [Fact]
    public void ByThePiece_AWheelFetchesMoreThanWhole_OnAnOrdinaryDay()
    {
        var wheel = new Wheel(1, 0, Flavour.Plain, 1);
        var whole = new Customer("Anna", Ripeness.Matured, Flavour.Cumin, 1).Offer(wheel, 7);
        whole.Should().Be(0, "a customer wanting cumin won't take it");
        Market.Price(wheel, 7, crowd: 1).Should().BeGreaterThan(new Customer("Bram", Ripeness.Old, Flavour.Plain, 1).Offer(wheel, 7));
    }

    [Fact]
    public void TheDaysPeople_AreTheSameInEveryRun()
    {
        // Fixed numbers: a seed salted per process (as HashCode is) would change the farm's days across a reload.
        Customers.For(3, 10, 3, [Flavour.Plain]).Select(c => c.Name).Should().Equal("Daan", "Roos", "Tess");
        Market.Crowd(3, 10).Should().BeApproximately(0.8306f, 0.0001f);
    }

    [Fact]
    public void TheCrowd_IsTheSameForTheSameDay()
    {
        Market.Crowd(3, 10).Should().Be(Market.Crowd(3, 10)).And.BeInRange(0.8f, 1.2f);
    }
}
