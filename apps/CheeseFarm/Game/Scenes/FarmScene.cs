using System.Globalization;
using CheeseFarm;
using Games.Input;
using Games.UI;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The farm by day: the meadow with the herd, the barn with its cheese kitchen and cellar, the shop by the road.
/// From here: make a cheese, see to the cellar, open the shop, buy for the farm, or sleep until the next milking.
/// </summary>
internal sealed class FarmScene : Scene
{
    private static readonly TimeSpan NoteTime = TimeSpan.FromSeconds(5);

    private readonly GameContext _context;
    private readonly KeyedButtons _buttons = new();
    private string? _note;
    private TimeSpan _noteLeft;
    private float _time;

    public FarmScene(GameContext context)
    {
        _context = context;
        SetButtons();
    }

    private Farm Farm => _context.Farm.Farm;

    public override IEnumerable<string> DebugInfo => [Farm.ToString()];

    protected override void OnEnter() => _context.PlayMood(Music.Village);

    protected override void OnUncovered()
    {
        _context.PlayMood(Music.Village);
        SetButtons();
    }

    public void Say(string note)
    {
        _note = note;
        _noteLeft = NoteTime;
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _time += (float)elapsed.TotalSeconds;
        _noteLeft -= elapsed;
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new FarmPauseScene(_context));
            return;
        }

        _buttons.Update(input);
    }

    private void SetButtons() => _buttons.Set(
    [
        .. Farm.CanEnterFair ? [new Button(T("Fair"), () => Scenes.Push(new FairScene(_context)), Highlight: true)] : Array.Empty<Button>(),
        new Button(T("Make cheese"), MakeCheese, Highlight: Farm.CanMake),
        new Button(Farm.Due > 0 ? T("Cellar ({0})", Farm.Due) : T("Cellar"), () => Scenes.Push(new CellarScene(_context), Navigation.Fade())),
        new Button(T("Shop"), () => Scenes.Push(new ShopScene(_context))),
        new Button(T("Buy"), () => Scenes.Push(new FarmShopScene(_context))),
        new Button(T("Sleep"), Sleep),
        new Button(T("Menu"), () => Scenes.Push(new FarmPauseScene(_context))),
    ]);

    private void MakeCheese()
    {
        if (Farm.Milk < Farm.MilkPerWheel)
        {
            _context.Sounds.Play(Sound.Error);
            Say(T("Not enough milk: the cows are milked in the morning"));
            return;
        }

        if (Farm.Wheels.Count >= Farm.Room)
        {
            _context.Sounds.Play(Sound.Error);
            Say(T("The cellar is full: sell some cheese, or buy a rack"));
            return;
        }

        Scenes.Push(new KitchenScene(_context), Navigation.Fade());
    }

    private void Sleep()
    {
        var report = Farm.NextDay();
        _context.Farm.Save();
        _context.Sounds.Play(Sound.Completed);
        var note = T("Day {0}: the cows gave {1} litres", report.Day, report.Milked);
        if (report.ToDairy > 0)
        {
            note += "  ·  " + T("{0} litres to the dairy for €{1}", report.ToDairy, report.DairyPay);
        }

        if (Fair.IsOn(report.Day))
        {
            note += "  ·  " + T("The cheese fair is today!");
        }

        if (report.Neglected > 0)
        {
            note += "  ·  " + (report.Neglected == 1 ? T("1 wheel went unturned") : T("{0} wheels went unturned", report.Neglected));
        }

        Say(note);
        SetButtons();
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(14);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(60));
        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var land = new Rectangle(viewport.X, hud.Bottom, viewport.Width, buttons.Top - hud.Bottom);

        DrawLand(batch, land);
        DrawHud(batch, hud, margin);
        if (_note is not null && _noteLeft > TimeSpan.Zero)
        {
            var size = fonts.Small.MeasureString(_note);
            var box = new Rectangle(land.Center.X - ((int)size.X / 2) - margin, land.Y + margin, (int)size.X + (2 * margin), (int)size.Y + margin);
            batch.FillRoundedRectangle(box, fonts.Px(8), Palette.Panel * 0.92f);
            batch.DrawText(fonts.Small, _note, box, Palette.Accent, Align.Center, shadow: false);
        }

        batch.FillRectangle(new Rectangle(viewport.X, buttons.Y - (margin / 2), viewport.Width, viewport.Bottom - buttons.Y + (margin / 2)), Palette.Background);
        _buttons.Draw(batch, fonts.Small, buttons);
    }

    private void DrawHud(SpriteBatch batch, Rectangle hud, int margin)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, T("Day {0}", Farm.Day), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"€ {Farm.Money}"), text, Palette.Accent, Align.Right);
        var status = T("{0} litres of milk  ·  {1} of {2} wheels in the cellar  ·  {3} cows", Farm.Milk, Farm.Wheels.Count, Farm.Room, Farm.Cows);
        batch.DrawText(fonts.Small, status, text, Palette.Muted, Align.Center, shadow: false);
    }

    /// <summary>Sky, meadow and a ditch; the barn and the shop at the back; the herd grazing in front.</summary>
    private void DrawLand(SpriteBatch batch, Rectangle land)
    {
        var horizon = land.Y + (int)(land.Height * 0.38f);
        batch.FillRectangle(new Rectangle(land.X, land.Y, land.Width, horizon - land.Y), new Color(150, 196, 230));
        batch.FillRectangle(new Rectangle(land.X, horizon, land.Width, land.Bottom - horizon), new Color(104, 162, 76));

        // A row of trees along the horizon, as round as the Achterhoek's oaks.
        var tree = Math.Max(10, land.Height / 14);
        for (var x = land.X - tree; x < land.Right + tree; x += (int)(tree * 1.6f))
        {
            var wobble = ((x * 37) % 11) - 5;
            batch.FillCircle(new Vector2(x, horizon - (tree * 0.6f) + wobble), tree, new Color(66, 112, 58));
        }

        // The barn, with its cheese kitchen and cellar, and the shop by the road.
        var unit = land.Height / 10f;
        var barn = new Rectangle(land.X + (int)(land.Width * 0.12f), horizon - (int)(unit * 2.4f), (int)(unit * 4.2f), (int)(unit * 2.6f));
        batch.FillRectangle(barn, new Color(160, 60, 48));
        batch.FillTriangle(new Rectangle(barn.X - (int)(unit * 0.3f), barn.Y - (int)(unit * 1.6f), barn.Width + (int)(unit * 0.6f), (int)(unit * 1.6f)), pointsUp: true, new Color(70, 62, 60));
        batch.FillRectangle(new Rectangle(barn.Center.X - (int)(unit * 0.5f), barn.Bottom - (int)(unit * 1.4f), (int)unit, (int)(unit * 1.4f)), new Color(110, 40, 34));
        batch.DrawText(_context.Fonts.Small, T("Cheese"), new Vector2(barn.Center.X, barn.Y + (unit * 0.3f)), new Color(250, 236, 200), Align.Center, shadow: false);

        // The fair's rosettes on the barn wall: gold, silver, bronze.
        Color[] rosette = [new(240, 196, 60), new(200, 204, 212), new(200, 130, 70)];
        var pinned = 0;
        for (var place = 0; place < 3; place++)
        {
            for (var n = 0; n < Math.Min(Farm.Rosettes[place], 4); n++, pinned++)
            {
                var at = new Vector2(barn.X + (unit * (0.45f + (pinned % 6 * 0.55f))), barn.Y + (unit * (1.0f + (pinned / 6 * 0.6f))));
                batch.FillTriangle(new Rectangle((int)(at.X - (unit * 0.12f)), (int)at.Y, (int)(unit * 0.24f), (int)(unit * 0.35f)), pointsUp: false, new Color(60, 90, 160));
                batch.FillCircle(at, unit * 0.2f, rosette[place]);
                batch.FillCircle(at, unit * 0.09f, Color.Lerp(rosette[place], Color.White, 0.4f));
            }
        }

        var shop = new Rectangle(land.X + (int)(land.Width * 0.62f), horizon - (int)(unit * 1.5f), (int)(unit * 2.4f), (int)(unit * 1.5f));
        batch.FillRectangle(shop, new Color(230, 220, 196));
        batch.FillTriangle(new Rectangle(shop.X - (int)(unit * 0.2f), shop.Y - (int)unit, shop.Width + (int)(unit * 0.4f), (int)unit), pointsUp: true, new Color(70, 62, 60));
        DrawWheel(batch, new Vector2(shop.Center.X, shop.Center.Y + (unit * 0.1f)), unit * 0.45f, Ripeness.Matured);
        for (var sign = 0; sign < Farm.Signs; sign++)
        {
            var post = new Vector2(shop.Right + (unit * (0.8f + (sign * 1.1f))), horizon + (unit * 0.4f));
            batch.FillRectangle(new Rectangle((int)post.X - 2, (int)(post.Y - unit), 4, (int)unit), new Color(90, 70, 50));
            batch.FillRectangle(new Rectangle((int)(post.X - (unit * 0.45f)), (int)(post.Y - (unit * 1.3f)), (int)(unit * 0.9f), (int)(unit * 0.5f)), new Color(250, 236, 200));
        }

        // The herd, each cow in its own spot and grazing at its own pace.
        var meadow = new Rectangle(land.X, horizon + (int)unit, land.Width, land.Bottom - horizon - (int)unit);
        for (var cow = 0; cow < Farm.Cows; cow++)
        {
            // Spread across the meadow, each nudged from its place a little; a new cow joins somewhere new.
            var random = new Random(HashCode.Combine(cow, 7919));
            var across = ((cow * 0.618034f) + 0.1f) % 1;
            var at = new Vector2(meadow.X + (meadow.Width * (0.06f + (0.88f * across))), meadow.Y + (meadow.Height * (0.1f + (0.7f * (float)random.NextDouble()))));
            var size = unit * (0.75f + (0.35f * (at.Y - meadow.Y) / Math.Max(1, meadow.Height)));
            DrawCow(batch, at, size, (cow % 3) != 1, _time + cow);
        }
    }

    /// <summary>A black-and-white cow: body, patches, head lowered to graze now and then, four legs.</summary>
    private static void DrawCow(SpriteBatch batch, Vector2 at, float size, bool facingRight, float time)
    {
        var face = facingRight ? 1 : -1;
        var graze = (MathF.Sin(time * 0.7f) + 1) / 2;
        var legs = new Color(40, 40, 40);
        foreach (var leg in new[] { -0.7f, -0.4f, 0.4f, 0.7f })
        {
            batch.FillRectangle(new Rectangle((int)(at.X + (leg * size)), (int)(at.Y + (size * 0.3f)), Math.Max(2, (int)(size * 0.14f)), (int)(size * 0.55f)), legs);
        }

        batch.FillEllipse(at, new Vector2(size, size * 0.5f), new Color(246, 244, 238));
        batch.FillEllipse(at + new Vector2(-0.35f * size * face, -0.1f * size), new Vector2(size * 0.35f, size * 0.28f), new Color(36, 36, 40));
        batch.FillEllipse(at + new Vector2(0.25f * size * face, 0.12f * size), new Vector2(size * 0.25f, size * 0.2f), new Color(36, 36, 40));
        var head = at + new Vector2(1.05f * size * face, (-0.25f + (0.45f * graze)) * size);
        batch.FillEllipse(head, new Vector2(size * 0.3f, size * 0.24f), new Color(246, 244, 238));
        batch.FillEllipse(head + new Vector2(0.18f * size * face, 0.08f * size), new Vector2(size * 0.14f, size * 0.12f), new Color(232, 170, 170));
        batch.FillCircle(head + new Vector2(0.02f * size * face, -0.08f * size), Math.Max(1.5f, size * 0.04f), new Color(30, 30, 30));
    }

    /// <summary>A wheel of cheese seen from above, its rind coloured by its ripeness.</summary>
    public static void DrawWheel(SpriteBatch batch, Vector2 at, float radius, Ripeness ripeness, Flavour flavour = Flavour.Plain)
    {
        batch.FillCircle(at + new Vector2(radius * 0.08f), radius, new Color(0, 0, 0, 70));
        batch.FillCircle(at, radius, Cheese.Rind(ripeness));
        batch.FillCircle(at, radius * 0.82f, Color.Lerp(Cheese.Rind(ripeness), new Color(255, 236, 150), 0.45f));
        if (Cheese.Flecks(flavour) is { } fleck)
        {
            var random = new Random((int)flavour);
            for (var i = 0; i < 14; i++)
            {
                var angle = (float)(random.NextDouble() * MathF.Tau);
                var distance = (float)Math.Sqrt(random.NextDouble()) * radius * 0.72f;
                batch.FillCircle(at + new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance), Math.Max(1, radius * 0.05f), fleck);
            }
        }
    }
}

internal sealed class FarmPauseScene : PanelScene
{
    public FarmPauseScene(GameContext context)
        : base(context) => Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));

    protected override string Title => T("Cheese Farm");
}
