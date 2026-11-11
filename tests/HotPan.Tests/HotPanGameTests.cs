namespace HotPan.Tests;

public class HotPanGameTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 60.0);

    private static readonly PanInput Still = new(0, false, false);

    private static void Run(HotPanGame game, double seconds, PanInput? input = null)
    {
        for (var t = 0.0; t < seconds; t += Frame.TotalSeconds)
        {
            game.Update(Frame, input ?? Still);
        }
    }

    [Fact]
    public void TheCook_StartsOnColdChicken_AndDoesNotBurn()
    {
        var game = new HotPanGame(seed: 1);

        game.StandingOn.Should().NotBeNull();
        game.Chicken.Should().HaveCount(3).And.OnlyContain(c => c.Temperature == HotPanGame.FridgeTemperature);
        Run(game, 1);

        game.Health.Should().Be(HotPanGame.MaxHealth);
        game.IsBurning.Should().BeFalse();
    }

    [Fact]
    public void Chicken_WarmsTowardsThePan_ThinCutsFastest()
    {
        var game = new HotPanGame(seed: 1);
        Run(game, 5);

        var temperatures = game.Chicken.Where(c => c.Landed).ToDictionary(c => c.Id, c => c.Temperature);
        temperatures.Values.Should().OnlyContain(t => t > HotPanGame.FridgeTemperature && t < game.PanTemperature);
        var nugget = new Chicken(0, Cut.Nugget, 0, 0);
        var breast = new Chicken(0, Cut.Breast, 0, 0);
        nugget.HeatRate.Should().BeGreaterThan(breast.HeatRate);
        nugget.Width.Should().BeLessThan(breast.Width);
    }

    [Fact]
    public void ThePan_GetsHotterAsTheGameGoesOn()
    {
        var game = new HotPanGame(seed: 1);

        Run(game, 10);

        game.PanTemperature.Should().BeGreaterThan(HotPanGame.StartPanTemperature + 10);
        game.DropInterval.Should().BeLessThan(3);
    }

    [Fact]
    public void StandingOnThePan_Burns()
    {
        var game = new HotPanGame(seed: 1);

        // Walk left off the middle piece and stand still on the bare pan.
        Run(game, 0.6, new PanInput(-1, false, false));
        Run(game, 0.6);

        game.StandingOn.Should().BeNull();
        game.OnGround.Should().BeTrue();
        game.CookY.Should().Be(HotPanGame.FloorY);
        game.IsBurning.Should().BeTrue();
        game.Health.Should().BeLessThan(HotPanGame.MaxHealth);
        game.DrainEvents().OfType<StartedBurning>().Should().ContainSingle();
    }

    [Fact]
    public void Jumping_ReachesTheNextPiece()
    {
        var game = new HotPanGame(seed: 1);
        var next = game.Chicken.OrderBy(c => c.X).Last();
        game.DrainEvents();

        // Jump to the right, holding right until over the piece, then let go of right and come down on it.
        game.Update(Frame, new PanInput(1, true, true));
        for (var i = 0; i < 120 && !(game.OnGround && game.StandingOn is not null); i++)
        {
            game.Update(Frame, new PanInput(game.CookX < next.X ? 1 : 0, false, true));
        }

        var events = game.DrainEvents();
        events.OfType<Jumped>().Should().ContainSingle();
        game.StandingOn.Should().Be(next, "the jump carries the cook onto the piece to the right");
    }

    [Fact]
    public void AShortTap_JumpsLowerThanAHeldJump()
    {
        double Peak(bool held)
        {
            var game = new HotPanGame(seed: 1);
            var start = game.CookY;
            var lowest = start;
            game.Update(Frame, new PanInput(0, true, held));
            for (var i = 0; i < 60; i++)
            {
                game.Update(Frame, new PanInput(0, false, held));
                lowest = Math.Min(lowest, game.CookY);
            }

            return start - lowest;
        }

        Peak(held: true).Should().BeGreaterThan(Peak(held: false) * 1.5);
        Peak(held: true).Should().BeGreaterThan(120, "high enough to get onto a breast from the pan");
    }

    [Fact]
    public void PiecesFallIn_AndCharredOnesAreFlickedOut()
    {
        var game = new HotPanGame(seed: 4);

        // Hop up and down on the spot, so the cook keeps clear of the floor as long as possible.
        var events = new List<PanEvent>();
        for (var i = 0; i < 60 * 90 && !game.IsOver; i++)
        {
            game.Update(Frame, new PanInput(0, i % 40 == 0, true));
            events.AddRange(game.DrainEvents());
        }

        events.OfType<ChickenDropped>().Should().NotBeEmpty();
        events.OfType<ChickenLanded>().Should().NotBeEmpty();
        events.OfType<ChickenCharred>().Should().NotBeEmpty();
        game.Chicken.Should().HaveCountLessThanOrEqualTo(HotPanGame.MaxPieces);
        game.Chicken.Where(c => c.Landed).Should().OnlyContain(c => c.Bottom == HotPanGame.FloorY);
    }

    [Fact]
    public void AtNoHealth_TheGameIsOver_WithPointsForTheTimeLasted()
    {
        var game = new HotPanGame(seed: 2);
        Run(game, 0.6, new PanInput(-1, false, false));

        Run(game, 30);

        game.IsOver.Should().BeTrue();
        game.Health.Should().Be(0);
        var over = game.DrainEvents().OfType<GameOver>().Should().ContainSingle().Subject;
        over.Score.Should().Be(game.Score).And.Be((int)(game.Time.TotalSeconds * 10));
        var time = game.Time;
        Run(game, 1);
        game.Time.Should().Be(time, "nothing moves once it is over");
    }

    [Fact]
    public void TheSameSeed_PlaysTheSame()
    {
        static string Play(int seed)
        {
            var game = new HotPanGame(seed);
            for (var i = 0; i < 600; i++)
            {
                game.Update(Frame, new PanInput(MathF.Sin(i / 30f), i % 50 == 0, true));
            }

            return string.Join(";", game.Chicken.Select(c => $"{c.Cut}@{c.X:0.0},{c.Temperature:0.0}")) + $"|{game.CookX:0.0},{game.Health:0.0}";
        }

        Play(9).Should().Be(Play(9));
        Play(9).Should().NotBe(Play(10));
    }

    [Fact]
    public void ThePansSides_KeepTheCookIn()
    {
        var game = new HotPanGame(seed: 1);

        Run(game, 3, new PanInput(-1, true, true));

        game.CookX.Should().BeGreaterThanOrEqualTo(HotPanGame.PanLeft + (HotPanGame.CookWidth / 2));
    }
}
