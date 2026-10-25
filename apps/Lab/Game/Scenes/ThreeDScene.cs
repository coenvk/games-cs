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
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Numerics = System.Numerics;
using RasterTexture = Jentl.Raster.Texture;

namespace Games.Scenes;

/// <summary>
/// A spinning low-poly car on a field, drawn by the software renderer (320×180, scaled up) or on the GPU (full size):
/// switch with Enter or a tap, and compare the time each takes to draw.
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

        _angle += elapsed.TotalSeconds * 0.6;
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

        var camera = new Camera { Position = new Numerics.Vector3(4, 3, 6), Target = new Numerics.Vector3(0, 0.5f, 0), FieldOfView = MathF.PI / 3.5f, Near = 0.1f, Far = 100 };
        _view.ViewProjection = camera.ViewProjection(size.X / (float)size.Y);
        _clock.Restart();
        _view.Begin(Sky);
        var spin = Numerics.Matrix4x4.CreateRotationY((float)_angle);
        if (_view is SoftwareView software)
        {
            software.Renderer.Mapping = _affine ? TextureMapping.Affine : TextureMapping.PerspectiveCorrect;
        }

        _view.DrawMesh(_ground, Numerics.Matrix4x4.Identity, new Material(Numerics.Vector4.One, Shading.Flat, _grass));
        _view.DrawMesh(_crate, Numerics.Matrix4x4.CreateRotationY(0.4f) * Numerics.Matrix4x4.CreateTranslation(2.8f, 0.6f, -1.5f), new Material(Numerics.Vector4.One, Shading.Flat, _wood));
        _view.DrawMesh(_body, spin, new Material(Numerics.Vector4.One, Shading.Flat, _paint));
        foreach (var (x, z) in new[] { (-1.2f, 0.8f), (1.2f, 0.8f), (-1.2f, -0.8f), (1.2f, -0.8f) })
        {
            _view.DrawMesh(_wheel, Numerics.Matrix4x4.CreateTranslation(x, 0.35f, z) * spin, new Material(new Numerics.Vector4(0.1f, 0.1f, 0.12f, 1)));
        }

        _view.DrawMesh(_ball, Numerics.Matrix4x4.CreateTranslation(-3, 0.8f, -2), new Material(new Numerics.Vector4(0.9f, 0.8f, 0.2f, 1)));
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
            "Enter or Switch to change",
        ];
        var box = new Rectangle(viewport.X + pad, viewport.Y + pad, fonts.Px(640), (int)(fonts.Small.LineHeight * 3) + pad);
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
