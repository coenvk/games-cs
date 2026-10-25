using Jentl.Core.Tracks;
using Jentl.Tracks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;

namespace Games.Race;

/// <summary>
/// A track drawn from above for one condition: the road (Jentl's road painter) with scenery around it, and a small
/// map of the whole track. World units are pixels, <see cref="PixelsPerMetre"/> to the metre.
/// </summary>
internal sealed class TrackArt : IDisposable
{
    /// <summary>World pixels per metre; cars (18 pixels for about 4.5 m) are drawn 1:1 at this scale.</summary>
    public const float PixelsPerMetre = 2.5f;

    private const int RoadWidth = 32;

    private readonly uint[] _pixels;
    private Jentl.Raster.Texture? _picture;

    private TrackArt(TrackPath path, Texture2D world, uint[] pixels, Rectangle bounds, IReadOnlyList<Prop> props, Texture2D map, Func<Vector2, Vector2> toMap, Color ground)
    {
        Path = path;
        World = world;
        _pixels = pixels;
        Props = props;
        Bounds = bounds;
        Map = map;
        ToMap = toMap;
        Ground = ground;
    }

    /// <summary>The track's centre line in world pixels.</summary>
    public TrackPath Path { get; }

    public Texture2D World { get; }

    /// <summary>The trees, rocks and buildings painted into <see cref="World"/>, for standing them up in 3D.</summary>
    public IReadOnlyList<Prop> Props { get; }

    /// <summary><see cref="World"/> for the software renderer, made when first asked for.</summary>
    public Jentl.Raster.Texture Picture => _picture ??= new Jentl.Raster.Texture(Bounds.Width, Bounds.Height, _pixels) { Wrap = Jentl.Raster.TextureWrap.Clamp };

    /// <summary>Where <see cref="World"/> lies in world pixels.</summary>
    public Rectangle Bounds { get; }

    /// <summary>The colour of the ground around the road, for beyond the painted area.</summary>
    public Color Ground { get; }

    /// <summary>The whole track, small.</summary>
    public Texture2D Map { get; }

    /// <summary>A world position on the map, in map pixels.</summary>
    public Func<Vector2, Vector2> ToMap { get; }

    /// <summary>Half the road's width, for placing the two cars side by side.</summary>
    public static float Lane => RoadWidth / 4f;

    public static TrackArt Build(GraphicsDevice device, TrackDef track, Condition condition)
    {
        var path = TrackPath.Build([.. track.Pieces.Select(p => p.Radius > 0
            ? (PathSegment)new Arc(p.Radius * PixelsPerMetre, p.Degrees)
            : new Straight(p.Length * PixelsPerMetre))]);
        var style = StyleFor(track.Kind, condition);
        var pixels = RoadPainter.Paint(path, style, padding: 90, out var bounds);
        var props = TrackScenery.Decorate(pixels, bounds, style, track, condition);
        var world = RoadPainter.ToTexture(device, pixels, bounds);
        var (map, toMap) = DrawMap(device, path);
        return new TrackArt(path, world, [.. pixels.Select(p => p.PackedValue)], bounds, props, map, toMap, style.Ground);
    }

    /// <summary>Where a car is: along the centre line, <paramref name="lane"/> pixels to the left of it (negative: right).</summary>
    public (Vector2 Position, float Heading) Pose(double metres, float lane)
    {
        var pose = Path.At(metres * PixelsPerMetre);
        var (sin, cos) = MathF.SinCos(pose.Heading);
        var left = new Vector2(sin, -cos);
        return (new Vector2(pose.Position.X, pose.Position.Y) + (left * lane), pose.Heading);
    }

    public void Dispose()
    {
        World.Dispose();
        Map.Dispose();
    }

    private static RoadStyle StyleFor(TrackKind kind, Condition condition)
    {
        var (ground, verge, surface) = condition switch
        {
            Condition.Wet => (new Color(52, 92, 50), new Color(150, 136, 104), new Color(58, 60, 70)),
            Condition.Dirt => (new Color(70, 104, 56), new Color(120, 92, 60), new Color(130, 96, 62)),
            Condition.Gravel => (new Color(84, 110, 66), new Color(150, 140, 120), new Color(160, 150, 132)),
            Condition.Snow => (new Color(236, 241, 246), new Color(208, 216, 226), new Color(196, 204, 216)),
            Condition.Ice => (new Color(232, 238, 246), new Color(200, 214, 228), new Color(168, 206, 232)),
            Condition.Sand => (new Color(226, 204, 150), new Color(210, 184, 128), new Color(200, 172, 116)),
            _ => (new Color(61, 107, 56), new Color(201, 180, 138), new Color(75, 76, 85)),
        };
        if (kind == TrackKind.City)
        {
            (ground, verge) = (new Color(120, 120, 128), new Color(160, 160, 166));
        }

        var paved = condition is Condition.Dry or Condition.Wet;
        return new RoadStyle(
            ground, verge, surface,
            EdgeA: paved ? new Color(210, 58, 48) : surface,
            EdgeB: paved ? new Color(238, 238, 238) : surface,
            CentreLine: paved ? new Color(230, 230, 220) : surface,
            Width: RoadWidth, VergeWidth: 6, KerbLength: 6);
    }

    private static (Texture2D Map, Func<Vector2, Vector2> ToMap) DrawMap(GraphicsDevice device, TrackPath path)
    {
        var map = RoadPainter.PaintMap(path, 120, new Color(0, 0, 0, 140), new Color(190, 190, 200));
        var texture = new Texture2D(device, map.Size.X, map.Size.Y);
        texture.SetData(map.Pixels);
        return (texture, map.ToMap);
    }
}
