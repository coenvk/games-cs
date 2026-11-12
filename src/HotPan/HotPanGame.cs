namespace HotPan;

/// <summary>The cuts of chicken that drop into the pan: thinner ones heat through faster.</summary>
public enum Cut
{
    Nugget,
    Wing,
    Drumstick,
    Breast,
}

/// <summary>A piece of chicken in the pan: a platform the cook can stand on while it is still cool enough.</summary>
public sealed class Chicken
{
    internal Chicken(int id, Cut cut, float x, float y)
    {
        Id = id;
        Cut = cut;
        (Width, Height, HeatRate) = Sizes(cut);
        X = x;
        Y = y;
    }

    public int Id { get; }

    public Cut Cut { get; }

    /// <summary>The middle of the piece, across the pan.</summary>
    public float X { get; internal set; }

    /// <summary>The top of the piece, down from the top of the field.</summary>
    public float Y { get; internal set; }

    public float Width { get; }

    public float Height { get; }

    /// <summary>How quickly it takes on the pan's heat, per second.</summary>
    public float HeatRate { get; }

    /// <summary>Degrees Celsius; it comes out of the fridge.</summary>
    public float Temperature { get; internal set; } = HotPanGame.FridgeTemperature;

    public float FallSpeed { get; internal set; }

    /// <summary>Whether it has come to rest in the pan (falling pieces aren't stood on, nor heated).</summary>
    public bool Landed { get; internal set; }

    /// <summary>How long it has been charred, before it is flicked out.</summary>
    public float CharredFor { get; internal set; }

    public float Left => X - (Width / 2);

    public float Right => X + (Width / 2);

    public float Bottom => Y + Height;

    /// <summary>Whether the cook's feet burn standing on it.</summary>
    public bool IsTooHot => Temperature > HotPanGame.SafeTemperature;

    public bool IsCharred => Temperature >= HotPanGame.CharTemperature;

    private static (float Width, float Height, float HeatRate) Sizes(Cut cut) => cut switch
    {
        Cut.Nugget => (54, 26, 0.045f),
        Cut.Wing => (76, 30, 0.034f),
        Cut.Drumstick => (96, 34, 0.024f),
        _ => (124, 40, 0.016f),
    };
}

/// <summary>What the player does this frame.</summary>
/// <param name="Move">-1 (left) to 1 (right).</param>
/// <param name="Jump">Jump was pressed this frame.</param>
/// <param name="JumpHeld">Jump is held: let go early for a shorter jump.</param>
public readonly record struct PanInput(float Move, bool Jump, bool JumpHeld);

/// <summary>What a power-up does: a cold drink heals, oven gloves stop burns for a while, a lid stops the pan heating, ice cools the chicken.</summary>
public enum Treat
{
    Drink,
    Gloves,
    Lid,
    Ice,
}

/// <summary>A power-up dropped into the pan: it lands on the floor or on chicken, and is taken by walking or jumping into it.</summary>
public sealed class PowerUp(int id, Treat treat, float x, float y)
{
    public const float Size = 26;

    public int Id { get; } = id;

    public Treat Treat { get; } = treat;

    public float X { get; internal set; } = x;

    public float Y { get; internal set; } = y;

    public float FallSpeed { get; internal set; }

    public bool Landed { get; internal set; }

    /// <summary>Seconds since it landed: it is gone after <see cref="HotPanGame.PowerUpLife"/>.</summary>
    public float Age { get; internal set; }
}

/// <summary>Something that happened, for the screen to show and sound.</summary>
public abstract record PanEvent;

public sealed record Jumped(float X, float Y) : PanEvent;

/// <summary>The cook came down on a piece of chicken (or the pan, when <paramref name="Chicken"/> is null).</summary>
public sealed record Landed(float X, float Y, Chicken? Chicken) : PanEvent;

public sealed record ChickenDropped(Chicken Chicken) : PanEvent;

public sealed record ChickenLanded(Chicken Chicken) : PanEvent;

/// <summary>A charred piece flicked out of the pan.</summary>
public sealed record ChickenCharred(Chicken Chicken) : PanEvent;

/// <summary>Feet started burning (not every frame of it: once per burn).</summary>
public sealed record StartedBurning(float X, float Y) : PanEvent;

public sealed record GameOver(int Score, TimeSpan Survived) : PanEvent;

public sealed record PowerUpDropped(PowerUp PowerUp) : PanEvent;

public sealed record PowerUpTaken(PowerUp PowerUp) : PanEvent;

