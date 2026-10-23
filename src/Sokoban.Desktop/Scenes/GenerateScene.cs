using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Sokoban.Model;
using Sokoban.Solver;

namespace Sokoban.Desktop.Scenes;

/// <summary>Generates a random level in the background, then replaces itself with a scene to play it.</summary>
internal sealed class GenerateScene : Scene
{
    // A run occasionally finds nothing hard enough; retry quietly a few times before giving up.
    private const int MaxRuns = 5;

    private static readonly GeneratorOptions Plain = new(Width: 10, Height: 9, Crates: 4, MinPushes: 12, Attempts: 80);

    // Ice makes levels harder to build (the solver must confirm every one), so ask a little less of them.
    private static readonly GeneratorOptions Icy = Plain with { MinPushes = 10, IceCells = 10 };

    private readonly GameContext _context;
    private readonly int? _seed;
    private CancellationTokenSource? _cancellation;
    private Task<Level?>? _task;
    private bool _failed;
    private int _runs;

    /// <param name="seed">Fixes the level for reproducible runs; random when null.</param>
    public GenerateScene(GameContext context, int? seed = null)
    {
        _context = context;
        _seed = seed;
    }

    protected override void OnEnter() => Start();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (_failed && _context.Actions.WasPressed(input, GameAction.Confirm))
        {
            _runs = 0;
            Start();
            return;
        }

        if (_task is { IsCompleted: true } task)
        {
            _task = null;
            if (task.IsCompletedSuccessfully && task.Result is { } level)
            {
                Scenes.Replace(new PlayScene(_context, level), Navigation.Fade());
            }
            else if (++_runs < MaxRuns)
            {
                Start();
            }
            else
            {
                _failed = true;
            }
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var center = new Vector2(viewport.Center.X, viewport.Center.Y - fonts.Heading.LineHeight);
        var (title, hint) = _failed
            ? ("Couldn't make a good level", "Enter to try again, Esc to go back")
            : ("Building a level...", "Esc to cancel");
        batch.DrawText(fonts.Heading, title, center, Palette.Text, Align.Center);
        batch.DrawText(fonts.Small, hint, center + new Vector2(0, fonts.Heading.LineHeight + 12), Palette.Muted, Align.Center, shadow: false);
    }

    private void Start()
    {
        _failed = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
#pragma warning disable CA5394 // Level layout randomness, not security relevant.
        var random = new Random(_seed + _runs ?? Random.Shared.Next());
#pragma warning restore CA5394
        // Every other random level has ice.
        var options = random.Next(2) == 0 ? Plain : Icy;
        _task = Task.Run(() => LevelGenerator.Generate(options, random, token), token);
    }
}
