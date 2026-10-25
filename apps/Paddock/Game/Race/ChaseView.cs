using Jentl.Core.Raster;
using Jentl.Geometry;
using Jentl.Raster;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using Numerics = System.Numerics;
using ParticleEffect = Jentl.Core.Graphics.ParticleEffect;

namespace Games.Race;

/// <summary>
/// A round on a circuit, road or trail in 3D from just behind the player's car, drawn by the software renderer at a
/// low resolution and scaled up, so it keeps Paddock's pixel look. The painted track is the ground, rocks and
/// buildings on it stand up and trees are pixel-art cut-outs turned to the camera, the cars are their side sprites
/// made solid, everything casts a blob shadow, and the distance fades into a sky of the weather's colours.
/// World units are metres: x east and z south as the painted track runs, y up.
/// </summary>
internal sealed class ChaseView(TrackArt art, Jentl.Raster.Texture player, Jentl.Raster.Texture opponent, Condition condition) : IDisposable
{
    private const int Height = 180;
    private const float Behind = 8;
    private const float EyeHeight = 3;
    private const float Ahead = 8;
    private const float FogStart = 30;
    private const float FogEnd = 95;

    // Side sprites are drawn 48 pixels for about 4.6 m of car; cars are about 1.75 m wide.
    private const float CarPixel = 4.6f / 48;
    private const float CarWidth = 1.75f;

    // Soft daylight: the sprites are painted already, so the light only sets the faces apart.
    private static readonly Lighting Light = new(new Numerics.Vector3(-0.4f, -1, 0.3f), new Numerics.Vector3(0.45f), new Numerics.Vector3(0.7f));

    private static readonly Mesh Cutout = MeshBuilder.Quad();
    private static readonly Mesh Patch = MeshBuilder.Plane(1, 1);
    private static readonly Material Shadow = new(Numerics.Vector4.One, Shading.Unlit, Textures.Spot(16, new Numerics.Vector4(0, 0, 0, 0.35f), softness: 0.3f), Translucent: true);
    private static readonly Mesh Rock = MeshBuilder.Sphere(1, segments: 6, rings: 3);
    private static readonly Mesh Block = MeshBuilder.Box(Numerics.Vector3.One);

    private readonly Mesh _ground = Ground(art);
    // The sprites have a dark border; the cars' sides take the paint just inside it.
    private readonly Mesh _player = Models.FromPicture(player, CarPixel, CarWidth, sideInset: 1.5f);
    private readonly Mesh _opponent = Models.FromPicture(opponent, CarPixel, CarWidth, sideInset: 1.5f);
    private readonly Material _groundMaterial = new(Numerics.Vector4.One, Shading.Unlit, art.Picture);
    private readonly Material _landMaterial = new(Of(art.Ground), Shading.Unlit);
    private readonly Material _playerMaterial = new(Numerics.Vector4.One, Shading.Flat, player);
    private readonly Material _opponentMaterial = new(Numerics.Vector4.One, Shading.Flat, opponent);
    private readonly Sky _sky = SkyFor(condition);
    private readonly (Prop Prop, Numerics.Vector3 At, Material Material)[] _props = Props(art, condition);
    private readonly Camera _camera = new() { FieldOfView = MathF.PI / 3, Near = 0.3f, Far = FogEnd + 10 };
    private readonly ParticleEffect? _trail = Trails.For(condition);
    private readonly Particles _spray = new(capacity: 600) { Gravity = 9, Drag = 1.5f, Floor = 0 };
    private readonly Random _random = new(1);
    private (double Player, double Opponent)? _last;
    private SoftwareView? _view;

    /// <summary>The picture, after <see cref="Render"/>.</summary>
    public Texture2D? Picture => _view?.Texture;

