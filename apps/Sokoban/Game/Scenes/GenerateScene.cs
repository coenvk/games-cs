using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Sokoban.Model;
using Sokoban.Solver;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

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
    private readonly ButtonBar _buildingButtons;
    private readonly ButtonBar _failedButtons;

    /// <param name="seed">Fixes the level for reproducible runs; random when null.</param>
    public GenerateScene(GameContext context, int? seed = null)
    {
        _context = context;
        _seed = seed;
        _buildingButtons = new ButtonBar(new Button(T("Cancel"), () => Scenes.Pop(Navigation.Fade())));
        _failedButtons = new ButtonBar(
            new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())),
            new Button(T("Try again"), Retry, Highlight: true));
    }

    protected override void OnEnter()
    {
        _context.Sounds.PlayMusic(Music.Puzzle);
        Start();
    }

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
            Retry();
            return;
        }

        if ((_failed ? _failedButtons : _buildingButtons).Update(input))
        {
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
        var title = _failed ? T("Couldn't make a good level") : T("Building a level...");
        batch.DrawText(fonts.Heading, title, center, Palette.Text, Align.Center);
        var buttons = new Rectangle(viewport.X, (int)(center.Y + fonts.Heading.LineHeight + fonts.Px(24)), viewport.Width, ButtonBar.Height(fonts.Body));
        (_failed ? _failedButtons : _buildingButtons).Draw(batch, fonts.Body, buttons);
    }

    private void Retry()
    {
        _runs = 0;
        Start();
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