/// <summary>A power-up left too long, gone.</summary>
public sealed record PowerUpLost(PowerUp PowerUp) : PanEvent;

/// <summary>The spatula turned a piece over, its cooler side up; the cook, if standing on it, is thrown up.</summary>
public sealed record ChickenFlipped(Chicken Chicken, bool CookThrown) : PanEvent;

/// <summary>Oil starts to bubble where it will spit, a moment before it does.</summary>
public sealed record OilBubbling(float X) : PanEvent;

/// <summary>Hot oil spits up; <paramref name="Hit"/> when it caught the cook.</summary>
public sealed record OilSpat(float X, bool Hit) : PanEvent;

/// <summary>
/// Hot Pan: you stand in a frying pan. Pieces of chicken drop in and warm up from the pan, and the pan itself gets
/// hotter as the game goes on. Standing on the pan, or on a piece that is too hot, burns your feet and costs health;
/// jump from piece to piece onto the ones still cool enough. At no health the game is over, and the longer you
/// lasted, the more points. The field is <see cref="Width"/> by <see cref="Height"/>, y down; everything is seeded,
/// so a seed always plays the same.
/// </summary>
public sealed class HotPanGame
{
    public const float Width = 800;
    public const float Height = 450;
    public const float FloorY = 400;
    public const float PanLeft = 50;
    public const float PanRight = 750;

    public const float FridgeTemperature = 4;
    public const float SafeTemperature = 55;
    public const float CharTemperature = 150;
    public const float StartPanTemperature = 150;
    public const float MaxHealth = 100;

    public const float CookWidth = 28;
    public const float CookHeight = 44;

    internal const float Gravity = 2200;
    internal const float JumpSpeed = 800;
    internal const float RunSpeed = 290;
    internal const float CoyoteTime = 0.1f;
    internal const float JumpBuffer = 0.12f;
    internal const float CharredLinger = 0.8f;
    internal const int MaxPieces = 8;

    public const float PowerUpLife = 9;
    public const float GlovesTime = 6;
    public const float LidTime = 8;
    public const float DrinkHealth = 30;
    public const float IceCooling = 45;
    public const float SpitWarning = 0.8f;
    public const float SpitReach = 36;
    public const float SpitDamage = 12;
    internal const float FirstPowerUp = 12;
    internal const float FirstFlip = 25;
    internal const float FirstSpit = 20;

    private readonly Random _random;
    private readonly List<Chicken> _chicken = [];
    private readonly List<PanEvent> _events = [];
    private int _nextId;
    private float _untilDrop;
    private float _sinceGround;
    private float _jumpWanted = float.MaxValue;
    private bool _burning;

    // The extras (power-ups, the spatula, oil) draw from their own numbers, so the chicken comes as it always did.
    private readonly Random _extras;
    private readonly List<PowerUp> _powerUps = [];
    private readonly List<(float X, float Left)> _bubbles = [];
    private float _untilPowerUp = FirstPowerUp;
    private float _untilFlip = FirstFlip;
    private float _untilSpit = FirstSpit;

    public HotPanGame(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
        _extras = new Random(unchecked((seed * 7919) + 17));
        CookX = Width / 2;
        CookY = FloorY;

        // A few pieces to start with, around the cook, so the first jump is easy.
        foreach (var x in new[] { 260f, 420f, 580f })
        {
            var piece = new Chicken(_nextId++, Pick(), x, 0) { Landed = true };
            piece.Y = FloorY - piece.Height;
            _chicken.Add(piece);
        }

        CookY = _chicken[1].Y;
        CookX = _chicken[1].X;
        StandingOn = _chicken[1];
        _untilDrop = 2.5f;
    }

    public int Seed { get; }

    /// <summary>The middle of the cook's feet.</summary>
    public float CookX { get; private set; }

    /// <summary>The bottom of the cook's feet.</summary>
    public float CookY { get; private set; }

    public float VelocityX { get; private set; }

    public float VelocityY { get; private set; }

    /// <summary>Which way the cook faces: 1 right, -1 left.</summary>
    public int Facing { get; private set; } = 1;

    public bool OnGround => _sinceGround == 0;

    /// <summary>The piece stood on, or null on the pan or in the air.</summary>
    public Chicken? StandingOn { get; private set; }

    public float Health { get; private set; } = MaxHealth;

    /// <summary>Whether the cook's feet are burning right now.</summary>
    public bool IsBurning => _burning;

    public float PanTemperature { get; private set; } = StartPanTemperature;