    /// <summary>Draws the 3D picture; call while the sprite batch is not begun (in <c>Scene.DrawLayers</c>).</summary>
    public void Render(GraphicsDevice device, Rectangle viewport, double playerMetres, double opponentMetres)
    {
        // As tall as the pixel look wants, as wide as the screen's shape.
        var width = Math.Clamp((int)MathF.Round(Height * viewport.Width / (float)Math.Max(1, viewport.Height)), 120, 420);
        if (_view is null || _view.Size != new Point(width, Height))
        {
            _view?.Dispose();
            _view = new SoftwareView(device, width, Height) { Fog = new Fog(_sky.Horizon, FogStart, FogEnd), Lighting = Light };
        }

        var (eye, forward) = CameraAt(playerMetres);
        (_camera.Position, _camera.Target) = (eye, Spot(playerMetres + Ahead, TrackArt.Lane, 1));
        _view.Look(_camera);
        _view.Begin(_sky);

        // The land round the painted area, then the painted track just above it.
        var centre = new Numerics.Vector3(Metres(art.Bounds.Center.X), 0, Metres(art.Bounds.Center.Y));
        var reach = (MathF.Max(art.Bounds.Width, art.Bounds.Height) / TrackArt.PixelsPerMetre) + (2 * FogEnd);
        _view.DrawMesh(Patch, Numerics.Matrix4x4.CreateScale(reach, 1, reach) * Numerics.Matrix4x4.CreateTranslation(centre - new Numerics.Vector3(0, 0.05f, 0)), _landMaterial);
        _view.DrawMesh(_ground, Numerics.Matrix4x4.Identity, _groundMaterial);

        // Shadows on the ground first (they write no depth, so what stands on them covers them), then what casts them.
        // Only what is near enough to show through the fog, and not behind the camera.
        bool Near(Numerics.Vector3 at, float size)
        {
            var to = at - eye;
            return to.LengthSquared() < FogEnd * FogEnd && Numerics.Vector3.Dot(to, forward) > -size;
        }

        foreach (var (prop, at, _) in _props)
        {
            if (prop.Kind == PropKind.Tree && Near(at, 4))
            {
                var spread = Metres(prop.Area.Width) * 1.1f;
                _view.DrawMesh(Patch, Scaled(new Numerics.Vector3(spread, 1, spread), at + new Numerics.Vector3(0, 0.03f, 0)), Shadow);
            }
        }

        CarShadow(_view, opponentMetres, -TrackArt.Lane);
        CarShadow(_view, playerMetres, TrackArt.Lane);
        foreach (var (prop, at, material) in _props)
        {
            if (Near(at, Metres(Math.Max(prop.Area.Width, prop.Area.Height))))
            {
                DrawProp(_view, prop, at, eye, material);
            }
        }

        DrawCar(_view, _opponent, _opponentMaterial, opponentMetres, -TrackArt.Lane);
        DrawCar(_view, _player, _playerMaterial, playerMetres, TrackArt.Lane);
        _view.DrawParticles(_spray);
        _view.Finish();
    }

    /// <summary>Throws spray or grit up behind moving cars and moves it on; call once per frame with the real elapsed time.</summary>
    public void Update(double playerMetres, double opponentMetres, TimeSpan elapsed)
    {
        if (_trail is { } trail && _last is var (lastPlayer, lastOpponent))
        {
            Throw(trail, playerMetres, playerMetres - lastPlayer, elapsed, TrackArt.Lane);
            Throw(trail, opponentMetres, opponentMetres - lastOpponent, elapsed, -TrackArt.Lane);
        }

        _spray.Update((float)elapsed.TotalSeconds);
        _last = (playerMetres, opponentMetres);
    }

