using CheeseFarm;

namespace CheeseFarm.Tests;

public class SizeTests
{
    private static Farm WithMilk(int litres) =>
        Farm.FromJson(new Farm(1).ToJson().Replace("\"milk\": 100", $"\"milk\": {litres}", StringComparison.Ordinal));

    private static Making Made(Farm farm, CheeseSize size)
    {
        var making = farm.StartMaking(Flavour.Plain, size)!;
        making.Warm(31);
        making.Set(making.SetsAfter);
        making.Cut([0, 0, 0, 0]);
        making.Brine(3);
        return making;
    }

    [Theory]
    [InlineData(CheeseSize.Baby, 50)]
    [InlineData(CheeseSize.Wheel, 100)]
    [InlineData(CheeseSize.Big, 200)]
    public void ACheese_TakesTheMilkOfItsSize(CheeseSize size, int litres)
    {
        var farm = WithMilk(300);
        var wheel = farm.Finish(Made(farm, size));
        wheel.Size.Should().Be(size);
        farm.Milk.Should().Be(300 - litres);
    }

    [Fact]
    public void WithoutTheMilk_ABigWheelCantBeStarted()
    {
        var farm = WithMilk(150);
        farm.StartMaking(Flavour.Plain, CheeseSize.Big).Should().BeNull();
        farm.CanMakeSize(CheeseSize.Wheel).Should().BeTrue();
        WithMilk(60).CanMake.Should().BeTrue("a baby cheese takes fifty");
    }

    [Fact]
    public void ABabyCheese_RipensTwiceAsFast_ABigWheelHalf()
    {
        var baby = new Wheel(1, 0, Flavour.Plain, 1) { Size = CheeseSize.Baby };
        var big = new Wheel(2, 0, Flavour.Plain, 1) { Size = CheeseSize.Big };
        var wheel = new Wheel(3, 0, Flavour.Plain, 1);

        baby.Ripeness(3).Should().Be(Ripening.At(6));
        wheel.Ripeness(6).Should().Be(Ripening.At(6));
        big.Ripeness(12).Should().Be(Ripening.At(6));
        big.DaysTo(Ripeness.Matured, 0).Should().Be(12);
        baby.DaysTo(Ripeness.Matured, 0).Should().Be(3);
        wheel.DaysTo(Ripeness.Young, 4).Should().Be(0);
    }

    [Fact]
    public void ABigWheel_FetchesMore_ABabyCheeseLess()
    {
        var customer = new Customer("Anna", Ripeness.Matured, Flavour.Plain, 1);
        var wheel = customer.Offer(new Wheel(1, 0, Flavour.Plain, 1), 6);
        customer.Offer(new Wheel(2, 0, Flavour.Plain, 1) { Size = CheeseSize.Big }, 12).Should().BeGreaterThan(wheel * 2);
        customer.Offer(new Wheel(3, 0, Flavour.Plain, 1) { Size = CheeseSize.Baby }, 3).Should().BeLessThan(wheel);
    }

    [Fact]
    public void TheSize_IsSaved()
    {
        var farm = WithMilk(300);
        farm.Finish(Made(farm, CheeseSize.Big));
        farm.Finish(Made(farm, CheeseSize.Baby));
        Farm.FromJson(farm.ToJson()).Wheels.Select(w => w.Size).Should().Equal(CheeseSize.Big, CheeseSize.Baby);
    }
}
