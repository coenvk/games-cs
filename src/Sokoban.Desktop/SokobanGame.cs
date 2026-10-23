using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Model;

namespace Sokoban.Desktop;

internal sealed class SokobanGame : Game
{
    private const int Margin = 24;
    private static readonly Color Background = new(28, 33, 48);

    private readonly GameOptions _options;
    private readonly GraphicsDeviceManager _graphics;
    private readonly InputState _input = new();
    private readonly ActionMap _actions = ActionMap.Default;

    private SpriteBatch? _batch;
    private Tileset? _tileset;
    private PlayScene? _play;

    public SokobanGame(GameOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        Window.AllowUserResizing = true;
        Window.Title = "Sokoban";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _tileset = new Tileset(GraphicsDevice);
        var levels = Xsb.ParsePack(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Levels", "original.xsb")));
        _play = new PlayScene(levels, _tileset, _options.StartLevel);
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        if (_actions.WasPressed(_input, GameAction.Quit))
        {
            Exit();
            return;
        }

        _play!.Update(_input, _actions, gameTime.ElapsedGameTime);
        Window.Title = $"Sokoban · {_play.Status}";
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Background);

        var viewport = GraphicsDevice.Viewport.Bounds;
        viewport.Inflate(-Margin, -Margin);

        // Textures are loaded with Texture2D.FromFile, which does not premultiply alpha.
        _batch!.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
        _play!.Draw(_batch, viewport);
        _batch.End();

        base.Draw(gameTime);

        if (_options.ScreenshotPath is { } path)
        {
            SaveScreenshot(path);
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batch?.Dispose();
            _tileset?.Dispose();
            _graphics.Dispose();
        }

        base.Dispose(disposing);
    }

    private void SaveScreenshot(string path)
    {
        var parameters = GraphicsDevice.PresentationParameters;
        var (width, height) = (parameters.BackBufferWidth, parameters.BackBufferHeight);
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        using var file = File.Create(path);
        texture.SaveAsPng(file, width, height);
    }
}
