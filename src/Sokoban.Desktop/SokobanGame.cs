using Jentl.Core;
using Microsoft.Xna.Framework;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Model;

namespace Sokoban.Desktop;

internal sealed class SokobanGame : JentlGame
{
    private readonly GameOptions _options;
    private Tileset? _tileset;

    public SokobanGame(GameOptions options)
        : base(1280, 800)
    {
        _options = options;
        Window.AllowUserResizing = true;
        Window.Title = "Sokoban";
        IsMouseVisible = true;
    }

    protected override Color ClearColor { get; } = new(28, 33, 48);

    protected override void LoadContent()
    {
        base.LoadContent();
        _tileset = new Tileset(GraphicsDevice);
        var levels = Xsb.ParsePack(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Levels", "original.xsb")));
        var context = new GameContext(levels, _tileset, Controls.Default);
        Scenes.Push(new PlayScene(context, _options.StartLevel));
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (Scenes.Top is PlayScene play)
        {
            Window.Title = $"Sokoban · {play.Status}";
        }
    }

    protected override void Draw(GameTime gameTime)
    {
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
            _tileset?.Dispose();
        }

        base.Dispose(disposing);
    }
}
