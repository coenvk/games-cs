using System.Numerics;
using Jentl.Physics;

namespace Bricks;

/// <summary>Links an entity to its body in the physics world (the body's <c>UserData</c> links back).</summary>
public readonly record struct Physical(Body Body);

/// <summary>The player's bat. <see cref="WideFor"/> counts down the wide power-up.</summary>
public record struct Paddle(float Width, TimeSpan WideFor);

/// <summary>A ball; a stuck one rides on the paddle at <see cref="Offset"/> until launched.</summary>
public record struct Ball(bool Stuck, float Offset);

/// <summary>A brick in row <see cref="Row"/> (its colour); unbreakable ones have no hits to lose.</summary>
public record struct Brick(int Hits, int Points, int Row, bool Unbreakable);

public enum PowerKind
{
    /// <summary>A wider paddle for a while.</summary>
    Wide,

    /// <summary>Two more balls.</summary>
    Multi,

    /// <summary>An extra life.</summary>
    Life,
}

/// <summary>A falling capsule; catching it with the paddle gives its power.</summary>
public readonly record struct PowerUp(PowerKind Kind);

/// <summary>One of the field's three walls.</summary>
public readonly record struct Wall;

/// <summary>Collision layers.</summary>
internal static class Layers
{
    public const uint Ball = 1;
    public const uint Brick = 2;
    public const uint Wall = 4;
    public const uint Paddle = 8;
    public const uint PowerUp = 16;
}

/// <summary>Things that happened, for the screen to show and sound: emitted as ECS events.</summary>
public abstract record BricksEvent;

public sealed record BrickHit(Vector2 Position, int Row, bool Broken) : BricksEvent;

public sealed record PaddleBounce(Vector2 Position) : BricksEvent;

public sealed record WallBounce(Vector2 Position) : BricksEvent;

public sealed record PowerCaught(PowerKind Kind, Vector2 Position) : BricksEvent;

public sealed record BallLost : BricksEvent;

public sealed record LifeLost(int LivesLeft) : BricksEvent;

public sealed record LevelCleared(int Level) : BricksEvent;

public sealed record GameOver(int Score) : BricksEvent;
