using CheeseFarm;

namespace CheeseFarm.Tests;

public class MilkingTests
{
    /// <summary>Squeezes in turn, a squeeze every <paramref name="every"/> seconds, until it ends.</summary>
    private static Milking Steady(float every, bool alternate = true)
    {
        var milking = new Milking();
        var teat = Teat.Left;
        while (!milking.IsDone)
        {
            milking.Squeeze(teat);
            if (alternate)
            {
                teat = teat == Teat.Left ? Teat.Right : Teat.Left;
            }

            milking.Advance(every);
        }

        return milking;
    }

    [Fact]
    public void ASteadyRhythm_FillsThePail_ACalmCow()
    {
        var milking = Steady(0.3f);
        milking.Kicked.Should().BeFalse();
        milking.Bad.Should().Be(0);
        milking.Litres.Should().BeApproximately(MathF.Ceiling(Milking.Length / 0.3f) * Milking.PerSqueeze, 0.5f);
    }

    [Fact]
    public void TheSameTeatOverAndOver_GivesNothing_AndGetsThePailKicked()
    {
        var milking = Steady(0.3f, alternate: false);
        milking.Kicked.Should().BeTrue();
        milking.Good.Should().Be(1, "only the first squeeze was good");
    }

    [Fact]
    public void Hurrying_UpsetsTheCow()
    {
        var milking = Steady(0.05f);
        milking.Kicked.Should().BeTrue();
    }

    [Fact]
    public void AKickedPail_SpillsHalf()
    {
        var milking = new Milking();
        for (var i = 0; i < 20; i++)
        {
            milking.Squeeze(i % 2 == 0 ? Teat.Left : Teat.Right);
            milking.Advance(0.3f);
        }

        var before = milking.Litres;
        while (!milking.Kicked)
        {
            milking.Squeeze(Teat.Right);
        }

        milking.Litres.Should().BeApproximately(before / 2, 0.01f);
        milking.Squeeze(Teat.Right).Should().BeFalse("it is over");
    }

    [Fact]
    public void TheFarm_TakesTheMilk_OnceADay_AsMuchAsTheTankHolds()
    {
        var farm = new Farm(1);
        var milk = farm.Milk;
        var pail = Steady(0.3f);

        farm.CanMilkByHand.Should().BeTrue();
        var poured = farm.MilkByHand(pail);
        poured.Should().Be((int)MathF.Round(pail.Litres));
        farm.Milk.Should().Be(milk + poured);
        farm.CanMilkByHand.Should().BeFalse();
        farm.MilkByHand(pail).Should().Be(0);

        Farm.FromJson(farm.ToJson()).CanMilkByHand.Should().BeFalse("it is saved");
        farm.NextDay();
        farm.CanMilkByHand.Should().BeTrue("a new morning");

        for (var i = 0; i < 10; i++)
        {
            farm.NextDay();
        }

        farm.MilkByHand(pail).Should().Be(0, "the tank is full");
    }
}