    /// <summary>The last picture, filling <paramref name="viewport"/> with square pixels.</summary>
    public void Draw(SpriteBatch batch, Rectangle viewport)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_view is null)
        {
            return;
        }

        var texture = _view.Texture;
        var scale = MathF.Max(viewport.Width / (float)texture.Width, viewport.Height / (float)texture.Height);
        var size = new Point((int)MathF.Ceiling(texture.Width * scale), (int)MathF.Ceiling(texture.Height * scale));
        var at = new Rectangle(viewport.Center.X - (size.X / 2), viewport.Center.Y - (size.Y / 2), size.X, size.Y);
        batch.End();
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.PointClamp);
        batch.Draw(texture, at, Color.White);
        batch.End();
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
    }

    public void Dispose() => _view?.Dispose();

    // Weather in the sky, deeper overhead than at the horizon: grey with rain, pale with snow and ice, hazy over sand.
    private static Sky SkyFor(Condition condition)
    {
        var (zenith, horizon) = condition switch
        {
            Condition.Wet => (new Color(78, 90, 110), new Color(126, 140, 160)),
            Condition.Snow or Condition.Ice => (new Color(150, 166, 188), new Color(204, 214, 226)),
            Condition.Sand => (new Color(96, 146, 210), new Color(206, 210, 206)),
            _ => (new Color(62, 116, 206), new Color(168, 198, 232)),
        };
        return new Sky(Of(zenith), Of(horizon));
    }

    // Each prop with where it stands and what it is made of: trees a pixel-art cut-out in their leaves' colour (one
    // picture per colour), the rest flat colour.
    private static (Prop Prop, Numerics.Vector3 At, Material Material)[] Props(TrackArt art, Condition condition)
    {
        var trees = new Dictionary<Color, Material>();
        return [.. art.Props.Select(p =>
        {
            var material = p.Kind == PropKind.Tree
                ? trees.TryGetValue(p.Color, out var tree) ? tree : trees[p.Color] = new Material(Numerics.Vector4.One, Shading.Unlit, TreePicture(p.Color, condition == Condition.Snow))
                : new Material(Of(p.Color), Shading.Flat);
            return (p, new Numerics.Vector3(Metres(p.Area.Center.X), 0, Metres(p.Area.Center.Y)), material);
        })];
    }

    // A pine 16 by 32 pixels: three tiers of branches, lit from the left with a dark edge, on a brown trunk; snow
    // lies along the bottom of each tier.
    private static Jentl.Raster.Texture TreePicture(Color leaves, bool snow)
    {
        const int W = 16, H = 32;
        var pixels = new uint[W * H];
        var (light, dark, edge) = (Color.Lerp(leaves, Color.White, 0.18f), Color.Lerp(leaves, Color.Black, 0.25f), Color.Lerp(leaves, Color.Black, 0.5f));
        (int Top, int Bottom, float Half)[] tiers = [(0, 12, 4.5f), (7, 20, 6.5f), (14, 27, 8)];
        bool Inside(int x, int y)
        {
            foreach (var (top, bottom, half) in tiers)
            {
                if (y >= top && y <= bottom && MathF.Abs(x + 0.5f - (W / 2f)) <= half * (y - top + 1) / (bottom - top + 1))
                {
                    return true;
                }
            }

            return false;
        }

        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                Color? c = null;
                if (Inside(x, y))
                {
                    var rim = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y + 1);
                    var onSnow = snow && !Inside(x, y + 1);
                    c = onSnow ? new Color(236, 241, 246) : rim ? edge : x < W / 2 ? light : dark;
                }
                else if (y >= 27 && x is 7 or 8)
                {
                    c = x == 7 ? new Color(110, 78, 50) : new Color(84, 58, 38);
                }

                pixels[(y * W) + x] = c?.PackedValue ?? 0;
            }
        }

        return new Jentl.Raster.Texture(W, H, pixels) { Wrap = TextureWrap.Clamp };
    }

    // The painted track as a flat grid over its area.
    private static Mesh Ground(TrackArt art)
    {
        var (width, depth) = (art.Bounds.Width / TrackArt.PixelsPerMetre, art.Bounds.Height / TrackArt.PixelsPerMetre);
        var centre = new Numerics.Vector3(art.Bounds.Center.X / TrackArt.PixelsPerMetre, 0, art.Bounds.Center.Y / TrackArt.PixelsPerMetre);
        return MeshBuilder.Plane(width, depth, 8, 8).Transform(Numerics.Matrix4x4.CreateTranslation(centre));
    }

    private static float Metres(float pixels) => pixels / TrackArt.PixelsPerMetre;

    private static Numerics.Vector4 Of(Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    // A point in a lane at `metres` along the track, `height` above the ground.
    private Numerics.Vector3 Spot(double metres, float lane, float height)
    {
        var (position, _) = art.Pose(metres, lane);
        return new Numerics.Vector3(Metres(position.X), height, Metres(position.Y));
    }

    // Behind the car along the track, so the camera follows the bends; before the start of an open road, straight back.
    private (Numerics.Vector3 Eye, Numerics.Vector3 Forward) CameraAt(double metres)
    {
        var back = metres - Behind;
        Numerics.Vector3 eye;
        if (back < 0 && !art.Path.IsClosed)
        {
            var (_, heading) = art.Pose(0, TrackArt.Lane);
            var (sin, cos) = MathF.SinCos(heading);
            eye = Spot(0, TrackArt.Lane, EyeHeight) - (new Numerics.Vector3(cos, 0, sin) * (float)-back);
        }
        else
        {
            eye = Spot(back, TrackArt.Lane, EyeHeight);
        }

        var forward = Spot(metres + Ahead, TrackArt.Lane, EyeHeight) - eye;
        return (eye, forward.LengthSquared() > 0 ? Numerics.Vector3.Normalize(forward) : Numerics.Vector3.UnitX);
    }

    // From both rear wheels, more the faster the car, thrown up and back but carried along at most of its speed (as
    // spray is), in the trail's colours.
    private void Throw(ParticleEffect trail, double metres, double moved, TimeSpan elapsed, float lane)
    {
        if (moved <= 0 || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        var (position, heading) = art.Pose(metres, lane);
        var (sin, cos) = MathF.SinCos(heading);
        var forward = new Numerics.Vector3(cos, 0, sin);
        var side = new Numerics.Vector3(-sin, 0, cos);
        var rear = new Numerics.Vector3(Metres(position.X), 0.2f, Metres(position.Y)) - (forward * 1.6f);
        var speed = (float)(moved / elapsed.TotalSeconds);
        var count = Math.Min(12, (int)Math.Ceiling(Math.Min(speed * 4, 150) * elapsed.TotalSeconds));
        for (var i = 0; i < count; i++)
        {
            var wheel = rear + (side * (_random.Next(2) == 0 ? -0.75f : 0.75f));
            var velocity = (forward * ((speed * Between(0.5f, 0.8f)) - Between(1, 4))) + (side * Between(-1, 1)) + new Numerics.Vector3(0, Between(1.5f, 3.5f), 0);
            var color = trail.Colors[_random.Next(trail.Colors.Count)];
            _spray.Emit(wheel, velocity, Between(0.4f, 0.9f), Between(0.15f, 0.3f), Of(color));
        }
    }

    private float Between(float low, float high) => low + ((high - low) * _random.NextSingle());

    // The model's front is +x; turned to the car's heading (x east, z south, as the track's pixels run).
    private void DrawCar(SoftwareView view, Mesh car, Material material, double metres, float lane)
    {
        var (position, heading) = art.Pose(metres, lane);
        var world = Numerics.Matrix4x4.CreateRotationY(-heading) * Numerics.Matrix4x4.CreateTranslation(Metres(position.X), 0, Metres(position.Y));
        view.DrawMesh(car, world, material);
    }

    // A blob under the car, stretched along it.
    private void CarShadow(SoftwareView view, double metres, float lane)
    {
        var (position, heading) = art.Pose(metres, lane);
        var world = Numerics.Matrix4x4.CreateScale(4.8f, 1, 2.2f) * Numerics.Matrix4x4.CreateRotationY(-heading)
            * Numerics.Matrix4x4.CreateTranslation(Metres(position.X), 0.04f, Metres(position.Y));
        view.DrawMesh(Patch, world, Shadow);
    }

    private static void DrawProp(SoftwareView view, Prop prop, Numerics.Vector3 at, Numerics.Vector3 eye, Material material)
    {
        var (w, d) = (Metres(prop.Area.Width), Metres(prop.Area.Height));
        switch (prop.Kind)
        {
            case PropKind.Tree:
                // Twice as tall as wide, as the picture is.
                view.DrawMesh(Cutout, Billboard.Upright(at, eye, w * 1.2f, w * 2.4f), material);
                break;
            case PropKind.Rock:
                view.DrawMesh(Rock, Scaled(new Numerics.Vector3(w / 2, w / 4, d / 2), at), material);
                break;
            case PropKind.Building:
                // Taller the bigger the roof, a storey or three.
                var storeys = 3 + ((prop.Area.Width + prop.Area.Height) % 3 * 3);
                view.DrawMesh(Block, Scaled(new Numerics.Vector3(w, storeys, d), at + new Numerics.Vector3(0, storeys / 2f, 0)), material);
                break;
        }
    }

    private static Numerics.Matrix4x4 Scaled(Numerics.Vector3 scale, Numerics.Vector3 at) =>
        Numerics.Matrix4x4.CreateScale(scale) * Numerics.Matrix4x4.CreateTranslation(at);
}