    public TimeSpan Time { get; private set; }

    public bool IsOver => Health <= 0;

    /// <summary>Ten points a second survived.</summary>
    public int Score => (int)(Time.TotalSeconds * 10);

    public IReadOnlyList<Chicken> Chicken => _chicken;

    public IReadOnlyList<PowerUp> PowerUps => _powerUps;

    /// <summary>Where oil is bubbling, about to spit, and how long until it does.</summary>
    public IReadOnlyList<(float X, float Left)> Bubbles => _bubbles;

    /// <summary>Seconds of oven gloves left: no burns while they last.</summary>
    public float GlovesLeft { get; private set; }

    /// <summary>Seconds of the lid left: the pan doesn't heat while it lasts.</summary>
    public float LidLeft { get; private set; }

    public bool IsProtected => GlovesLeft > 0;

    /// <summary>The seconds between pieces dropping in: three at first, down to one and a bit.</summary>
    public float DropInterval => MathF.Max(1.3f, 3f - ((float)Time.TotalSeconds * 0.015f));

    /// <summary>The events since the last call, oldest first.</summary>
    public IReadOnlyList<PanEvent> DrainEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    public void Update(TimeSpan elapsed, PanInput input)
    {
        if (IsOver)
        {
            return;
        }

        // Small steps, so fast falls never pass through a piece.
        var left = (float)Math.Min(elapsed.TotalSeconds, 0.1);
        while (left > 0 && !IsOver)
        {
            var dt = MathF.Min(left, 1 / 120f);
            Step(dt, input);
            input = input with { Jump = false };
            left -= dt;
        }
    }

    private void Step(float dt, PanInput input)
    {
        Time += TimeSpan.FromSeconds(dt);

        // The pan creeps hotter, a degree and a bit a second, and faster the longer it goes; not under the lid.
        GlovesLeft = MathF.Max(0, GlovesLeft - dt);
        LidLeft = MathF.Max(0, LidLeft - dt);
        if (LidLeft <= 0)
        {
            PanTemperature += (1.2f + ((float)Time.TotalSeconds * 0.01f)) * dt;
        }

        Heat(dt);
        Drop(dt);
        Fall(dt);
        MoveCook(dt, input);
        Extras(dt);
        Burn(dt);
    }

    private void Heat(float dt)
    {
        foreach (var piece in _chicken.Where(c => c.Landed).ToList())
        {
            piece.Temperature += piece.HeatRate * (PanTemperature - piece.Temperature) * dt;
            if (!piece.IsCharred)
            {
                continue;
            }

            piece.CharredFor += dt;
            if (piece.CharredFor >= CharredLinger)
            {
                _chicken.Remove(piece);
                if (StandingOn == piece)
                {
                    StandingOn = null;
                    _sinceGround = dt;
                }

                _events.Add(new ChickenCharred(piece));
            }
        }
    }

    private void Drop(float dt)
    {
        _untilDrop -= dt;
        if (_untilDrop > 0)
        {
            return;
        }

        _untilDrop = DropInterval;
        if (_chicken.Count >= MaxPieces)
        {
            return;
        }

        var cut = Pick();
        var probe = new Chicken(-1, cut, 0, 0);

        // A free place on the floor: not on another piece, and not right under the cook if it can be helped.
        for (var tries = 0; tries < 12; tries++)
        {
            var x = PanLeft + (probe.Width / 2) + ((float)_random.NextDouble() * (PanRight - PanLeft - probe.Width));
            if (_chicken.Any(c => MathF.Abs(c.X - x) < (c.Width + probe.Width) / 2 + 6))
            {
                continue;
            }

            if (tries < 8 && MathF.Abs(x - CookX) < (probe.Width / 2) + CookWidth)
            {
                continue;
            }

            var piece = new Chicken(_nextId++, cut, x, -probe.Height);
            _chicken.Add(piece);
            _events.Add(new ChickenDropped(piece));
            return;
        }
    }

    private Cut Pick() => _random.Next(10) switch
    {
        < 3 => Cut.Nugget,
        < 6 => Cut.Wing,
        < 8 => Cut.Drumstick,
        _ => Cut.Breast,
    };

