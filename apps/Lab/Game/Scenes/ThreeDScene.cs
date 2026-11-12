using System.Diagnostics;
using System.Globalization;
using Games.Input;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Raster;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Geometry;
using Jentl.Raster;
using Jentl.Scene3D;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Numerics = System.Numerics;
using RasterTexture = Jentl.Raster.Texture;

namespace Games.Scenes;

/// <summary>
/// A low-poly car driving round a field, a windmill turning and a lamp glowing, built as a tree of nodes and drawn by
/// the software renderer (320×180, scaled up) or on the GPU (full size): switch with Enter or a tap, and compare the
/// time each takes to draw. Drag to turn the camera round the field, scroll to zoom, and click a thing to name it.
/// </summary>
internal sealed class ThreeDScene : Scene
{
    private const int SoftwareWidth = 320;
    private const int SoftwareHeight = 180;

    private static readonly Color Sky = new(115, 153, 217);

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly Mesh _ground = MeshBuilder.Plane(14, 14, 7, 7);
    private readonly Mesh _body = MeshBuilder.Extrude(
        [new(-2, 0.3f), new(2, 0.3f), new(2, 0.9f), new(1.2f, 1.0f), new(0.6f, 1.5f), new(-0.8f, 1.5f), new(-1.4f, 1.0f), new(-2, 0.9f)], 1.6f);

    private readonly Mesh _wheel = MeshBuilder.Cylinder(0.35f, 0.3f, 12).Transform(Numerics.Matrix4x4.CreateRotationX(MathF.PI / 2));
    private readonly Mesh _ball = MeshBuilder.Sphere(0.8f, 16, 10);
    private readonly Mesh _crate = MeshBuilder.Box(new Numerics.Vector3(1.2f));
    private readonly RasterTexture _grass = DemoTextures.Grass();
    private readonly RasterTexture _wood = DemoTextures.Crate();
    private readonly RasterTexture _paint = DemoTextures.CarSide();
    private readonly Stopwatch _clock = new();
    private readonly Node _world = new("world");
    private readonly Node _car;
    private readonly List<Node> _wheels = [];
    private readonly AnimationPlayer _windmill;
    private readonly Node _stalk;
    private readonly Node[] _bones = new Node[4];
    private readonly OrbitCamera _orbit = new() { Target = new Numerics.Vector3(0, 0.5f, 0), Yaw = 0.6f, Pitch = 0.35f, Distance = 9, MinDistance = 3, MaxDistance = 30, MinPitch = 0.05f };
    private readonly Camera _camera = new() { FieldOfView = MathF.PI / 3.5f, Near = 0.1f, Far = 100 };
    private readonly RenderList _list = new();
    private Vector2? _dragFrom;
    private Rectangle _picture;
    private string _picked = "nothing";
    private IMeshView? _view;
    private bool _gpu;
    private bool _affine;
    private double _angle;
    private double _drawMs;
    private double _finishMs;
    private double _frameMs;

    public ThreeDScene(GameContext context)
    {
        _context = context;
        _buttons = new ButtonBar();
        SetButtons();

        var dark = new Material(new Numerics.Vector4(0.1f, 0.1f, 0.12f, 1));
        _world.Add("field", _ground, new Material(Numerics.Vector4.One, Shading.Flat, _grass));
        var crate = _world.Add("crate", _crate, new Material(Numerics.Vector4.One, Shading.Flat, _wood));
        (crate.Position, crate.Rotation) = (new Numerics.Vector3(2.8f, 0.6f, -1.5f), Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitY, 0.4f));
        _world.Add("ball", _ball, new Material(new Numerics.Vector4(0.9f, 0.8f, 0.2f, 1)) { Specular = new Numerics.Vector3(0.9f), Shininess = 40 })
            .Position = new Numerics.Vector3(-3, 0.8f, -2);

