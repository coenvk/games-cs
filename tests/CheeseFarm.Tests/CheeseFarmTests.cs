using CheeseFarm;

namespace CheeseFarm.Tests;

public class RipeningTests
{
    [Theory]
    [InlineData(0, Ripeness.Young)]
    [InlineData(2, Ripeness.Young)]
    [InlineData(3, Ripeness.YoungMatured)]
    [InlineData(6, Ripeness.Matured)]
    [InlineData(10, Ripeness.ExtraMatured)]
    [InlineData(15, Ripeness.Old)]
    [InlineData(40, Ripeness.VeryOld)]
    public void ACheese_RipensWithTheDays(int age, Ripeness expected) => Ripening.At(age).Should().Be(expected);

    [Fact]
    public void OlderCheese_FetchesMore()
    {
        var prices = Enum.GetValues<Ripeness>().Select(Ripening.Price).ToList();
        prices.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }
}

public class WheelTests
{
    [Fact]
    public void AWheel_WantsTurningEverySecondDay()
    {
        var wheel = new Wheel(1, madeOn: 5, Flavour.Plain, 1);
        wheel.NeedsTurning(6).Should().BeFalse();
        wheel.NeedsTurning(7).Should().BeTrue();

        wheel.Turn(7);
        wheel.NeedsTurning(8).Should().BeFalse();
    }

    [Fact]
    public void Neglect_CostsStars()
    {
        var wheel = new Wheel(1, 1, Flavour.Plain, 1);
        wheel.Stars.Should().Be(5);

        wheel.Neglect = 5;
        wheel.Stars.Should().BeLessThan(5);
        wheel.Quality.Should().BeApproximately(0.8f + (0.2f * 0.4f), 0.001f);
    }

    [Fact]
    public void ABadlyWarmedMilk_SpoilsTheWheel()
    {
        // Cold milk, a good set, ragged cuts, two days in the brine: half made, two stars however well it is kept.
        var making = new Making(Flavour.Plain, seed: 1);
        making.Warm(18);
        making.Set(making.SetsAfter);
        making.Cut([0.08f, 0.08f, 0.09f, 0.09f]);
        making.Brine(2);

        new Wheel(1, 1, Flavour.Plain, making.Made).Stars.Should().Be(2);
    }

    [Fact]
    public void ADisaster_StillMakesOneStar() => new Wheel(1, 1, Flavour.Plain, 0) { Neglect = 99 }.Stars.Should().Be(1);
}

public class MakingTests
{
    [Theory]
    [InlineData(31.5f, 1f)]
    [InlineData(30f, 1f)]
    [InlineData(28f, 0.6f)]
    [InlineData(35f, 0.6f)]
    [InlineData(40f, 0f)]
    public void Warming_ScoresTheBand(float temperature, float score) =>
        Making.WarmScore(temperature).Should().BeApproximately(score, 0.001f);

    [Fact]
    public void AGoodKettle_WidensTheBand()
    {
        Making.WarmScore(29).Should().BeLessThan(1);
        Making.WarmScore(29, goodKettle: true).Should().Be(1);
    }

    [Fact]
    public void TheSet_ForgivesASecondEitherSide()
    {
        Making.SetScore(8.9f, 8).Should().Be(1);
        Making.SetScore(7.1f, 8).Should().Be(1);
        Making.SetScore(5.5f, 8).Should().BeApproximately(0.5f, 0.001f);
        Making.SetScore(14, 8).Should().Be(0);
    }

    [Fact]
    public void EvenCuts_ScoreBest()
    {
        Making.CutScore([0, 0, 0, 0]).Should().Be(1);
        Making.CutScore([0.05f, -0.05f, 0, 0]).Should().BeApproximately(0.75f, 0.001f);
        Making.CutScore([0.5f]).Should().Be(0);
    }

    [Fact]
    public void ThreeDaysInTheBrine_IsJustRight()
    {
        Making.BrineScore(3).Should().Be(1);
        Making.BrineScore(1).Should().BeLessThan(Making.BrineScore(2));
        Making.BrineScore(6).Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public void TheStages_GoInOrder_AndWeighIntoHowWellItIsMade()
    {
        var making = new Making(Flavour.Cumin, seed: 3);
        making.Next.Should().Be(Stage.Warm);
        making.Invoking(m => m.Brine(3)).Should().Throw<InvalidOperationException>();

        making.Warm(31);
        making.Set(making.SetsAfter);
        making.Cut([0, 0, 0, 0]);
        making.IsDone.Should().BeFalse();
        making.Brine(3);

        making.IsDone.Should().BeTrue();
        making.Made.Should().BeApproximately(1, 0.001f);
    }

    [Fact]
    public void TheCurd_SetsBetweenSixAndTenSeconds() =>
        Enumerable.Range(0, 50).Select(seed => new Making(Flavour.Plain, seed: seed).SetsAfter).Should().OnlyContain(s => s >= 6 && s <= 10);
}

public class ShopTests
{
    [Fact]
    public void ACustomer_TakesTheirFlavour_OneRipenessEitherSide()
    {
        var customer = new Customer("Anna", Ripeness.Matured, Flavour.Plain, 1);
        customer.Accepts(new Wheel(1, madeOn: 0, Flavour.Plain, 1), day: 7).Should().BeTrue("matured");
        customer.Accepts(new Wheel(2, madeOn: 0, Flavour.Plain, 1), day: 4).Should().BeTrue("young matured is one off");
        customer.Accepts(new Wheel(3, madeOn: 0, Flavour.Plain, 1), day: 1).Should().BeFalse("young is two off");
        customer.Accepts(new Wheel(4, madeOn: 0, Flavour.Cumin, 1), day: 7).Should().BeFalse("not their flavour");
    }

