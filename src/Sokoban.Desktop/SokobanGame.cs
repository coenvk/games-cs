using Jentl.Core;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Desktop.Scenes;
using Sokoban.Desktop.UI;
using Sokoban.Model;

namespace Sokoban.Desktop;

internal sealed class SokobanGame : JentlGame
{
    // Frames to idle after a script before taking the screenshot, so transitions can finish.
    private const int SettleFrames = 60;
    private const int WaitFrames = 30;

    private readonly GameOptions _options;
    private readonly Queue<KeyboardState>? _script;
    private int _settledFrames;
    private Tileset? _tileset;
    private Fonts? _fonts;

    public SokobanGame(GameOptions options)
        : base(1280, 800)
    {
        _options = options;
        _script = options.Script is null ? null : BuildScript(options.Script);
        Window.AllowUserResizing = true;
        Window.Title = "Sokoban";
        IsMouseVisible = true;
    }

    protected override Color ClearColor => Palette.Background;

    protected override void LoadContent()
    {
        base.LoadContent();
        _tileset = new Tileset(GraphicsDevice);
        _fonts = new Fonts();
        var levels = Xsb.ParsePack(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Levels", "original.xsb")));
        var context = new GameContext(levels, _tileset, _fonts, Controls.Default);

        Scene[] start = _options.StartLevel is { } level
            ? [new MenuScene(context), new PlayScene(context, level)]
            : [new MenuScene(context)];
        Scenes.Reset(start);
    }

    protected override KeyboardState ReadKeyboard()
    {
        if (_script is null)
        {
            return base.ReadKeyboard();
        }

        if (_script.TryDequeue(out var state))
        {
            return state;
        }

        _settledFrames++;
        return default;
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (_options.ScreenshotPath is { } path && (_script is null || _settledFrames >= SettleFrames))
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
            _fonts?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Each key is held for one frame and released for one frame.</summary>
    private static Queue<KeyboardState> BuildScript(IEnumerable<string> keys)
    {
        var frames = new Queue<KeyboardState>();
        foreach (var name in keys)
        {
            if (name.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < WaitFrames; i++)
                {
                    frames.Enqueue(default);
                }

                continue;
            }

            frames.Enqueue(new KeyboardState(Enum.Parse<Keys>(name, ignoreCase: true)));
            frames.Enqueue(default);
        }

        return frames;
    }
}