        // The car's body faces +X as built; a child turns it to face +Z, the way nodes go.
        _car = _world.Add(new Node("car"));
        var body = _car.Add("body", _body, new Material(Numerics.Vector4.One, Shading.Flat, _paint));
        body.Rotation = Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitY, -MathF.PI / 2);
        foreach (var (x, z) in new[] { (-1.2f, 0.8f), (1.2f, 0.8f), (-1.2f, -0.8f), (1.2f, -0.8f) })
        {
            var wheel = body.Add($"wheel {_wheels.Count + 1}", _wheel, dark);
            wheel.Position = new Numerics.Vector3(x, 0.35f, z);
            _wheels.Add(wheel);
        }

        var lamp = _world.Add("lamp post", MeshBuilder.Cylinder(0.06f, 2.2f, 8), dark);
        lamp.Position = new Numerics.Vector3(-1.5f, 1.1f, 3.2f);
        lamp.Add("bulb", MeshBuilder.Sphere(0.18f, 10, 6), new Material(new Numerics.Vector4(0.2f, 0.2f, 0.1f, 1)) { Emissive = new Numerics.Vector3(1, 0.9f, 0.5f) })
            .Position = new Numerics.Vector3(0, 1.2f, 0);

        // A windmill whose sails turn by an animation: a quarter turn per key, round and round.
        var mill = _world.Add("windmill", MeshBuilder.Cone(0.6f, 3, 6), new Material(new Numerics.Vector4(0.85f, 0.8f, 0.7f, 1), Shading.Flat));
        mill.Position = new Numerics.Vector3(3.5f, 1.5f, 3);
        var hub = mill.Add(new Node("sails") { Position = new Numerics.Vector3(0, 1.2f, 0.45f) });
        foreach (var angle in new[] { 0f, MathF.PI / 2 })
        {
            var sail = hub.Add("sail", MeshBuilder.Box(new Numerics.Vector3(0.25f, 2.6f, 0.04f)), new Material(new Numerics.Vector4(0.95f, 0.95f, 0.9f, 1), Shading.Flat) { DoubleSided = true });
            sail.Rotation = Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitZ, angle);
        }

        var quarter = Enumerable.Range(0, 5).Select(i => (i * 0.75f, Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitZ, -i * MathF.PI / 2))).ToArray();
        _windmill = new AnimationPlayer([hub]);
        _windmill.Play(new AnimationClip("turn", [AnimationChannel.Turn(0, quarter)]));
        _stalk = Stalk();
    }

    /// <summary>
    /// A skinned stalk: four cylinders stacked into one mesh on a chain of four bones, each vertex pulled by the bones
    /// either side of its height, so the stalk bends smoothly as the bones sway.
    /// </summary>
    private Node Stalk()
    {
        const float Step = 0.5f;
        var mesh = MeshBuilder.Combine([.. Enumerable.Range(0, 4).Select(i => (MeshBuilder.Cylinder(0.14f - (i * 0.025f), Step, 10), Numerics.Matrix4x4.CreateTranslation(0, i * Step, 0)))]);
        var joints = new int[mesh.Positions.Count * 4];
        var weights = new float[mesh.Positions.Count * 4];
        for (var v = 0; v < mesh.Positions.Count; v++)
        {
            var along = Math.Clamp(mesh.Positions[v].Y / Step, 0, 3);
            var lower = Math.Min((int)along, 3);
            var upper = Math.Min(lower + 1, 3);
            var t = along - lower;
            (joints[v * 4], joints[(v * 4) + 1]) = (lower, upper);
            (weights[v * 4], weights[(v * 4) + 1]) = (1 - t, upper == lower ? 0 : t);
            if (upper == lower)
            {
                weights[v * 4] = 1;
            }
        }

        var stalk = _world.Add(new Node("stalk") { Position = new Numerics.Vector3(-2.2f, 0, 1.4f) });
        stalk.Parts.Add(new MeshPart(mesh, new Material(new Numerics.Vector4(0.45f, 0.75f, 0.3f, 1))) { Weights = new SkinWeights(joints, weights) });
        var parent = stalk;
        for (var i = 0; i < _bones.Length; i++)
        {
            _bones[i] = parent.Add(new Node($"bone{i}") { Position = new Numerics.Vector3(0, i == 0 ? 0 : Step, 0) });
            parent = _bones[i];
        }

        stalk.Skin = new Skin(_bones, [.. Enumerable.Range(0, 4).Select(i => Numerics.Matrix4x4.CreateTranslation(0, -i * Step, 0))]);
        return stalk;
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Confirm))
        {
            Switch();
        }

        Steer(input);
        _angle += elapsed.TotalSeconds * 0.6;

        // The car drives a circle, facing along it, its wheels rolling as far as it goes.
        var around = (float)_angle;
        _car.Position = new Numerics.Vector3(MathF.Sin(around) * 1.2f, 0, MathF.Cos(around) * 1.2f);
        _car.Rotation = Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitY, around + (MathF.PI / 2));
        foreach (var wheel in _wheels)
        {
            wheel.Turn(Numerics.Vector3.UnitZ, -(float)elapsed.TotalSeconds * 0.6f * 1.2f / 0.35f);
        }

        _windmill.Update(elapsed);

        // The stalk sways, each bone a little later than the one below.
        for (var i = 0; i < _bones.Length; i++)
        {
            _bones[i].Rotation = Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitZ, MathF.Sin((float)(_angle * 2.5) - (i * 0.7f)) * 0.3f);
        }

        _stalk.Skin!.Deform(_stalk);
        _frameMs = (_frameMs * 0.9) + (elapsed.TotalMilliseconds * 0.1);
    }

    public override void DrawLayers(SpriteBatch batch, Rectangle viewport)
    {
        var size = _gpu ? new Point(Math.Max(1, viewport.Width), Math.Max(1, viewport.Height)) : new Point(SoftwareWidth, SoftwareHeight);
        if (_view is null || _view.Size != size || (_view is GpuView) != _gpu)
        {
            _view?.Dispose();
            var device = batch.GraphicsDevice;
            _view = _gpu ? new GpuView(device, size.X, size.Y) : new SoftwareView(device, size.X, size.Y);
        }

        // Sun from the front left, and a cool bounce from below the other way.
        _view.Lighting = Lighting.Default with { Fill = [new ParallelLight(new Numerics.Vector3(0.5f, 0.6f, 0.4f), new Numerics.Vector3(0.15f, 0.18f, 0.25f))] };
        _orbit.Apply(_camera);
        _view.Look(_camera);
        _clock.Restart();
        _view.Begin(Sky);
        if (_view is SoftwareView software)
        {
            software.Renderer.Mapping = _affine ? TextureMapping.Affine : TextureMapping.PerspectiveCorrect;
        }

        _view.DrawScene(_world, _camera, _list);
        var drawn = _clock.Elapsed.TotalMilliseconds;
        _view.Finish();
        _finishMs = (_finishMs * 0.9) + ((_clock.Elapsed.TotalMilliseconds - drawn) * 0.1);
        _drawMs = (_drawMs * 0.9) + (_clock.Elapsed.TotalMilliseconds * 0.1);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        batch.FillRectangle(viewport, Color.Black);
        if (_view is not null)
        {
            // The picture fills the view at its own shape; the software one with point sampling, so its pixels stay square.
            var texture = _view.Texture;
            var scale = MathF.Min(viewport.Width / (float)texture.Width, viewport.Height / (float)texture.Height);
            var size = new Point((int)(texture.Width * scale), (int)(texture.Height * scale));
            var at = new Rectangle(viewport.Center.X - (size.X / 2), viewport.Center.Y - (size.Y / 2), size.X, size.Y);
            _picture = at;
            batch.Restart(sampler: SamplerState.PointClamp);
            batch.Draw(texture, at, Color.White);
            batch.Restart();
        }

        var fonts = _context.Fonts;
        var pad = fonts.Px(12);
        var name = _gpu ? "GPU (BasicEffect)" : _affine ? "Software (Jentl.Raster), affine textures" : "Software (Jentl.Raster)";
        var size2 = _view?.Size ?? Point.Zero;
        string[] lines =
        [
            name,
            string.Create(CultureInfo.InvariantCulture, $"{size2.X}x{size2.Y}, draw {_drawMs:0.00} ms (of which upload {_finishMs:0.00}), frame {_frameMs:0.0} ms"),
            string.Create(CultureInfo.InvariantCulture, $"{_list.Drawn} meshes drawn, {_list.Culled} out of view; clicked: {_picked}"),
            "Enter or Switch to change; drag to turn, scroll to zoom, click to pick",
        ];
        var box = new Rectangle(viewport.X + pad, viewport.Y + pad, fonts.Px(640), (int)(fonts.Small.LineHeight * lines.Length) + pad);
        batch.FillRectangle(box, new Color(0, 0, 0, 160));
        for (var i = 0; i < lines.Length; i++)
        {
            batch.DrawText(fonts.Small, lines[i], new Vector2(box.X + (pad / 2), box.Y + (pad / 2) + (i * fonts.Small.LineHeight)), i == 0 ? Palette.Accent : Palette.Text, shadow: false);
        }

        _buttons.Draw(batch, fonts.Body, new Rectangle(viewport.X, viewport.Bottom - ButtonBar.Height(fonts.Body) - pad, viewport.Width, ButtonBar.Height(fonts.Body)));
    }

    protected override void Dispose(bool disposing)
    {
        _view?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Drags turn the camera, the wheel zooms, and a click names what is under it.</summary>
    private void Steer(InputState input)
    {
        if (input.Cursor.IsDown)
        {
            if (_dragFrom is { } from)
            {
                var moved = input.Cursor.Position - from;
                _orbit.Rotate(-moved.X * 0.01f, moved.Y * 0.01f);
            }

            _dragFrom = input.Cursor.Position;
        }
        else
        {
            _dragFrom = null;
        }

        if (input.Wheel != 0)
        {
            _orbit.Zoom(MathF.Pow(0.9f, input.Wheel));
        }

        if (input.Tap is { } tap && _picture.Contains(tap) && _view is not null)
        {
            // From the screen to the picture, which may be smaller and scaled up.
            var size = new Numerics.Vector2(_view.Size.X, _view.Size.Y);
            var point = new Numerics.Vector2((tap.X - _picture.X) / (float)_picture.Width * size.X, (tap.Y - _picture.Y) / (float)_picture.Height * size.Y);
            var hit = _world.Pick(_camera.RayThrough(point, size));
            _picked = hit?.Node.Name ?? "nothing";
        }
    }

    private void Switch()
    {
        _gpu = !_gpu;
        _drawMs = 0;
        _context.Sounds.Play(Sound.Click);
        SetButtons();
    }

    // Affine texture mapping is the software renderer's; the GPU always maps correctly.
    private void SetButtons()
    {
        List<Button> buttons = [new Button("Switch", Switch)];
        if (!_gpu)
        {
            buttons.Add(new Button(_affine ? "Affine: on" : "Affine: off", () =>
            {
                _affine = !_affine;
                SetButtons();
            }));
        }

        buttons.Add(new Button("Back", () => Scenes.Pop()));
        _buttons.Set([.. buttons]);
    }
}