    [Fact]
    public void ExactlyWhatTheyAskedFor_PaysMost()
    {
        var customer = new Customer("Bram", Ripeness.Matured, Flavour.Plain, 1);
        var exact = customer.Offer(new Wheel(1, 0, Flavour.Plain, 1), 7);
        var near = customer.Offer(new Wheel(2, 0, Flavour.Plain, 1), 10);
        exact.Should().BeGreaterThan(0);
        exact.Should().Be((int)MathF.Round(19 * 1.1f * 1.25f));
        near.Should().BeLessThan((int)MathF.Round(Ripening.Price(Ripeness.ExtraMatured) * 1.1f * 1.25f));
        customer.Offer(new Wheel(3, 0, Flavour.Plain, 1), 1).Should().Be(0);
    }

    [Fact]
    public void TheSameDay_BringsTheSamePeople()
    {
        var a = Customers.For(5, 9, 4, [Flavour.Plain, Flavour.Cumin]);
        var b = Customers.For(5, 9, 4, [Flavour.Plain, Flavour.Cumin]);
        a.Should().Equal(b);
        a.Should().HaveCount(4);
        a.Should().OnlyContain(c => c.Flavour == Flavour.Plain || c.Flavour == Flavour.Cumin);
    }

    [Fact]
    public void OnTheFirstDays_PeopleAskForYoungCheese() =>
        Customers.For(1, 1, 20, [Flavour.Plain]).Should().OnlyContain(c => c.Wants <= Ripeness.YoungMatured);
}

public class FarmTests
{
    private static Making Perfect(Farm farm, Flavour flavour = Flavour.Plain)
    {
        var making = farm.StartMaking(flavour)!;
        making.Warm(31);
        making.Set(making.SetsAfter);
        making.Cut([0, 0, 0, 0]);
        making.Brine(3);
        return making;
    }

    [Fact]
    public void ANewFarm_HasMilkForOneWheel()
    {
        var farm = new Farm(1);
        farm.CanMake.Should().BeTrue();
        farm.Finish(Perfect(farm));

        farm.Milk.Should().Be(0);
        farm.CanMake.Should().BeFalse();
        farm.Wheels.Should().ContainSingle().Which.Stars.Should().Be(5);
    }

    [Fact]
    public void AFlavourNotBought_CantBeMade() => new Farm(1).StartMaking(Flavour.Nettle).Should().BeNull();

    [Fact]
    public void TheHerd_FillsTheTank_AndTheDairyTakesTheRest()
    {
        var farm = new Farm(1);
        for (var i = 0; i < 4; i++)
        {
            farm.NextDay();
        }

        farm.Milk.Should().Be(Farm.Tank);
        var report = farm.NextDay();
        report.ToDairy.Should().Be(100);
        report.DairyPay.Should().Be(15);
    }

    [Fact]
    public void AWheelLeftUnturned_IsNeglected()
    {
        var farm = new Farm(1);
        var wheel = farm.Finish(Perfect(farm));
        farm.NextDay();
        farm.NextDay();
        farm.Due.Should().Be(1);
        farm.NextDay().Neglected.Should().Be(1);

        farm.Turn(wheel);
        farm.NextDay().Neglected.Should().Be(0);
        wheel.Neglect.Should().Be(1);
    }

    [Fact]
    public void Selling_PaysAndServesTheCustomerOnce()
    {
        var farm = new Farm(1);
        var wheel = farm.Finish(Perfect(farm));
        farm.NextDay();
        var offer = farm.BestOffer(wheel);
        offer.Should().NotBeNull("someone on the second day wants young cheese");

        var money = farm.Money;
        var price = farm.Sell(offer!.Value.Customer, wheel);

        price.Should().Be(offer.Value.Price);
        farm.Money.Should().Be(money + price);
        farm.Wheels.Should().BeEmpty();
        farm.Served(offer.Value.Customer).Should().BeTrue();
        farm.Sell(offer.Value.Customer, wheel).Should().Be(0);
    }

    [Fact]
    public void Upgrades_CostMoney_AndRunOut()
    {
        var farm = new Farm(1);
        farm.CanBuy(Upgrade.Cow).Should().BeFalse("a cow costs more than the farm starts with");
        farm.Buy(Upgrade.Cumin).Should().BeTrue();
        farm.Money.Should().Be(20);
        farm.Flavours.Should().Contain(Flavour.Cumin);
        farm.Available(Upgrade.Cumin).Should().BeFalse();
        farm.Buy(Upgrade.Cumin).Should().BeFalse();
    }

