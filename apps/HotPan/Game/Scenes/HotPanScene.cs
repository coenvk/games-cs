using System.Globalization;
using HotPan;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Hot Pan: <see cref="HotPanGame"/> runs the pan; this screen steers the cook (keys, gamepad, or a hand on the
/// screen: hold to walk towards it, tap or swipe up to jump), draws the kitchen, and turns the game's events into
/// sounds and sizzles. Chicken shows how hot it is by its colour: pink and pale are safe, golden burns, black is
/// about to be flicked out.
/// </summary>
internal sealed class HotPanScene : Scene
{
    private const int HudHeight = 64;
    private static readonly TimeSpan SteamEvery = TimeSpan.FromMilliseconds(260);

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private float _scale = 1;
    private Vector2 _offset;
    private TimeSpan _steam;
    private TimeSpan _sizzle;
    private float _flicker;

    public HotPanScene(GameContext context, int? seed = null)
    {
        _context = context;
#pragma warning disable CA5394 // A seed for the drops, not security relevant.
        Seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);
#pragma warning restore CA5394
        Game = new HotPanGame(Seed);
        _buttons = new ButtonBar(new Button(T("Menu"), () => Scenes.Push(new HotPanPauseScene(_context, this))));
    }

    public int Seed { get; }

    public HotPanGame Game { get; }

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"Seed {Seed}  pan {Game.PanTemperature:0}°C  health {Game.Health:0}  drop every {Game.DropInterval:0.0} s"),
        string.Create(CultureInfo.InvariantCulture, $"Cook ({Game.CookX:0}, {Game.CookY:0})  on {(Game.StandingOn is { } c ? $"{c.Cut} {c.Temperature:0}°C" : Game.OnGround ? "the pan" : "air")}"),
    ];

    public void NewGame() => Scenes.Replace(new HotPanScene(_context), Navigation.Fade());

    protected override void OnEnter() => _context.PlayMood(Music.Kitchen);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new HotPanPauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        // Keys and the gamepad, or a hand on the screen: the cook walks towards where it is held.
        var move = (actions.IsDown(input, GameAction.Right) ? 1f : 0) - (actions.IsDown(input, GameAction.Left) ? 1f : 0);
        if (input.Cursor.IsDown && move == 0)
        {
            var target = ToField(input.Cursor.Position).X;
            move = Math.Clamp((target - Game.CookX) / 40f, -1, 1);
        }

        var jump = actions.WasPressed(input, GameAction.Up) || actions.WasPressed(input, GameAction.Confirm)
            || input.Tap is not null || input.Swipe == SwipeDirection.Up;
        var held = actions.IsDown(input, GameAction.Up) || actions.IsDown(input, GameAction.Confirm) || input.Cursor.IsDown;
        Game.Update(elapsed, new PanInput(move, jump, held || jump));
        foreach (var e in Game.DrainEvents())
        {
            Show(e);
        }

        Steam(elapsed);
        _flicker += (float)elapsed.TotalSeconds;
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var area = new Rectangle(viewport.X, hud.Bottom, viewport.Width, buttons.Top - hud.Bottom);
        _scale = MathF.Min(area.Width / HotPanGame.Width, area.Height / HotPanGame.Height);
        _offset = new Vector2(area.Center.X - (HotPanGame.Width * _scale / 2), area.Center.Y - (HotPanGame.Height * _scale / 2));

        DrawKitchen(batch, area);
        DrawFlames(batch);
        DrawPan(batch);
        foreach (var piece in Game.Chicken)
        {
            DrawChicken(batch, piece);
        }

        DrawCook(batch);
        DrawHud(batch, hud, margin);
        if (Game.Time < TimeSpan.FromSeconds(6))
        {
            var hint = T("Jump onto chicken that's still cool: the pan burns your feet");
            batch.DrawText(fonts.Small, hint, Rect(HotPanGame.Width / 2, 40, HotPanGame.Width, 30), Palette.Text);
            batch.DrawText(fonts.Small, T("Arrows or A/D to walk, Space or Up to jump; or hold to walk, tap to jump"), Rect(HotPanGame.Width / 2, 72, HotPanGame.Width, 30), Palette.Muted);
        }

        _buttons.Draw(batch, fonts.Small, buttons);
    }

    /// <summary>Raw pink, pale as it warms, golden once too hot to stand on, brown, then black.</summary>
    internal static Color ChickenColor(float temperature)
    {
        (float At, Color Color)[] stops =
        [
            (HotPanGame.FridgeTemperature, new Color(242, 168, 170)),
            (40, new Color(240, 200, 180)),
            (HotPanGame.SafeTemperature, new Color(236, 214, 160)),
            (75, new Color(226, 168, 72)),
            (110, new Color(178, 108, 40)),
            (HotPanGame.CharTemperature, new Color(52, 36, 28)),
        ];
        if (temperature <= stops[0].At)
        {
            return stops[0].Color;
        }

        for (var i = 1; i < stops.Length; i++)
        {
            if (temperature <= stops[i].At)
            {
                var t = (temperature - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
                return Color.Lerp(stops[i - 1].Color, stops[i].Color, t);
            }
        }

        return stops[^1].Color;
    }

    private void Show(PanEvent e)
    {
        var sounds = _context.Sounds;
        switch (e)
        {
            case Jumped:
                sounds.Play(Sound.Jump);
                break;
            case Landed { Chicken: { } piece }:
                sounds.Play(Sound.Card);
                Scenes.Effects.Emit(Screen(Game.CookX, Game.CookY), ParticleEffects.Dust with { Colors = [ChickenColor(piece.Temperature)] }, _scale * 0.6f);
                break;
            case ChickenLanded landed:
                sounds.Play(Sound.Card);
                Scenes.Effects.Emit(Screen(landed.Chicken.X, HotPanGame.FloorY), ParticleEffects.Dust, _scale * 0.8f);
                break;
            case ChickenCharred charred:
                sounds.Play(Sound.Click);
                Scenes.Effects.Emit(Screen(charred.Chicken.X, charred.Chicken.Y), ParticleEffects.Smoke, _scale);
                break;
            case StartedBurning burn:
                sounds.Play(Sound.Sizzle);
                Scenes.Effects.Emit(Screen(burn.X, burn.Y), ParticleEffects.Smoke with { Colors = [new Color(230, 230, 230), new Color(160, 160, 160)] }, _scale * 0.6f);
                break;
            case GameOver:
                sounds.Play(Sound.Lose);
                Scenes.Post.Flash(new Color(230, 80, 20, 110), TimeSpan.FromMilliseconds(400));
                Timers.After(TimeSpan.FromSeconds(0.9), () => Scenes.Push(new HotPanOverScene(_context, this)));
                break;
        }
    }

    // Steam off chicken that is too hot, and a sizzle and smoke while the feet burn.
    private void Steam(TimeSpan elapsed)
    {
        _steam -= elapsed;
        _sizzle -= elapsed;
        if (_steam <= TimeSpan.Zero)
        {
            _steam = SteamEvery;
            foreach (var piece in Game.Chicken.Where(c => c.Landed && c.IsTooHot))
            {
                Scenes.Effects.Emit(Screen(piece.X, piece.Y), ParticleEffects.Smoke with { Colors = [new Color(255, 255, 255, 120)] }, _scale * 0.35f);
            }
        }

        if (Game.IsBurning && _sizzle <= TimeSpan.Zero)
        {
            _sizzle = TimeSpan.FromMilliseconds(420);
            _context.Sounds.Play(Sound.Sizzle, volume: 0.6f);
            Scenes.Effects.Emit(Screen(Game.CookX, Game.CookY), ParticleEffects.Smoke with { Colors = [new Color(220, 220, 220)] }, _scale * 0.4f);
        }
    }

    private void DrawKitchen(SpriteBatch batch, Rectangle area)
    {
        // Tiles behind, and the stove top the pan stands on.
        batch.FillRectangle(area, new Color(64, 50, 46));
        var tile = (int)(50 * _scale);
        for (var x = area.X; x < area.Right; x += tile)
        {
            batch.FillRectangle(new Rectangle(x, area.Y, 2, area.Height), new Color(76, 60, 55));
        }

        for (var y = area.Y; y < area.Bottom; y += tile)
        {
            batch.FillRectangle(new Rectangle(area.X, y, area.Width, 2), new Color(76, 60, 55));
        }

        var stove = Screen(0, HotPanGame.FloorY + 34);
        batch.FillRectangle(new Rectangle(area.X, (int)stove.Y, area.Width, area.Bottom - (int)stove.Y), new Color(36, 38, 44));
    }

    private float Glow => Math.Clamp((Game.PanTemperature - HotPanGame.StartPanTemperature) / 160f, 0, 1);

    private void DrawFlames(SpriteBatch batch)
    {
        // Blue gas flames, taller and more orange as the pan heats.
        var height = 10 + (Glow * 16);
        for (var i = 0; i < 14; i++)
        {
            var x = 120 + (i * 43);
            var wobble = MathF.Sin((_flicker * 11) + (i * 1.7f)) * 3;
            var tip = Screen(x, HotPanGame.FloorY + 34 - height - wobble);
            var flame = new Rectangle((int)(tip.X - (6 * _scale)), (int)tip.Y, (int)(12 * _scale), (int)((height + wobble) * _scale));
            batch.FillTriangle(flame, pointsUp: true, Color.Lerp(new Color(80, 140, 255), new Color(255, 150, 40), Glow));
        }
    }

    private void DrawPan(SpriteBatch batch)
    {
        // Cast iron, glowing red as it gets hotter; the sides slope out, the handle goes off to the right.
        var iron = Color.Lerp(new Color(58, 60, 68), new Color(150, 52, 30), Glow);
        var floor = Rect((HotPanGame.PanLeft + HotPanGame.PanRight) / 2, HotPanGame.FloorY + 12, HotPanGame.PanRight - HotPanGame.PanLeft + 40, 24);
        batch.FillRoundedRectangle(floor, (int)(10 * _scale), iron);
        batch.DrawLine(Screen(HotPanGame.PanLeft - 20, HotPanGame.FloorY + 2), Screen(HotPanGame.PanLeft - 46, HotPanGame.FloorY - 70), iron, 14 * _scale);
        batch.DrawLine(Screen(HotPanGame.PanRight + 20, HotPanGame.FloorY + 2), Screen(HotPanGame.PanRight + 46, HotPanGame.FloorY - 70), iron, 14 * _scale);
        batch.FillRectangle(Rect(HotPanGame.PanRight + 60, HotPanGame.FloorY - 30, 60, 14), new Color(30, 30, 34));
        batch.FillRoundedRectangle(Rect(HotPanGame.PanRight + 90, HotPanGame.FloorY - 34, 90, 18), (int)(8 * _scale), new Color(92, 56, 36));

        // The cooking surface itself, with a sheen of oil.
        var surface = Rect((HotPanGame.PanLeft + HotPanGame.PanRight) / 2, HotPanGame.FloorY + 3, HotPanGame.PanRight - HotPanGame.PanLeft + 30, 6);
        batch.FillRectangle(surface, Color.Lerp(new Color(84, 86, 94), new Color(220, 96, 44), Glow));
    }

    private void DrawChicken(SpriteBatch batch, Chicken piece)
    {
        var color = ChickenColor(piece.Temperature);
        var dark = Color.Lerp(color, Color.Black, 0.25f);
        var body = Rect(piece.X, piece.Y + (piece.Height / 2), piece.Width, piece.Height);
        switch (piece.Cut)
        {
            case Cut.Drumstick:
            {
                // Meat to the left, the bone sticking out to the right.
                var meat = Screen(piece.X - (piece.Width * 0.12f), piece.Y + (piece.Height / 2));
                batch.FillEllipse(meat, new Vector2(piece.Width * 0.38f, piece.Height * 0.5f) * _scale, dark);
                batch.FillEllipse(meat - new Vector2(0, 2 * _scale), new Vector2(piece.Width * 0.36f, piece.Height * 0.44f) * _scale, color);
                var bone = Color.Lerp(new Color(244, 236, 220), Color.Black, Glow * 0.2f);
                batch.FillRectangle(Rect(piece.X + (piece.Width * 0.3f), piece.Y + (piece.Height / 2), piece.Width * 0.3f, piece.Height * 0.22f), bone);
                batch.FillCircle(Screen(piece.Right - 5, piece.Y + (piece.Height * 0.38f)), 5 * _scale, bone);
                batch.FillCircle(Screen(piece.Right - 5, piece.Y + (piece.Height * 0.62f)), 5 * _scale, bone);
                break;
            }

            case Cut.Wing:
                batch.FillEllipse(Screen(piece.X - (piece.Width * 0.18f), piece.Y + (piece.Height / 2)), new Vector2(piece.Width * 0.32f, piece.Height * 0.5f) * _scale, dark);
                batch.FillEllipse(Screen(piece.X + (piece.Width * 0.2f), piece.Y + (piece.Height * 0.55f)), new Vector2(piece.Width * 0.3f, piece.Height * 0.42f) * _scale, dark);
                batch.FillEllipse(Screen(piece.X - (piece.Width * 0.18f), piece.Y + (piece.Height * 0.45f)), new Vector2(piece.Width * 0.3f, piece.Height * 0.44f) * _scale, color);
                batch.FillEllipse(Screen(piece.X + (piece.Width * 0.2f), piece.Y + (piece.Height * 0.5f)), new Vector2(piece.Width * 0.28f, piece.Height * 0.36f) * _scale, color);
                break;
            default:
                batch.FillRoundedRectangle(body, (int)(piece.Height * 0.45f * _scale), dark);
                var top = body;
                top.Height -= (int)(4 * _scale);
                batch.FillRoundedRectangle(top, (int)(piece.Height * 0.45f * _scale), color);
                break;
        }

        // Too hot to stand on: an orange line along the top, so it shows even as the colours shift.
        if (piece.Landed && piece.IsTooHot && !piece.IsCharred)
        {
            batch.FillRectangle(Rect(piece.X, piece.Y + 1, piece.Width * 0.7f, 3), new Color(255, 120, 40, 200));
        }
    }

    private void DrawCook(SpriteBatch batch)
    {
        // A small cook in whites and a tall hat; hopping and red-footed while the feet burn.
        var hop = Game.IsBurning ? MathF.Abs(MathF.Sin(_flicker * 30)) * 4 : 0;
        var x = Game.CookX;
        var feet = Game.CookY - hop;
        var face = Game.Facing;
        var shoe = Game.IsBurning ? new Color(240, 70, 40) : new Color(40, 40, 46);

        batch.FillEllipse(Screen(x, Game.CookY + 1), new Vector2(16, 4) * _scale, Palette.Shadow);
        batch.FillEllipse(Screen(x - 6, feet - 3), new Vector2(7, 4) * _scale, shoe);
        batch.FillEllipse(Screen(x + 6, feet - 3), new Vector2(7, 4) * _scale, shoe);
        batch.FillRoundedRectangle(Rect(x, feet - 17, 26, 26), (int)(9 * _scale), new Color(246, 246, 250));
        batch.FillRectangle(Rect(x, feet - 14, 26, 3), new Color(220, 60, 60));
        batch.FillCircle(Screen(x, feet - 36), 11 * _scale, new Color(250, 214, 180));
        batch.FillCircle(Screen(x + (face * 4), feet - 38), 1.8f * _scale, Palette.Ink);
        batch.FillCircle(Screen(x + (face * 9), feet - 35), 2.2f * _scale, new Color(240, 150, 130));

        // The hat: a band and a puff on top.
        batch.FillRectangle(Rect(x, feet - 48, 18, 8), Color.White);
        batch.FillCircle(Screen(x - 5, feet - 55), 7 * _scale, Color.White);
        batch.FillCircle(Screen(x + 5, feet - 55), 7 * _scale, Color.White);
        batch.FillCircle(Screen(x, feet - 60), 7 * _scale, Color.White);
    }

    private void DrawHud(SpriteBatch batch, Rectangle hud, int margin)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"{(int)Game.Time.TotalMinutes}:{Game.Time.Seconds:00}"), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, Game.Score.ToString("N0", Culture), text, Palette.Accent, Align.Right);

        // Health in the middle, red as it runs out, and the pan's temperature beside it.
        var width = Math.Min(hud.Width / 3, fonts.Px(320));
        var bar = new Rectangle(hud.Center.X - (width / 2), hud.Center.Y - fonts.Px(10), width, fonts.Px(20));
        batch.FillRoundedRectangle(bar, bar.Height / 2, Palette.Ink);
        var fill = bar with { Width = (int)(bar.Width * Game.Health / HotPanGame.MaxHealth) };
        if (fill.Width > 0)
        {
            batch.FillRoundedRectangle(fill, bar.Height / 2, Color.Lerp(new Color(232, 72, 85), new Color(110, 204, 120), Game.Health / HotPanGame.MaxHealth));
        }

        var pan = string.Create(CultureInfo.InvariantCulture, $"{Game.PanTemperature:0}°C");
        batch.DrawText(fonts.Small, pan, new Rectangle(bar.Right + margin, hud.Y, fonts.Px(120), hud.Height), Color.Lerp(Palette.Muted, new Color(255, 120, 40), Glow), Align.Left);
    }

    private Rectangle Rect(float centreX, float centreY, float width, float height)
    {
        var at = Screen(centreX - (width / 2), centreY - (height / 2));
        return new Rectangle((int)at.X, (int)at.Y, (int)MathF.Ceiling(width * _scale), (int)MathF.Ceiling(height * _scale));
    }

    private Vector2 Screen(float x, float y) => _offset + (new Vector2(x, y) * _scale);

    private Vector2 ToField(Vector2 screen) => (screen - _offset) / _scale;
}