    private void Fall(float dt)
    {
        foreach (var piece in _chicken.Where(c => !c.Landed))
        {
            piece.FallSpeed += Gravity * dt;
            piece.Y += piece.FallSpeed * dt;
            if (piece.Bottom < FloorY)
            {
                continue;
            }

            piece.Y = FloorY - piece.Height;
            piece.Landed = true;
            piece.FallSpeed = 0;
            _events.Add(new ChickenLanded(piece));

            // Landing under the cook's feet lifts them onto it.
            if (OnGround && StandingOn is null && CookX > piece.Left && CookX < piece.Right)
            {
                (CookY, StandingOn) = (piece.Y, piece);
            }
        }
    }

    private void MoveCook(float dt, PanInput input)
    {
        var move = Math.Clamp(input.Move, -1, 1);
        if (move != 0)
        {
            Facing = move > 0 ? 1 : -1;
        }

        // Quick to start and stop on the ground, a little floatier in the air.
        var target = move * RunSpeed;
        var grip = OnGround ? 18f : 8f;
        VelocityX += (target - VelocityX) * MathF.Min(1, grip * dt);

        _jumpWanted = input.Jump ? 0 : _jumpWanted + dt;
        if (!OnGround)
        {
            _sinceGround += dt;
        }

        // A jump pressed just before landing, or just after running off an edge, still counts.
        if (_jumpWanted <= JumpBuffer && _sinceGround <= CoyoteTime)
        {
            VelocityY = -JumpSpeed;
            _sinceGround = CoyoteTime + dt;
            _jumpWanted = float.MaxValue;
            StandingOn = null;
            _events.Add(new Jumped(CookX, CookY));
        }

        if (!input.JumpHeld && VelocityY < -JumpSpeed * 0.45f)
        {
            VelocityY = -JumpSpeed * 0.45f;
        }

        // Across: blocked by the pan's sides and by pieces taller than the feet.
        var x = Math.Clamp(CookX + (VelocityX * dt), PanLeft + (CookWidth / 2), PanRight - (CookWidth / 2));
        foreach (var piece in _chicken.Where(c => c.Landed && CookY > c.Y + 2))
        {
            var reach = (piece.Width + CookWidth) / 2;
            if (MathF.Abs(x - piece.X) < reach)
            {
                x = CookX <= piece.X ? piece.X - reach : piece.X + reach;
                VelocityX = 0;
            }
        }

        CookX = x;

        // Standing: still on the piece, or walked off it?
        if (OnGround)
        {
            if (StandingOn is { } under && (CookX < under.Left || CookX > under.Right))
            {
                StandingOn = null;
                _sinceGround = dt;
            }

            if (OnGround)
            {
                VelocityY = 0;
                CookY = StandingOn?.Y ?? FloorY;
                return;
            }
        }

        // In the air: fall, and land on the first top or the floor crossed on the way down.
        VelocityY += Gravity * dt;
        var y = CookY + (VelocityY * dt);
        if (VelocityY > 0)
        {
            var top = _chicken
                .Where(c => c.Landed && CookX >= c.Left && CookX <= c.Right && CookY <= c.Y + 0.5f && y >= c.Y)
                .OrderBy(c => c.Y)
                .FirstOrDefault();
            if (top is not null || y >= FloorY)
            {
                CookY = top?.Y ?? FloorY;
                StandingOn = top;
                VelocityY = 0;
                _sinceGround = 0;
                _events.Add(new Landed(CookX, CookY, top));
                return;
            }
        }

        CookY = y;
    }