    [Fact]
    public void AFarm_IsSavedAndReadBack()
    {
        var farm = new Farm(42);
        var wheel = farm.Finish(Perfect(farm, Flavour.Plain));
        farm.Buy(Upgrade.Cumin);
        farm.NextDay();
        farm.NextDay();
        farm.NextDay();
        farm.Sell(0, wheel);

        var read = Farm.FromJson(farm.ToJson());

        read.ToJson().Should().Be(farm.ToJson());
        read.Wheels.Should().HaveCount(farm.Wheels.Count);
        read.Flavours.Should().BeEquivalentTo(farm.Flavours);
    }

    [Fact]
    public void Nonsense_IsNotAFarm() => FluentActions.Invoking(() => Farm.FromJson("{}")).Should().Throw<InvalidDataException>();

    /// <summary>
    /// A plain strategy (make a good wheel whenever there is milk and room, turn what is due, sell each wheel to the
    /// best offer, buy cows and racks when there is money) must make the farm grow within a month of days.
    /// </summary>
    [Fact]
    public void ASteadyFarmer_GrowsTheFarmWithinAMonth()
    {
        var farm = new Farm(7);
        for (var day = 0; day < 30; day++)
        {
            while (farm.CanMakeSize(CheeseSize.Wheel))
            {
                farm.Finish(Perfect(farm));
            }

            foreach (var wheel in farm.Wheels.Where(w => w.NeedsTurning(farm.Day)).ToList())
            {
                farm.Turn(wheel);
            }

            // Let some ripen: sell only what has reached young matured, or when the cellar is full.
            foreach (var wheel in farm.Wheels.ToList())
            {
                if ((wheel.Ripeness(farm.Day) >= Ripeness.YoungMatured || farm.Wheels.Count >= farm.Room) && farm.BestOffer(wheel) is { } offer)
                {
                    farm.Sell(offer.Customer, wheel);
                }
            }

            foreach (var upgrade in new[] { Upgrade.Rack, Upgrade.Cow })
            {
                farm.Buy(upgrade);
            }

            farm.NextDay();
        }

        (farm.Cows + farm.Racks).Should().BeGreaterThan(6, farm.ToString());
        farm.WheelsSold.Should().BeGreaterThan(15);
    }
}

public class FairTests
{
    [Fact]
    public void TheFair_IsOnEverySeventhDay()
    {
        Fair.IsOn(7).Should().BeTrue();
        Fair.IsOn(14).Should().BeTrue();
        Fair.IsOn(8).Should().BeFalse();
        Fair.IsOn(0).Should().BeFalse();
    }

    [Fact]
    public void TheJudges_LikeStarsMost_AndAge_Too()
    {
        var young = new Wheel(1, madeOn: 20, Flavour.Plain, 1);
        var old = new Wheel(2, madeOn: 0, Flavour.Plain, 1);
        var poor = new Wheel(3, madeOn: 0, Flavour.Plain, 0.3f);

        Fair.Score(young, 21).Should().Be(80);
        Fair.Score(old, 21).Should().Be(96, "five stars and old");
        Fair.Score(poor, 21).Should().BeLessThan(Fair.Score(young, 21));
    }

    [Fact]
    public void TheRivals_AreTheSameForTheSameDay_AndThreeOfThem()
    {
        Fair.Others(5, 14).Should().Equal(Fair.Others(5, 14));
        Fair.Others(5, 14).Should().HaveCount(3).And.OnlyContain(e => e.Score >= 20 && e.Score <= 96);
    }

    [Fact]
    public void APerfectOldWheel_Places_AndIsPaid()
    {
        var farm = new Farm(9);
        var making = farm.StartMaking(Flavour.Plain)!;
        making.Warm(31);
        making.Set(making.SetsAfter);
        making.Cut([0, 0, 0, 0]);
        making.Brine(3);
        var wheel = farm.Finish(making);
        while (!Fair.IsOn(farm.Day))
        {
            farm.Turn(wheel);
            farm.NextDay();
        }

        farm.CanEnterFair.Should().BeTrue();
        var money = farm.Money;
        var result = farm.EnterFair(wheel, "Us")!;

        result.Entries.Should().HaveCount(4);
        result.Place.Should().BeInRange(1, 2, "five stars against the first week's rivals");
        farm.Money.Should().Be(money + result.Prize);
        farm.Rosettes[result.Place - 1].Should().Be(1);
        farm.Wheels.Should().BeEmpty("the wheel went to the fair");
        farm.CanEnterFair.Should().BeFalse("one wheel a fair");

        var read = Farm.FromJson(farm.ToJson());
        read.Rosettes.Should().Equal(farm.Rosettes);
        read.CanEnterFair.Should().BeFalse();
    }
}
