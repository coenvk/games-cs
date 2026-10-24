using System.Globalization;
using Bricks;
using Jentl.Core.Diagnostics;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Ecs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Breakout on the Jentl ECS and physics: <see cref="BricksGame"/> runs the rules; this screen steers the paddle
/// (pointer, keys, gamepad), draws the entities and turns the game's events into sounds and sparks.
/// </summary>
internal sealed class BricksScene : Scene
{
    private const int HudHeight = 64;
    private const float KeySpeed = 900;

    private static readonly Color[] RowColors =
    [
        new(232, 72, 85), new(245, 140, 60), new(255, 199, 44), new(110, 204, 120), new(76, 175, 239), new(170, 120, 230),
    ];

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private float _scale = 1;
    private Vector2 _offset;

    public BricksScene(GameContext context, int? seed = null)
    {
        _context = context;
        Seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);
        Game = new BricksGame(Seed);
        _buttons = new ButtonBar(new Button("Menu", () => Scenes.Push(new BricksPauseScene(_context, this))));
    }

    public int Seed { get; }

    public BricksGame Game { get; }

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"Seed {Seed}  level {Game.Level + 1}  speed {Game.BallSpeed:0}"),
        string.Create(CultureInfo.InvariantCulture, $"Entities {Game.World.Count}  balls {Game.BallCount}  bricks {Game.BricksLeft}  power-ups {Game.World.CountOf<PowerUp>()}"),
        string.Create(CultureInfo.InvariantCulture, $"Bodies {Game.Physics.Bodies.Count}  contacts {Game.Physics.Contacts.Count}"),
    ];

    public void NewGame() => Scenes.Replace(new BricksScene(_context), Navigation.Fade());

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Puzzle);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new BricksPauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        // The pointer steers whenever it moves or is held; keys and the gamepad nudge.
        if (input.PointerMoved || input.Cursor.IsDown)
        {
            Game.PaddleTarget = ToField(input.Cursor.Position).X;
        }

        var seconds = (float)elapsed.TotalSeconds;
        if (actions.IsDown(input, GameAction.Left))
        {
            Game.PaddleTarget -= KeySpeed * seconds;
        }

        if (actions.IsDown(input, GameAction.Right))
        {
            Game.PaddleTarget += KeySpeed * seconds;
        }

        if (input.Tap is not null || actions.WasPressed(input, GameAction.Confirm) || actions.WasPressed(input, GameAction.Up))
        {
            Game.Launch();
        }

        Game.Update(elapsed);
        foreach (var e in Game.DrainEvents())
        {
            Show(e);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"Bricks  level {Game.Level + 1}"), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"{Game.Score:N0}"), text, Palette.Accent, Align.Right);

        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var area = new Rectangle(viewport.X + margin, hud.Bottom + fonts.Px(8), viewport.Width - 2 * margin, buttons.Top - hud.Bottom - fonts.Px(8));
        _scale = MathF.Min(area.Width / BricksGame.Width, area.Height / BricksGame.Height);
        _offset = new Vector2(area.Center.X - BricksGame.Width * _scale / 2, area.Center.Y - BricksGame.Height * _scale / 2);

        var field = Rect(new System.Numerics.Vector2(BricksGame.Width / 2, BricksGame.Height / 2), BricksGame.Width, BricksGame.Height);
        batch.FillRectangle(field, Palette.Ink);
        var world = Game.World;
        foreach (var wall in world.Query<Wall, Physical>())
        {
            // The side walls reach past the field (so nothing escapes round them); draw only the part on it.
            batch.FillRectangle(Rectangle.Intersect(Bounds(world.Get<Physical>(wall).Body), field), Palette.PanelLight);
        }

        foreach (var entity in world.Query<Brick, Physical>())
        {
            DrawBrick(batch, world.Get<Brick>(entity), world.Get<Physical>(entity).Body);
        }

        foreach (var entity in world.Query<PowerUp, Physical>())
        {
            DrawPowerUp(batch, world.Get<PowerUp>(entity).Kind, world.Get<Physical>(entity).Body);
        }

        var paddle = world.Get<Physical>(Game.Paddle).Body;
        DrawPaddle(batch, paddle, world.Get<Paddle>(Game.Paddle));
        foreach (var entity in world.Query<Ball, Physical>())
        {
            var at = Screen(world.Get<Physical>(entity).Body.Position);
            var radius = BricksGame.BallRadius * _scale;
            batch.FillCircle(at + new Vector2(2, 3) * _scale / 2, radius * 1.15f, Palette.Shadow);
            batch.FillCircle(at, radius * 1.2f, Color.White);
        }

        // Lives as small balls, bottom left of the field.
        for (var i = 0; i < Game.Lives - 1; i++)
        {
            batch.FillCircle(Screen(new System.Numerics.Vector2(BricksGame.WallThickness + 16 + i * 22, BricksGame.Height - 14)), 6 * _scale, Palette.Muted);
        }

        var hint = Game.HasStuckBall ? "Tap or press Enter to launch" : "";
        if (hint.Length > 0)
        {
            batch.DrawText(fonts.Small, hint, Rect(new System.Numerics.Vector2(BricksGame.Width / 2, BricksGame.PaddleY - 70), BricksGame.Width, 40), Palette.Muted);
        }

        _buttons.Draw(batch, fonts.Small, buttons);
    }

    public override void DrawDebug(SpriteBatch batch, Rectangle viewport) => PhysicsDebug.Draw(batch, Game.Physics, _offset, _scale);

    private static Color RowColor(int row) => RowColors[row % RowColors.Length];

    private void Show(BricksEvent e)
    {
        var sounds = _context.Sounds;
        switch (e)
        {
            case BrickHit { Broken: true } hit:
                sounds.Play(Sound.Hit);
                Scenes.Effects.Emit(Screen(hit.Position), ParticleEffects.Sparkles with { Colors = [RowColor(hit.Row), Color.White] }, _scale * 1.4f);
                break;
            case BrickHit:
                sounds.Play(Sound.Click);
                break;
            case PaddleBounce:
                sounds.Play(Sound.Navigate);
                break;
            case PowerCaught caught:
                sounds.Play(Sound.Pickup);
                Scenes.Effects.Emit(Screen(caught.Position), ParticleEffects.Rise, _scale * 1.4f);
                break;
            case LifeLost:
                sounds.Play(Sound.Lose);
                Scenes.Post.Flash(new Color(220, 40, 30, 90), TimeSpan.FromMilliseconds(350));
                break;
            case LevelCleared:
                sounds.Play(Sound.Fanfare);
                Celebration.Confetti(Scenes);
                break;
            case GameOver:
                sounds.Play(Sound.Lose);
                Timers.After(TimeSpan.FromSeconds(0.8), () => Scenes.Push(new BricksOverScene(_context, this)));
                break;
        }
    }

    private void DrawBrick(SpriteBatch batch, Brick brick, Jentl.Physics.Body body)
    {
        var bounds = Bounds(body);
        if (brick.Unbreakable)
        {
            batch.FillRectangle(bounds, new Color(120, 126, 140));
            var inner = bounds;
            inner.Inflate(-(int)(3 * _scale), -(int)(3 * _scale));
            batch.FillRectangle(inner, new Color(150, 156, 172));
            return;
        }

        var color = RowColor(brick.Row);
        var tough = brick.Points > 10;
        batch.FillRectangle(bounds, tough ? Color.Lerp(color, Color.Black, 0.35f) : color);
        var shine = bounds with { Height = Math.Max(2, bounds.Height / 4) };
        batch.FillRectangle(shine, Color.White * 0.35f);
        if (tough && brick.Hits == 1)
        {
            // Cracked: a hit taken, one to go.
            var mid = bounds.Center.ToVector2();
            batch.DrawLine(mid + new Vector2(-bounds.Width * 0.3f, -bounds.Height * 0.3f), mid + new Vector2(0, bounds.Height * 0.1f), Palette.Ink, 2);
            batch.DrawLine(mid + new Vector2(0, bounds.Height * 0.1f), mid + new Vector2(bounds.Width * 0.25f, bounds.Height * 0.35f), Palette.Ink, 2);
        }
    }

    private void DrawPaddle(SpriteBatch batch, Jentl.Physics.Body body, Paddle paddle)
    {
        var bounds = Bounds(body);
        var color = paddle.WideFor > TimeSpan.Zero ? new Color(110, 204, 120) : Palette.Accent;
        var radius = bounds.Height / 2f;
        batch.FillRectangle(new Rectangle(bounds.X + (int)radius, bounds.Y, bounds.Width - (int)(2 * radius), bounds.Height), color);
        batch.FillCircle(new Vector2(bounds.X + radius, bounds.Center.Y), radius * 1.15f, color);
        batch.FillCircle(new Vector2(bounds.Right - radius, bounds.Center.Y), radius * 1.15f, color);
    }

    private void DrawPowerUp(SpriteBatch batch, PowerKind kind, Jentl.Physics.Body body)
    {
        var bounds = Bounds(body);
        var (color, label) = kind switch
        {
            PowerKind.Wide => (new Color(110, 204, 120), "W"),
            PowerKind.Multi => (new Color(76, 175, 239), "M"),
            _ => (new Color(232, 72, 85), "+"),
        };
        batch.FillRectangle(bounds, color);
        batch.DrawText(_context.Fonts.Sized(bounds.Height * 0.9f), label, bounds, Color.White, shadow: false);
    }

    private Rectangle Bounds(Jentl.Physics.Body body)
    {
        var bounds = body.Bounds;
        var min = Screen(bounds.Min);
        var max = Screen(bounds.Max);
        return new Rectangle((int)MathF.Round(min.X), (int)MathF.Round(min.Y), (int)MathF.Round(max.X - min.X), (int)MathF.Round(max.Y - min.Y));
    }

    private Rectangle Rect(System.Numerics.Vector2 centre, float width, float height)
    {
        var at = Screen(centre - new System.Numerics.Vector2(width, height) / 2);
        return new Rectangle((int)at.X, (int)at.Y, (int)(width * _scale), (int)(height * _scale));
    }

    private Vector2 Screen(System.Numerics.Vector2 field) => _offset + new Vector2(field.X, field.Y) * _scale;

    private System.Numerics.Vector2 ToField(Vector2 screen)
    {
        var field = (screen - _offset) / _scale;
        return new System.Numerics.Vector2(field.X, field.Y);
    }
}

internal sealed class BricksPauseScene : PanelScene
{
    private readonly BricksScene _game;

    public BricksPauseScene(GameContext context, BricksScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem("Resume", () => Scenes.Pop()),
            new MenuItem("New game", () =>
            {
                Scenes.Pop();
                _game.NewGame();
            }),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => "Bricks";

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(string.Create(CultureInfo.InvariantCulture, $"Level {_game.Game.Level + 1}, score {_game.Game.Score:N0}"), Palette.Muted),
    ];
}

internal sealed class BricksOverScene : PanelScene
{
    private readonly BricksScene _game;

    public BricksOverScene(GameContext context, BricksScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem("New game", () =>
            {
                Scenes.Pop();
                _game.NewGame();
            }),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => "Game over";

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(string.Create(CultureInfo.InvariantCulture, $"Score {_game.Game.Score:N0}"), Palette.Text),
        new(string.Create(CultureInfo.InvariantCulture, $"Reached level {_game.Game.Level + 1}"), Palette.Muted),
    ];

    protected override void OnBack() => Navigation.ToMainMenu(Scenes, Context);
}
