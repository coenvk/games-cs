using System.Globalization;
using Jentl.Core;
using Jentl.Core.Audio;
using Jentl.Core.Cards;
using Jentl.Core.Content;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Desktop.Scenes;
using Sokoban.Desktop.UI;
using Sokoban.Model;
using Jentl.Core.UI;

namespace Sokoban.Desktop;

internal sealed class SokobanGame : JentlGame
{
    private const int WaitFrames = 30;
    private const int DragFrames = 12;

    private readonly GameOptions _options;
    private readonly ISaveStore? _store;
    private readonly PlatformFeatures _platform;
    private readonly Queue<ScriptFrame>? _script;
    private PointerState _scriptPointer;
    private GamePadState _scriptPad;
    private int _settledFrames;
    private Tileset? _tileset;
    private Fonts? _fonts;
    private SoundPlayer? _sounds;
    private CardArt? _cardArt;

    /// <param name="store">Where to keep progress and settings; null keeps them in memory only.</param>
    public SokobanGame(GameOptions options, ISaveStore? store, PlatformFeatures platform)
        : base(options.Size?.X ?? 1280, options.Size?.Y ?? 800)
    {
        _options = options;
        _store = store;
        _platform = platform;
        _script = options.Script is null ? null : BuildScript(options.Script);
        VirtualScreen = options.Virtual;
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
        _fonts = new Fonts(GameFiles.ReadAllBytes("Assets/fonts/LilitaOne-Regular.ttf"));
        Scenes.Debug.Text = new FontTextDrawer(_fonts);
        Scenes.Debug.IsVisible = _options.Debug;
        LevelPack[] packs =
        [
            new("Classic", "Level", LoadPack("original.xsb")),
            new("Ice", "Ice", LoadPack("ice.xsb")),
        ];

        var store = _store;
        var settings = SavedFile<Settings>.Load(store, "settings.json", Settings.FromJson, s => s.ToJson(), () => new Settings());
        Tr.Use(settings.Value.Language);
        _sounds = SoundPlayer.Load("Assets/sounds", Sound.Navigate, Sound.Completed);
        _sounds.Enabled = settings.Value.Sound;
        _sounds.Volume = Math.Clamp(settings.Value.Volume, 0f, 1f);
        _sounds.MusicEnabled = settings.Value.Music;
        _sounds.MusicVolume = settings.Value.MusicVolume;
        GameAudio.Register(_sounds);
        RetroScreen.Apply(Scenes.Post, settings.Value.Retro);
        if (_platform.CanFullscreen)
        {
            SetFullscreen(settings.Value.Fullscreen);
        }

        var context = new GameContext(
            packs,
            _tileset,
            _fonts,
            Controls.Default,
            new ProgressStore(store),
            _sounds,
            settings,
            SetFullscreen,
            _platform,
            new CardRenderer(_cardArt = new CardArt(GraphicsDevice), new FontTextDrawer(_fonts)),
            new AdventureSaves(store),
            new CodeLockStore(store),
            _options.Deal,
            _options.Server);

        Scene[] start = _options.StartLevel is { } level
            ? [new MenuScene(context), new PlayScene(context, new LevelRef(0, Math.Clamp(level, 0, packs[0].Levels.Count - 1)))]
            : [new MenuScene(context)];
        Scenes.Reset(start);
    }

    protected override KeyboardState ReadKeyboard()
    {
        if (_script is null)
        {
            return base.ReadKeyboard();
        }

        if (_script.TryDequeue(out var frame))
        {
            _scriptPointer = frame.Pointer ?? _scriptPointer with { IsDown = false };
            _scriptPad = frame.Pad;
            return frame.Keys;
        }

        _settledFrames++;
        _scriptPointer = _scriptPointer with { IsDown = false };
        _scriptPad = default;
        return default;
    }

    // Called right after ReadKeyboard each frame, so it returns the same script frame's pointer.
    protected override PointerState ReadPointer(TimeSpan now) => _script is null ? base.ReadPointer(now) : _scriptPointer;

    protected override GamePadState ReadGamePad() => _script is null ? base.ReadGamePad() : _scriptPad;

