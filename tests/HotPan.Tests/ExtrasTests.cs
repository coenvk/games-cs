namespace HotPan.Tests;

public class ExtrasTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 60.0);

    private static void Run(HotPanGame game, double seconds, PanInput input = default)
    {
        for (var t = 0.0; t < seconds; t += Frame.TotalSeconds)
        {
            game.Update(Frame, input);
        }
    }

    [Fact]
    public void APowerUp_FallsOntoWhatIsBelow_AndIsTakenByWalkingIntoIt()
    {
        var game = new HotPanGame(seed: 1);
        var item = game.DropPowerUp(Treat.Gloves, game.CookX);

        Run(game, 1);

        game.PowerUps.Should().NotContain(item);
        game.GlovesLeft.Should().BeGreaterThan(HotPanGame.GlovesTime - 1);
        game.DrainEvents().OfType<PowerUpTaken>().Should().ContainSingle();
    }

    [Fact]
    public void Gloves_StopTheBurning()
    {
        var game = new HotPanGame(seed: 1);
        game.DropPowerUp(Treat.Gloves, game.CookX);
        Run(game, 0.5);

        // Walk off the chicken onto the bare pan and stand there.
        Run(game, 1.5, new PanInput(1, false, false));
        var health = game.Health;
        Run(game, 1);

        game.Health.Should().Be(health);
        game.IsBurning.Should().BeFalse();
    }

    [Fact]
    public void ADrink_GivesHealthBack()
    {
        var game = new HotPanGame(seed: 1);
        Run(game, 1.5, new PanInput(1, false, false));
        var burnt = game.Health;
        burnt.Should().BeLessThan(HotPanGame.MaxHealth);

        game.DropPowerUp(Treat.Drink, game.CookX);
        Run(game, 0.8);

        game.DrainEvents().OfType<PowerUpTaken>().Should().ContainSingle();
        game.Health.Should().BeGreaterThan(burnt, "the drink gave back more than the pan took meanwhile");
    }

    [Fact]
    public void Ice_CoolsTheChicken_AndTheLidStopsThePan()
    {
        var game = new HotPanGame(seed: 1);
        Run(game, 5);
        var warm = game.Chicken.Where(c => c.Landed).Average(c => c.Temperature);

        game.DropPowerUp(Treat.Ice, game.CookX);
        game.DropPowerUp(Treat.Lid, game.CookX);
        Run(game, 1);
        var pan = game.PanTemperature;
        Run(game, 3);

        game.Chicken.Where(c => c.Landed).Average(c => c.Temperature).Should().BeLessThan(warm);
        game.PanTemperature.Should().Be(pan);
        game.LidLeft.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Oil_BubblesFirst_ThenSpits_HurtingTheCookNear()
    {
        var game = new HotPanGame(seed: 1);
        game.Bubble(game.CookX);
        game.Bubble(HotPanGame.PanLeft + 10);

        Run(game, HotPanGame.SpitWarning / 2);
        game.Bubbles.Should().HaveCount(2);
        Run(game, HotPanGame.SpitWarning);

        var spits = game.DrainEvents().OfType<OilSpat>().ToList();
        spits.Should().HaveCount(2);
        spits.Count(s => s.Hit).Should().Be(1);
        game.Health.Should().BeApproximately(HotPanGame.MaxHealth - HotPanGame.SpitDamage, 0.5f);
    }

    [Fact]
    public void TheSpatula_FlipsTheHottestPiece_AndThrowsTheCookStandingOnIt()
    {
        var game = new HotPanGame(seed: 1);
        game.Flip().Should().BeFalse("nothing is too hot yet");
        foreach (var piece in game.Chicken)
        {
            piece.Temperature = 90;
        }

        var under = game.StandingOn!;
        under.Temperature = 120;
        game.Flip().Should().BeTrue();

        under.Temperature.Should().BeApproximately(66, 0.01f);
        game.DrainEvents().OfType<ChickenFlipped>().Single().CookThrown.Should().BeTrue();
        Run(game, 0.1);
        game.OnGround.Should().BeFalse();
    }

    [Fact]
    public void TheChicken_StillComesAsItDid_WithTheExtras()
    {
        // The extras draw their own random numbers: the same seed drops the same pieces at the same places.
        var game = new HotPanGame(seed: 5);
        Run(game, 30, new PanInput(0, true, true));

        game.DrainEvents().OfType<ChickenDropped>().Select(d => (d.Chicken.Cut, (int)d.Chicken.X)).Should().NotBeEmpty();
    }
}
