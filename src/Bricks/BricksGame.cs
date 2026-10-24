using System.Numerics;
using Jentl.Ecs;
using Jentl.Physics;

namespace Bricks;

/// <summary>
/// A brick-breaking game: entities in a <see cref="World"/> (paddle, balls, bricks, power-ups, walls), each with a
/// body in a <see cref="PhysicsWorld"/>, and systems that run the rules once per update. The field is
/// <see cref="Width"/> by <see cref="Height"/> units with the origin at the top left; the bottom is open.
/// What happened comes out as <see cref="BricksEvent"/>s (<c>World.Drain&lt;BricksEvent&gt;()</c>).
/// </summary>
public sealed class BricksGame
{
    public const float Width = 640;
    public const float Height = 720;
    public const float WallThickness = 20;
    public const float BallRadius = 7;
    public const float PaddleY = Height - 50;
    public const float PaddleHeight = 14;
    public const float NormalPaddle = 96;
    public const float BrickHeight = 22;
    public const float BricksTop = 90;
    public const int StartingLives = 3;

    private static readonly TimeSpan WideTime = TimeSpan.FromSeconds(12);

    private readonly SystemGroup _systems = new();
    private readonly List<Contact> _contacts = [];
    private readonly Random _random;

    public BricksGame(int seed, int level = 0)
    {
        _random = new Random(seed);
        Seed = seed;
        Physics.ContactBegan += _contacts.Add;
        World.SetResource(Physics);

        // Order matters: steer the paddle, keep stuck balls on it, simulate, then apply the rules to what touched.
        _systems
            .Add("paddle", MovePaddle)
            .Add("stuck balls", CarryStuckBalls)
            .Add("physics", (_, elapsed) => Physics.Update(elapsed))
            .Add("collisions", HandleContacts)
            .Add("balls", KeepBallsMoving)
            .Add("power-ups", DropPowerUps)
            .Add("rounds", CheckRound);

        AddWalls();
        AddPaddle();
        StartLevel(level);
    }

    public int Seed { get; }

    public World World { get; } = new();

    /// <summary>Balls fly without gravity; only power-ups fall.</summary>
    public PhysicsWorld Physics { get; } = new() { Gravity = new Vector2(0, 260) };

    public int Score { get; private set; }

    public int Lives { get; private set; } = StartingLives;

    /// <summary>0-based; after the last wall the walls repeat, faster.</summary>
    public int Level { get; private set; }

    public bool IsOver { get; private set; }

    /// <summary>How fast balls fly (units a second): quicker on later levels and as bricks break.</summary>
    public float BallSpeed { get; private set; }

    /// <summary>Where the paddle is heading (its centre); it gets there as fast as the frame allows.</summary>
    public float PaddleTarget { get; set; } = Width / 2;

    public Entity Paddle => World.FindOne<Paddle>();

    public int BricksLeft => World.Query<Brick>().Count(e => !World.Get<Brick>(e).Unbreakable);

    public int BallCount => World.CountOf<Ball>();

    public bool HasStuckBall => World.Query<Ball>().Any(e => World.Get<Ball>(e).Stuck);

    /// <summary>Lets go of any balls sitting on the paddle.</summary>
    public void Launch()
    {
        foreach (var entity in World.Query<Ball, Physical>())
        {
            ref var ball = ref World.Get<Ball>(entity);
            if (ball.Stuck)
            {
                ball.Stuck = false;
                World.Get<Physical>(entity).Body.Velocity = Direction(ball.Offset / (NormalPaddle / 2) * 0.5f) * BallSpeed;
            }
        }
    }

    public void Update(TimeSpan elapsed)
    {
        if (!IsOver)
        {
            _systems.Update(World, elapsed);
        }
    }

    /// <summary>Takes the events since the last call.</summary>
    public IReadOnlyList<BricksEvent> DrainEvents() => World.Drain<BricksEvent>();

    /// <summary>A direction <paramref name="tilt"/> of the way from straight up to 60° either side.</summary>
    private static Vector2 Direction(float tilt)
    {
        var angle = Math.Clamp(tilt, -1, 1) * MathF.PI / 3;
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
    }