    protected override void Update(GameTime gameTime)
    {
        if (_fonts is not null)
        {
            _fonts.Fit(ScreenBounds);
            Scenes.Debug.TextSize = MathF.Max(13, 17 * _fonts.Scale);
        }
        _sounds?.Update(gameTime.ElapsedGameTime);
        base.Update(gameTime);
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
            _cardArt?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static IReadOnlyList<Level> LoadPack(string file) =>
        Xsb.ParsePack(GameFiles.ReadAllText($"Levels/{file}"));

    private void SetFullscreen(bool fullscreen)
    {
        if (Graphics.IsFullScreen != fullscreen)
        {
            Graphics.IsFullScreen = fullscreen;
            Graphics.ApplyChanges();
        }
    }

    /// <summary>
    /// Each key is held for one frame and released for one frame. <c>Tap:x,y</c> presses and releases the
    /// pointer there; <c>Drag:x1,y1,x2,y2</c> moves it from one point to the other while held. <c>Pad:A</c>
    /// presses a gamepad button; <c>RightStick:x,y,frames</c> tilts the right stick (with the trigger held when
    /// <c>x,y,frames,1</c>).
    /// </summary>
    private static Queue<ScriptFrame> BuildScript(IEnumerable<string> keys)
    {
        var frames = new Queue<ScriptFrame>();
        foreach (var name in keys)
        {
            if (name.StartsWith("Tap:", StringComparison.OrdinalIgnoreCase))
            {
                var at = Coordinates(name)[0];
                frames.Enqueue(new ScriptFrame(default, new PointerState(true, at)));
                frames.Enqueue(new ScriptFrame(default, new PointerState(false, at)));
                continue;
            }

            if (name.StartsWith("Drag:", StringComparison.OrdinalIgnoreCase))
            {
                var points = Coordinates(name);
                for (var step = 0; step <= DragFrames; step++)
                {
                    frames.Enqueue(new ScriptFrame(default, new PointerState(true, Vector2.Lerp(points[0], points[1], step / (float)DragFrames))));
                }

                frames.Enqueue(new ScriptFrame(default, new PointerState(false, points[1])));
                continue;
            }

            if (name.StartsWith("Pad:", StringComparison.OrdinalIgnoreCase))
            {
                var button = Enum.Parse<Buttons>(name[4..], ignoreCase: true);
                frames.Enqueue(new ScriptFrame(default, null, new GamePadState(Vector2.Zero, Vector2.Zero, 0f, 0f, button)));
                frames.Enqueue(default);
                continue;
            }

            if (name.StartsWith("RightStick:", StringComparison.OrdinalIgnoreCase))
            {
                var numbers = Numbers(name);
                var trigger = numbers.Length > 3 ? numbers[3] : 0f;
                for (var i = 0; i < numbers[2]; i++)
                {
                    frames.Enqueue(new ScriptFrame(default, null, new GamePadState(Vector2.Zero, new Vector2(numbers[0], numbers[1]), 0f, trigger, [])));
                }

                continue;
            }

            if (name.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < WaitFrames; i++)
                {
                    frames.Enqueue(default);
                }

                continue;
            }

            frames.Enqueue(new ScriptFrame(new KeyboardState(Enum.Parse<Keys>(name, ignoreCase: true)), null));
            frames.Enqueue(default);
        }

        return frames;
    }

    private static float[] Numbers(string token) =>
        token[(token.IndexOf(':', StringComparison.Ordinal) + 1)..]
            .Split(',')
            .Select(n => float.Parse(n, CultureInfo.InvariantCulture))
            .ToArray();

    private static Vector2[] Coordinates(string token)
    {
        var numbers = Numbers(token);
        return Enumerable.Range(0, numbers.Length / 2).Select(i => new Vector2(numbers[2 * i], numbers[2 * i + 1])).ToArray();
    }

    /// <summary>One frame of scripted input; a null pointer means "released where it last was".</summary>
    private readonly record struct ScriptFrame(KeyboardState Keys, PointerState? Pointer, GamePadState Pad = default);
}