internal sealed class HotPanPauseScene : PanelScene
{
    private readonly HotPanScene _game;

    public HotPanPauseScene(GameContext context, HotPanScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("New game"), () =>
            {
                Scenes.Pop();
                _game.NewGame();
            }),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => "Hot Pan";

    protected override IReadOnlyList<PanelLine> Lines => [new(T("Score {0:N0}", _game.Game.Score), Palette.Muted)];
}

internal sealed class HotPanOverScene : PanelScene
{
    private readonly HotPanScene _game;
    private readonly int? _place;

    public HotPanOverScene(GameContext context, HotPanScene game)
        : base(context)
    {
        _game = game;
        _place = game.Game.Score > 0 ? context.ScoreBoard.Submit(game.Game.Score) : null;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("New game"), () =>
            {
                Scenes.Pop();
                _game.NewGame();
            }),
            new MenuItem(T("Leaderboard"), () => Scenes.Push(new LeaderboardScene(context))),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => _place is not null ? T("New high score!") : T("Your feet are done");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(T("Score {0:N0}", _game.Game.Score), Palette.Text),
        new(T("You lasted {0}", string.Create(CultureInfo.InvariantCulture, $"{(int)_game.Game.Time.TotalMinutes}:{_game.Game.Time.Seconds:00}")), Palette.Muted),
        _place is { } place ? new(T("Number {0} on this device", place + 1), Palette.Accent) : new(T("The pan reached {0}°C", (int)_game.Game.PanTemperature), Palette.Muted),
    ];

    protected override void OnBack() => Navigation.ToMainMenu(Scenes, Context);
}