    private void StartLevel(int level)
    {
        Level = level;
        BallSpeed = 420 + 40 * Math.Min(level, 8);
        foreach (var entity in World.Query<Brick>().Concat(World.Query<Ball>()).Concat(World.Query<PowerUp>()))
        {
            Destroy(entity);
        }

        var rows = Levels.For(level);
        var brickWidth = (Width - 2 * WallThickness) / Levels.Columns;
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < Levels.Columns; column++)
            {
                var kind = rows[row][column];
                if (kind == '.')
                {
                    continue;
                }

                var centre = new Vector2(WallThickness + (column + 0.5f) * brickWidth, BricksTop + (row + 0.5f) * BrickHeight);
                var brick = kind switch
                {
                    '#' => new Brick(0, 0, row, Unbreakable: true),
                    '2' => new Brick(2, 25, row, Unbreakable: false),
                    _ => new Brick(1, 10, row, Unbreakable: false),
                };
                Spawn(new Body(new Box(brickWidth - 2, BrickHeight - 2), centre, BodyType.Static) { Layer = Layers.Brick, Restitution = 1, Friction = 0 }, brick);
            }
        }

        ref var paddle = ref World.Get<Paddle>(Paddle);
        paddle.WideFor = TimeSpan.Zero;
        SetPaddleWidth(NormalPaddle);
        AddStuckBall();
    }

    private void AddWalls()
    {
        var half = WallThickness / 2;
        Body Wall(Vector2 centre, float width, float height) =>
            new(new Box(width, height), centre, BodyType.Static) { Layer = Layers.Wall, Restitution = 1, Friction = 0 };

        Spawn(Wall(new Vector2(half, Height / 2), WallThickness, Height * 2), new Wall());
        Spawn(Wall(new Vector2(Width - half, Height / 2), WallThickness, Height * 2), new Wall());
        Spawn(Wall(new Vector2(Width / 2, half), Width, WallThickness), new Wall());
    }

    private void AddPaddle()
    {
        var body = new Body(new Box(NormalPaddle, PaddleHeight), new Vector2(Width / 2, PaddleY), BodyType.Kinematic)
        {
            Layer = Layers.Paddle,
            Restitution = 1,
            Friction = 0,
        };
        Spawn(body, new Paddle(NormalPaddle, TimeSpan.Zero));
    }

    private void AddStuckBall() => AddBall(new Vector2(PaddleTarget, PaddleY - PaddleHeight / 2 - BallRadius - 1), Vector2.Zero, stuck: true);

    private void AddBall(Vector2 position, Vector2 velocity, bool stuck)
    {
        var body = new Body(new Circle(BallRadius), position)
        {
            Layer = Layers.Ball,
            Mask = Layers.Brick | Layers.Wall | Layers.Paddle,
            Restitution = 1,
            Friction = 0,
            GravityScale = 0,
            Velocity = velocity,
        };
        var offset = stuck ? position.X - World.Get<Physical>(Paddle).Body.Position.X : 0;
        Spawn(body, new Ball(stuck, offset));
    }

    private Entity Spawn<T>(Body body, T component)
    {
        var entity = World.Spawn().With(new Physical(body)).With(component).Entity;
        body.UserData = entity;
        Physics.Add(body);
        return entity;
    }

    private void Destroy(Entity entity)
    {
        if (World.TryGet<Physical>(entity, out var physical))
        {
            Physics.Remove(physical.Body);
        }

        World.Destroy(entity);
    }

    private void SetPaddleWidth(float width)
    {
        World.Get<Paddle>(Paddle).Width = width;
        World.Get<Physical>(Paddle).Body.Shape = new Box(width, PaddleHeight);
    }

    /// <summary>The paddle is kinematic: give it the velocity that reaches the target this frame, so the ball feels its motion.</summary>
    private void MovePaddle(World world, TimeSpan elapsed)
    {
        ref var paddle = ref world.Get<Paddle>(Paddle);
        if (paddle.WideFor > TimeSpan.Zero)
        {
            paddle.WideFor -= elapsed;
            if (paddle.WideFor <= TimeSpan.Zero)
            {
                SetPaddleWidth(NormalPaddle);
            }
        }

        var body = world.Get<Physical>(Paddle).Body;
        var half = world.Get<Paddle>(Paddle).Width / 2;
        var target = Math.Clamp(PaddleTarget, WallThickness + half, Width - WallThickness - half);
        PaddleTarget = target;
        var seconds = (float)elapsed.TotalSeconds;
        body.Velocity = seconds > 0 ? new Vector2((target - body.Position.X) / seconds, 0) : Vector2.Zero;
    }

    private void CarryStuckBalls(World world, TimeSpan elapsed)
    {
        var paddle = world.Get<Physical>(Paddle).Body;
        world.ForEach((Entity _, ref Ball ball, ref Physical physical) =>
        {
            if (ball.Stuck)
            {
                // Ride where the paddle will be at the end of this frame.
                var x = paddle.Position.X + paddle.Velocity.X * (float)elapsed.TotalSeconds + ball.Offset;
                physical.Body.Position = new Vector2(x, PaddleY - PaddleHeight / 2 - BallRadius - 1);
                physical.Body.Velocity = Vector2.Zero;
            }
        });
    }

    private void HandleContacts(World world, TimeSpan elapsed)
    {
        foreach (var contact in _contacts)
        {
            if (contact.A.UserData is not Entity a || contact.B.UserData is not Entity b || !world.IsAlive(a) || !world.IsAlive(b))
            {
                continue;
            }

            if (world.Has<PowerUp>(a) || world.Has<PowerUp>(b))
            {
                var power = world.Has<PowerUp>(a) ? a : b;
                if (world.Has<Paddle>(contact.Other(world.Get<Physical>(power).Body).UserData is Entity other ? other : Entity.None))
                {
                    Catch(power);
                }

                continue;
            }

            var ball = world.Has<Ball>(a) ? a : world.Has<Ball>(b) ? b : Entity.None;
            if (ball == Entity.None)
            {
                continue;
            }

            var ballBody = world.Get<Physical>(ball).Body;
            var hit = ball == a ? b : a;
            if (world.Has<Brick>(hit))
            {
                HitBrick(hit);
            }
            else if (world.Has<Paddle>(hit))
            {
                // Where the ball lands on the paddle steers it: the edges send it off at a slant.
                var paddle = world.Get<Physical>(hit).Body;
                var tilt = (ballBody.Position.X - paddle.Position.X) / (world.Get<Paddle>(hit).Width / 2);
                ballBody.Velocity = Direction(tilt) * BallSpeed;
                world.Emit<BricksEvent>(new PaddleBounce(ballBody.Position));
            }
            else
            {
                world.Emit<BricksEvent>(new WallBounce(ballBody.Position));
            }
        }

        _contacts.Clear();
    }

    private void HitBrick(Entity entity)
    {
        ref var brick = ref World.Get<Brick>(entity);
        var position = World.Get<Physical>(entity).Body.Position;
        if (brick.Unbreakable)
        {
            World.Emit<BricksEvent>(new BrickHit(position, brick.Row, Broken: false));
            return;
        }

        brick.Hits--;
        if (brick.Hits > 0)
        {
            World.Emit<BricksEvent>(new BrickHit(position, brick.Row, Broken: false));
            return;
        }

        Score += brick.Points * (1 + Level / Levels.All.Count);
        BallSpeed = MathF.Min(BallSpeed + 3, 760);
        World.Emit<BricksEvent>(new BrickHit(position, brick.Row, Broken: true));
        Destroy(entity);

        // One brick in six drops a power-up.
        if (_random.Next(6) == 0)
        {
            var kind = (PowerKind)_random.Next(Enum.GetValues<PowerKind>().Length);
            var body = new Body(new Box(36, 16), position)
            {
                Layer = Layers.PowerUp,
                Mask = Layers.Paddle,
                IsSensor = true,
                Velocity = new Vector2(0, 60),
            };
            Spawn(body, new PowerUp(kind));
        }
    }

    private void Catch(Entity entity)
    {
        var kind = World.Get<PowerUp>(entity).Kind;
        var position = World.Get<Physical>(entity).Body.Position;
        Destroy(entity);
        switch (kind)
        {
            case PowerKind.Wide:
                World.Get<Paddle>(Paddle).WideFor = WideTime;
                SetPaddleWidth(NormalPaddle * 1.6f);
                break;
            case PowerKind.Multi:
                // Split every flying ball into three.
                foreach (var ball in World.Query<Ball, Physical>().Where(e => !World.Get<Ball>(e).Stuck).Take(4).ToList())
                {
                    var body = World.Get<Physical>(ball).Body;
                    var heading = body.Velocity == Vector2.Zero ? -Vector2.UnitY : Vector2.Normalize(body.Velocity);
                    foreach (var turn in new[] { -0.4f, 0.4f })
                    {
                        var turned = Vector2.Transform(heading, Matrix3x2.CreateRotation(turn));
                        AddBall(body.Position, turned * BallSpeed, stuck: false);
                    }
                }

                break;
            case PowerKind.Life:
                Lives++;
                break;
        }

        World.Emit<BricksEvent>(new PowerCaught(kind, position));
    }

    /// <summary>Balls keep a steady speed and never fly too flat (or they would bounce from wall to wall for ever); lost ones go.</summary>
    private void KeepBallsMoving(World world, TimeSpan elapsed)
    {
        foreach (var entity in world.Query<Ball, Physical>())
        {
            var body = world.Get<Physical>(entity).Body;
            if (world.Get<Ball>(entity).Stuck)
            {
                continue;
            }

            if (body.Position.Y > Height + BallRadius * 4)
            {
                Destroy(entity);
                world.Emit<BricksEvent>(new BallLost());
                continue;
            }

            var velocity = body.Velocity == Vector2.Zero ? -Vector2.UnitY : Vector2.Normalize(body.Velocity);
            if (MathF.Abs(velocity.Y) < 0.25f)
            {
                velocity = Vector2.Normalize(new Vector2(velocity.X, velocity.Y < 0 ? -0.25f : 0.25f));
            }

            body.Velocity = velocity * BallSpeed;
        }
    }

    private void DropPowerUps(World world, TimeSpan elapsed)
    {
        foreach (var entity in world.Query<PowerUp, Physical>())
        {
            if (world.Get<Physical>(entity).Body.Position.Y > Height + 40)
            {
                Destroy(entity);
            }
        }
    }

    private void CheckRound(World world, TimeSpan elapsed)
    {
        if (BricksLeft == 0)
        {
            world.Emit<BricksEvent>(new LevelCleared(Level));
            StartLevel(Level + 1);
            return;
        }

        if (BallCount > 0)
        {
            return;
        }

        Lives--;
        if (Lives <= 0)
        {
            IsOver = true;
            world.Emit<BricksEvent>(new GameOver(Score));
            return;
        }

        world.Emit<BricksEvent>(new LifeLost(Lives));
        World.Get<Paddle>(Paddle).WideFor = TimeSpan.Zero;
        SetPaddleWidth(NormalPaddle);
        AddStuckBall();
    }
}
