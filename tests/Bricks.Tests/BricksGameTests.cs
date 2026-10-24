using System.Numerics;
using Jentl.Ecs;
using Jentl.Physics;

namespace Bricks.Tests;

public class BricksGameTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    private readonly BricksGame _game = new(seed: 1);

    [Fact]
    public void ANewGame_HasAFullWall_AndABallOnThePaddle()
    {
        _game.BricksLeft.Should().Be(50);
        _game.BallCount.Should().Be(1);
        _game.HasStuckBall.Should().BeTrue();
        _game.Lives.Should().Be(BricksGame.StartingLives);
        _game.Score.Should().Be(0);
    }

    [Fact]
    public void AStuckBall_RidesWithThePaddle_UntilLaunched()
    {
        _game.PaddleTarget = 200;
        Run(0.2);
        BallBody().Position.X.Should().BeApproximately(200, 0.5f);

        _game.Launch();
        Run(0.2);

        _game.HasStuckBall.Should().BeFalse();
        BallBody().Position.Y.Should().BeLessThan(BricksGame.PaddleY - 50);
    }

    [Fact]
    public void ThePaddle_StaysBetweenTheWalls()
    {
        _game.PaddleTarget = -500;
        Run(0.1);

        PaddleBody().Position.X.Should().BeApproximately(BricksGame.WallThickness + BricksGame.NormalPaddle / 2, 0.5f);
    }

    [Fact]
    public void ABallBreaksABrick_AndScores()
    {
        _game.Launch();
        Run(2);

        _game.BricksLeft.Should().BeLessThan(50);
        _game.Score.Should().BeGreaterThan(0);
        _game.DrainEvents().OfType<BrickHit>().Should().Contain(h => h.Broken);
    }

    [Fact]
    public void TheBallKeepsItsSpeed()
    {
        _game.Launch();
        Run(1.5);

        foreach (var ball in Balls())
        {
            ball.Velocity.Length().Should().BeApproximately(_game.BallSpeed, 1);
            MathF.Abs(ball.Velocity.Y).Should().BeGreaterThan(_game.BallSpeed * 0.2f, "it never flies flat");
        }
    }

    [Fact]
    public void ThePaddleEdge_SendsTheBallOffAtASlant()
    {
        _game.PaddleTarget = BricksGame.Width / 2;
        Run(0.1);
        var ball = Flying(new Vector2(BricksGame.Width / 2 + 40, BricksGame.PaddleY - 40), new Vector2(0, 400));

        Run(0.2);

        ball.Velocity.X.Should().BeGreaterThan(200, "it hit right of centre");
        ball.Velocity.Y.Should().BeNegative();
        _game.DrainEvents().OfType<PaddleBounce>().Should().NotBeEmpty();
    }

    [Fact]
    public void LosingTheLastBall_CostsALife_AndGivesANewOne()
    {
        Flying(new Vector2(BricksGame.Width / 2, BricksGame.Height - 10), new Vector2(0, 500));

        Run(0.5);

        _game.Lives.Should().Be(BricksGame.StartingLives - 1);
        _game.HasStuckBall.Should().BeTrue();
        _game.DrainEvents().Should().ContainSingle(e => e is LifeLost);
    }

    [Fact]
    public void LosingEveryLife_EndsTheGame()
    {
        for (var life = 0; life < BricksGame.StartingLives; life++)
        {
            Flying(new Vector2(BricksGame.Width / 2, BricksGame.Height - 10), new Vector2(0, 500));
            Run(0.5);
        }

        _game.IsOver.Should().BeTrue();
        _game.Lives.Should().Be(0);
        _game.DrainEvents().OfType<GameOver>().Should().ContainSingle();
    }

    [Fact]
    public void ClearingTheWall_StartsTheNextLevel()
    {
        // Keep only the brick straight above the paddle.
        _game.PaddleTarget = 350;
        Run(0.2);
        var bricks = _game.World.Query<Brick, Physical>();
        var kept = bricks.MaxBy(e => (_game.World.Get<Physical>(e).Body.Position.Y, -MathF.Abs(_game.World.Get<Physical>(e).Body.Position.X - 350)));
        foreach (var brick in bricks.Where(b => b != kept))
        {
            _game.Physics.Remove(_game.World.Get<Physical>(brick).Body);
            _game.World.Destroy(brick);
        }

        _game.Launch();
        Run(4);

        _game.Level.Should().Be(1);
        _game.BricksLeft.Should().Be(Levels.For(1).Sum(row => row.Count(c => c is '1' or '2')));
        _game.HasStuckBall.Should().BeTrue("a new level starts with the ball on the paddle");
        _game.DrainEvents().OfType<LevelCleared>().Should().ContainSingle();
    }

    [Fact]
    public void ToughBricks_TakeTwoHits()
    {
        var game = new BricksGame(seed: 1, level: 3);
        var tough = game.World.Query<Brick>().First(e => game.World.Get<Brick>(e).Hits == 2);

        game.World.Get<Brick>(tough).Hits.Should().Be(2);
        game.BricksLeft.Should().Be(Levels.For(3).Sum(row => row.Count(c => c is '1' or '2')), "unbreakable bricks don't count");
    }

    [Theory]
    [InlineData(PowerKind.Wide)]
    [InlineData(PowerKind.Multi)]
    [InlineData(PowerKind.Life)]
    public void CatchingAPowerUp_GivesItsPower(PowerKind kind)
    {
        _game.Launch();
        Run(0.05);
        DropPowerUp(kind, new Vector2(_game.PaddleTarget, BricksGame.PaddleY - 30));

        Run(0.5);

        _game.DrainEvents().OfType<PowerCaught>().Should().ContainSingle(p => p.Kind == kind);
        _game.World.CountOf<PowerUp>().Should().Be(0);
        switch (kind)
        {
            case PowerKind.Wide:
                _game.World.Get<Paddle>(_game.Paddle).Width.Should().BeGreaterThan(BricksGame.NormalPaddle);
                break;
            case PowerKind.Multi:
                _game.BallCount.Should().Be(3);
                break;
            case PowerKind.Life:
                _game.Lives.Should().Be(BricksGame.StartingLives + 1);
                break;
        }
    }

    [Fact]
    public void AMissedPowerUp_FallsAway()
    {
        DropPowerUp(PowerKind.Life, new Vector2(BricksGame.WallThickness + 30, BricksGame.PaddleY));

        Run(2);

        _game.World.CountOf<PowerUp>().Should().Be(0);
        _game.Lives.Should().Be(BricksGame.StartingLives);
    }

    [Fact]
    public void TheSameSeed_PlaysTheSame()
    {
        var other = new BricksGame(seed: 1);
        foreach (var game in new[] { _game, other })
        {
            game.PaddleTarget = 250;
            game.Launch();
            for (var i = 0; i < 300; i++)
            {
                game.PaddleTarget = 150 + i % 300;
                game.Update(Frame);
            }
        }

        other.Score.Should().Be(_game.Score);
        other.BricksLeft.Should().Be(_game.BricksLeft);
    }

    private void Run(double seconds)
    {
        for (var i = 0; i < Math.Round(seconds * 60); i++)
        {
            _game.Update(Frame);
        }
    }

    private Body PaddleBody() => _game.World.Get<Physical>(_game.Paddle).Body;

    private Body BallBody() => Balls().Single();

    private IEnumerable<Body> Balls() => _game.World.Query<Ball, Physical>().Select(e => _game.World.Get<Physical>(e).Body);

    /// <summary>Turns the first ball into a flying one at a place and speed of the test's choosing.</summary>
    private Body Flying(Vector2 position, Vector2 velocity)
    {
        var entity = _game.World.Query<Ball>()[0];

        _game.World.Get<Ball>(entity).Stuck = false;
        var body = _game.World.Get<Physical>(entity).Body;
        body.Position = position;
        body.Velocity = velocity;
        return body;
    }

    private void DropPowerUp(PowerKind kind, Vector2 position)
    {
        var body = new Body(new Box(36, 16), position) { Layer = Layers.PowerUp, Mask = Layers.Paddle, IsSensor = true, Velocity = new Vector2(0, 60) };
        var entity = _game.World.Spawn().With(new Physical(body)).With(new PowerUp(kind)).Entity;
        body.UserData = entity;
        _game.Physics.Add(body);
    }
}
