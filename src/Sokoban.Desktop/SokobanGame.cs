using Jentl.Core;
using Jentl.Core.Audio;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
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
    private const int WaitFrames = 30;

    private readonly GameOptions _options;
    private readonly Queue<KeyboardState>? _script;
    private int _settledFrames;
    private Tileset? _tileset;
    private Fonts? _fonts;
    private SoundPlayer? _sounds;

    public SokobanGame(GameOptions options)
        : base(1280, 800)
    {
        _options = options;
        _script = options.Script is null ? null : BuildScript(options.Script);
        Window.AllowUserResizing = true;
        Window.Title = "Sokoban";
        IsMouseVisible = true;

        // Borderless fullscreen at desktop resolution, instead of switching display modes.
        Graphics.HardwareModeSwitch = false;
    }

    protected override Color ClearColor => Palette.Background;

    protected override void LoadContent()
    {
        base.LoadContent();
        _tileset = new Tileset(GraphicsDevice);
        _fonts = new Fonts();
        var levels = Xsb.ParsePack(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Levels", "original.xsb")));

        var store = ResolveStore();
        var settings = SavedFile<Settings>.Load(store, "settings.json", Settings.FromJson, s => s.ToJson(), () => new Settings());
        _sounds = SoundPlayer.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "sounds"), Sound.Navigate, Sound.Completed);
        _sounds.Enabled = settings.Value.Sound;
        SetFullscreen(settings.Value.Fullscreen);

        var context = new GameContext(
            levels,
            _tileset,
            _fonts,
            Controls.Default,
            new ProgressStore(store),
            _sounds,
            settings,
            SetFullscreen);

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
        if (_options.ScreenshotPath is { } path && (_script is null || _settledFrames >= _options.SettleFrames))
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
            _sounds?.Dispose();
        }

        base.Dispose(disposing);
    }

    // Scripted runs must not touch the player's real saves unless a directory is given.
    private SaveStore? ResolveStore() => _options switch
    {
        { SaveDirectory: { } directory } => new SaveStore(directory),
        { Script: not null } => null,
        _ => SaveStore.ForApp("Sokoban"),
    };

    private void SetFullscreen(bool fullscreen)
    {
        if (Graphics.IsFullScreen != fullscreen)
        {
            Graphics.IsFullScreen = fullscreen;
            Graphics.ApplyChanges();
        }
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