    /// <summary>Power-ups dropping, landing, taken and lost; the spatula; oil bubbling and spitting.</summary>
    private void Extras(float dt)
    {
        _untilPowerUp -= dt;
        if (_untilPowerUp <= 0)
        {
            _untilPowerUp = 12 + ((float)_extras.NextDouble() * 8);
            var treat = (Treat)_extras.Next(4);
            DropPowerUp(treat, PanLeft + PowerUp.Size + ((float)_extras.NextDouble() * (PanRight - PanLeft - (2 * PowerUp.Size))));
        }

        foreach (var item in _powerUps.ToList())
        {
            if (!item.Landed)
            {
                // It falls onto the first chicken top or the floor below it.
                item.FallSpeed += Gravity * dt;
                var y = item.Y + (item.FallSpeed * dt);
                var floor = _chicken.Where(c => c.Landed && item.X >= c.Left && item.X <= c.Right && item.Y <= c.Y).Select(c => c.Y).DefaultIfEmpty(FloorY).Min();
                (item.Y, item.Landed) = y >= floor ? (floor, true) : (y, false);
            }
            else
            {
                item.Age += dt;
                if (!_chicken.Any(c => c.Landed && item.X >= c.Left && item.X <= c.Right && MathF.Abs(c.Y - item.Y) < 1) && item.Y < FloorY)
                {
                    // What it sat on went (charred and flicked out): it drops again.
                    item.Landed = false;
                }
            }

            if (MathF.Abs(item.X - CookX) < (PowerUp.Size + CookWidth) / 2 && item.Y > CookY - CookHeight - PowerUp.Size && item.Y - PowerUp.Size < CookY)
            {
                Take(item);
            }
            else if (item.Age > PowerUpLife)
            {
                _powerUps.Remove(item);
                _events.Add(new PowerUpLost(item));
            }
        }

        // The spatula: now and then it turns over the hottest piece that isn't charred yet, cooler side up.
        _untilFlip -= dt;
        if (_untilFlip <= 0)
        {
            _untilFlip = 14 + ((float)_extras.NextDouble() * 10);
            Flip(dt);
        }

        // Oil: it bubbles somewhere first, then spits; the cook near it gets splashed, unless gloved.
        _untilSpit -= dt;
        if (_untilSpit <= 0)
        {
            _untilSpit = MathF.Max(3, 9 - ((float)Time.TotalSeconds * 0.02f)) + ((float)_extras.NextDouble() * 4);
            Bubble(PanLeft + 30 + ((float)_extras.NextDouble() * (PanRight - PanLeft - 60)));
        }

        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var (x, left) = _bubbles[i];
            left -= dt;
            if (left > 0)
            {
                _bubbles[i] = (x, left);
                continue;
            }

            _bubbles.RemoveAt(i);
            var hit = MathF.Abs(CookX - x) < SpitReach && CookY > FloorY - 140 && !IsProtected;
            if (hit)
            {
                Health = MathF.Max(0, Health - SpitDamage);
                if (Health <= 0)
                {
                    _events.Add(new GameOver(Score, Time));
                }
            }

            _events.Add(new OilSpat(x, hit));
        }
    }

    internal PowerUp DropPowerUp(Treat treat, float x)
    {
        var item = new PowerUp(_nextId++, treat, x, -PowerUp.Size);
        _powerUps.Add(item);
        _events.Add(new PowerUpDropped(item));
        return item;
    }

    internal void Bubble(float x)
    {
        _bubbles.Add((x, SpitWarning));
        _events.Add(new OilBubbling(x));
    }

    /// <summary>The spatula turns the hottest piece not yet charred; false when none is too hot.</summary>
    internal bool Flip(float dt = 0)
    {
        if (_chicken.Where(c => c.Landed && c.IsTooHot && !c.IsCharred).MaxBy(c => c.Temperature) is not { } piece)
        {
            return false;
        }

        piece.Temperature = MathF.Max(FridgeTemperature, piece.Temperature * 0.55f);
        var thrown = StandingOn == piece && OnGround;
        if (thrown)
        {
            StandingOn = null;
            VelocityY = -JumpSpeed * 0.7f;
            _sinceGround = CoyoteTime + dt;
        }

        _events.Add(new ChickenFlipped(piece, thrown));
        return true;
    }

    private void Take(PowerUp item)
    {
        _powerUps.Remove(item);
        switch (item.Treat)
        {
            case Treat.Drink:
                Health = MathF.Min(MaxHealth, Health + DrinkHealth);
                break;
            case Treat.Gloves:
                GlovesLeft = GlovesTime;
                break;
            case Treat.Lid:
                LidLeft = LidTime;
                break;
            default:
                foreach (var piece in _chicken.Where(c => !c.IsCharred))
                {
                    piece.Temperature = MathF.Max(FridgeTemperature, piece.Temperature - IceCooling);
                }

                break;
        }

        _events.Add(new PowerUpTaken(item));
    }

    private void Burn(float dt)
    {
        // Only feet on something too hot burn: the pan always is; chicken once it passes the safe temperature. Not in gloves.
        float? surface = !OnGround || IsProtected ? null : StandingOn is { } piece ? piece.Temperature : PanTemperature;
        var burning = surface > SafeTemperature;
        if (burning && !_burning)
        {
            _events.Add(new StartedBurning(CookX, CookY));
        }

        _burning = burning;
        if (!burning)
        {
            return;
        }

        // From a sting on warm chicken to a lot on the bare pan.
        var heat = surface!.Value - SafeTemperature;
        Health = MathF.Max(0, Health - ((8 + (heat * 0.18f)) * dt));
        if (Health <= 0)
        {
            _events.Add(new GameOver(Score, Time));
        }
    }
}
