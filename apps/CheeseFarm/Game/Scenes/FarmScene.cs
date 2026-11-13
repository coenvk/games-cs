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
        .. Farm.CanGoToMarket ? [new Button(T("Market"), () => Scenes.Push(new MarketScene(_context)), Highlight: true)] : Array.Empty<Button>(),
        .. Farm.CanMilkByHand ? [new Button(T("Milk"), () => Scenes.Push(new MilkingScene(_context), Navigation.Fade()))] : Array.Empty<Button>(),
        new Button(T("Make cheese"), MakeCheese, Highlight: Farm.CanMake),
        new Button(Farm.Due > 0 ? T("Cellar ({0})", Farm.Due) : T("Cellar"), () => Scenes.Push(new CellarScene(_context), Navigation.Fade())),
        new Button(T("Shop"), () => Scenes.Push(new ShopScene(_context))),
        new Button(T("Buy"), () => Scenes.Push(new FarmShopScene(_context))),
        new Button(T("Meadow"), () => Scenes.Push(new MeadowScene(_context))),
        new Button(T("Sleep"), Sleep),
        new Button(T("Menu"), () => Scenes.Push(new FarmPauseScene(_context))),
    ]);

    private void MakeCheese()
    {
        if (Farm.Milk < Sizes.Milk(CheeseSize.Baby))
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

        if (report.NewSeason is { } season)
        {
            note += "  ·  " + season switch
            {
                Season.Summer => T("Summer: mow the fields for hay"),
                Season.Autumn => T("Autumn: is there hay enough for the winter?"),
                Season.Winter => T("Winter: the herd is in the barn, eating hay"),
                _ => T("Spring: the herd is out in the meadow again"),
            };
        }

        if (report.HayEaten > 0)
        {
            note += "  ·  " + T("{0} bales of hay eaten", report.HayEaten);
        }

        if (report.Hungry > 0)
        {
            note += "  ·  " + T("No money for feed: {0} cows went hungry", report.Hungry);
        }
        else if (report.FeedBought > 0)
        {
            note += "  ·  " + T("Feed bought for €{0}", report.FeedBought);
        }

        if (report.Calf is { } calf)
        {
            note += "  ·  " + T("{0} the calf was born!", calf);
        }

        if (report.Grown is { } grown)
        {
            note += "  ·  " + T("{0} has grown up and gives milk", grown);
        }

        if (report.Fledged > 0)
        {
            note += "  ·  " + T("{0} meadow bird chicks fledged!", report.Fledged);
        }

        if (Market.IsOn(report.Day))
        {
            note += "  ·  " + T("Market day in town!");
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
        batch.DrawText(fonts.Body, T("Day {0}  ·  {1}", Farm.Day, SeasonName(Farm.Season)), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"€ {Farm.Money}"), text, Palette.Accent, Align.Right);
        var calves = Farm.Herd.Count - Farm.Cows;
        var herd = calves == 0 ? T("{0} cows", Farm.Cows) : calves == 1 ? T("{0} cows and a calf", Farm.Cows) : T("{0} cows and {1} calves", Farm.Cows, calves);
        var status = T("{0} litres of milk  ·  {1} of {2} wheels in the cellar  ·  {3}", Farm.Milk, Farm.Wheels.Count, Farm.Room, herd);
        if (Farm.Hay > 0 || Farm.Season == Season.Winter)
        {
            status += "  ·  " + T("{0} bales of hay", Farm.Hay);
        }
        batch.DrawText(fonts.Small, status, text, Palette.Muted, Align.Center, shadow: false);
    }

    /// <summary>Sky, meadow and a ditch; the barn and the shop at the back; the herd grazing in front.</summary>
    private void DrawLand(SpriteBatch batch, Rectangle land)
    {
        var horizon = land.Y + (int)(land.Height * 0.38f);
        var season = Farm.Season;
        batch.FillRectangle(new Rectangle(land.X, land.Y, land.Width, horizon - land.Y), season == Season.Winter ? new Color(176, 190, 204) : new Color(150, 196, 230));
        batch.FillRectangle(new Rectangle(land.X, horizon, land.Width, land.Bottom - horizon), Grass(season, new Color(104, 162, 76)));

        // A row of trees along the horizon, as round as the Achterhoek's oaks.
        var tree = Math.Max(10, land.Height / 14);
        for (var x = land.X - tree; x < land.Right + tree; x += (int)(tree * 1.6f))
        {
            var wobble = ((x * 37) % 11) - 5;
            batch.FillCircle(new Vector2(x, horizon - (tree * 0.6f) + wobble), tree, season switch
            {
                Season.Autumn => ((x / tree) % 3) switch { 0 => new Color(196, 120, 50), 1 => new Color(170, 140, 60), _ => new Color(120, 110, 60) },
                Season.Winter => new Color(110, 100, 92),
                _ => new Color(66, 112, 58),
            });
        }

        // The barn, with its cheese kitchen and cellar, and the shop by the road.
        var unit = land.Height / 10f;
        var barn = new Rectangle(land.X + (int)(land.Width * 0.12f), horizon - (int)(unit * 2.4f), (int)(unit * 4.2f), (int)(unit * 2.6f));
        batch.FillRectangle(barn, new Color(160, 60, 48));
        batch.FillTriangle(new Rectangle(barn.X - (int)(unit * 0.3f), barn.Y - (int)(unit * 1.6f), barn.Width + (int)(unit * 0.6f), (int)(unit * 1.6f)), pointsUp: true, new Color(70, 62, 60));
        batch.FillRectangle(new Rectangle(barn.Center.X - (int)(unit * 0.5f), barn.Bottom - (int)(unit * 1.4f), (int)unit, (int)(unit * 1.4f)), new Color(110, 40, 34));
        batch.DrawText(_context.Fonts.Small, T("Cheese"), new Vector2(barn.Center.X, barn.Y + (unit * 0.3f)), new Color(250, 236, 200), Align.Center, shadow: false);

        // The hay stacked by the barn, a bale for every twenty; in winter the herd looks out of the barn door.
        var bale = new Vector2(unit * 0.7f, unit * 0.45f);
        for (var b = 0; b < Math.Min(10, (Farm.Hay + 19) / 20); b++)
        {
            var (column, row) = (b % 4, b / 4);
            var at = new Rectangle((int)(barn.Right + (unit * 0.15f) + (column * bale.X) + (row * bale.X / 2)), (int)(horizon - ((row + 1) * bale.Y)), (int)bale.X - 2, (int)bale.Y - 2);
            batch.FillRoundedRectangle(at, (int)(unit * 0.08f), new Color(222, 186, 92));
            batch.FillRectangle(new Rectangle(at.X, at.Center.Y, at.Width, Math.Max(1, (int)(unit * 0.04f))), new Color(170, 130, 60));
        }

        if (season == Season.Winter && Farm.Herd.Count > 0)
        {
            var door = new Rectangle(barn.Center.X - (int)(unit * 0.5f), barn.Bottom - (int)(unit * 1.4f), (int)unit, (int)(unit * 1.4f));
            for (var c = 0; c < Math.Min(2, Farm.Herd.Count); c++)
            {
                var head = new Vector2(door.X + (door.Width * (0.3f + (0.4f * c))), door.Y + (door.Height * 0.45f) + (MathF.Sin((_time * 0.8f) + c) * unit * 0.04f));
                batch.FillEllipse(head, new Vector2(unit * 0.22f, unit * 0.26f), new Color(246, 244, 238));
                batch.FillEllipse(head + new Vector2(0, unit * 0.16f), new Vector2(unit * 0.14f, unit * 0.1f), new Color(232, 170, 170));
                batch.FillCircle(head + new Vector2(-unit * 0.08f, -unit * 0.06f), Math.Max(1.5f, unit * 0.035f), new Color(30, 30, 30));
                batch.FillCircle(head + new Vector2(unit * 0.08f, -unit * 0.06f), Math.Max(1.5f, unit * 0.035f), new Color(30, 30, 30));
            }
        }

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

        // The meadow's fields, two rows of three with ditches between; the herd grazing in front.
        var meadow = new Rectangle(land.X, horizon + (int)(unit * 0.6f), land.Width, land.Bottom - horizon - (int)(unit * 0.6f));
        var fields = DrawFields(batch, meadow, unit);
        if (Seasons.Grazing(season))
        {
            DrawHerd(batch, fields, meadow, unit);
        }
    }

    /// <summary>The fields: grass, herbs in flower, or long grass with meadow birds. Their bounds, front row last.</summary>
    private Rectangle[] DrawFields(SpriteBatch batch, Rectangle meadow, float unit)
    {
        var bounds = new Rectangle[Farm.Fields.Count];
        var ditch = Math.Max(2, (int)(unit * 0.12f));
        var rows = (Farm.Fields.Count + 2) / 3;
        for (var i = 0; i < bounds.Length; i++)
        {
            var (column, row) = (i % 3, i / 3);
            var top = meadow.Y + (meadow.Height * row / rows);
            var bottom = meadow.Y + (meadow.Height * (row + 1) / rows);
            var field = new Rectangle(meadow.X + (meadow.Width * column / 3), top, meadow.Width / 3, bottom - top);
            field.Inflate(-ditch / 2, -ditch / 2);
            bounds[i] = field;

            var kind = Farm.Fields[i];
            var season = Farm.Season;
            batch.FillRectangle(field, Grass(season, kind switch
            {
                Field.Herbs => new Color(112, 168, 80),
                Field.Birds => new Color(132, 150, 70),
                _ => new Color(104, 162, 76),
            }));

            // The same flowers and tufts in the same places every frame.
            var random = new Random(HashCode.Combine(i, 31));
            if (kind == Field.Herbs && season is Season.Spring or Season.Summer)
            {
                Color[] flowers = [new(250, 220, 70), new(200, 120, 210), new(250, 250, 245), new(230, 90, 70)];
                var count = field.Width * field.Height / (int)Math.Max(1, unit * unit * 0.25f);
                for (var n = 0; n < count; n++)
                {
                    var at = new Vector2(field.X + (float)(random.NextDouble() * field.Width), field.Y + (float)(random.NextDouble() * field.Height));
                    batch.FillCircle(at, Math.Max(1.5f, unit * 0.06f), flowers[n % flowers.Length]);
                }
            }
            else if (kind == Field.Birds && season != Season.Winter)
            {
                var count = field.Width * field.Height / (int)Math.Max(1, unit * unit * 0.3f);
                for (var n = 0; n < count; n++)
                {
                    var at = new Vector2(field.X + (float)(random.NextDouble() * field.Width), field.Y + (float)(random.NextDouble() * field.Height));
                    var tuft = unit * 0.22f;
                    batch.DrawLine(at, at + new Vector2(-tuft * 0.3f, -tuft), new Color(96, 118, 50), Math.Max(1, unit * 0.04f));
                    batch.DrawLine(at, at + new Vector2(tuft * 0.3f, -tuft), new Color(96, 118, 50), Math.Max(1, unit * 0.04f));
                }

                // A godwit or two, bobbing, and one overhead calling: while they nest, spring and summer.
                for (var bird = 0; bird < (Seasons.Nesting(season) ? 2 : 0); bird++)
                {
                    var at = new Vector2(field.X + (field.Width * (0.3f + (0.4f * bird))), field.Y + (field.Height * (0.45f + (0.2f * bird))));
                    DrawGodwit(batch, at, unit * 0.5f, bird == 0, MathF.Sin((_time * 2.3f) + (i * 1.7f) + bird));
                }

                if (!Seasons.Nesting(season))
                {
                    continue;
                }

                var flying = new Vector2(field.Center.X + (MathF.Sin((_time * 0.4f) + i) * field.Width * 0.35f), field.Y - (unit * 0.4f) + (MathF.Sin(_time * 1.3f) * unit * 0.1f));
                var wing = MathF.Sin((_time * 8) + i) * unit * 0.12f;
                batch.DrawLine(flying, flying + new Vector2(-unit * 0.25f, -wing), new Color(60, 50, 40), Math.Max(1, unit * 0.05f));
                batch.DrawLine(flying, flying + new Vector2(unit * 0.25f, -wing), new Color(60, 50, 40), Math.Max(1, unit * 0.05f));
            }
        }

        return bounds;
    }

    /// <summary>A field's green as the year turns: fresh in spring, yellowing in autumn, frosted in winter.</summary>
    private static Color Grass(Season season, Color green) => season switch
    {
        Season.Summer => Color.Lerp(green, new Color(150, 170, 60), 0.2f),
        Season.Autumn => Color.Lerp(green, new Color(160, 140, 70), 0.45f),
        Season.Winter => Color.Lerp(green, new Color(220, 228, 232), 0.7f),
        _ => green,
    };

    public static string SeasonName(Season season) => season switch
    {
        Season.Spring => T("spring"),
        Season.Summer => T("summer"),
        Season.Autumn => T("autumn"),
        _ => T("winter"),
    };

    /// <summary>A black-tailed godwit: a rusty body on long legs, and a long beak dipping into the grass.</summary>
    private static void DrawGodwit(SpriteBatch batch, Vector2 at, float size, bool facingRight, float bob)
    {
        var face = facingRight ? 1 : -1;
        var legs = new Color(50, 46, 40);
        batch.DrawLine(at + new Vector2(-size * 0.1f, 0), at + new Vector2(-size * 0.15f, size * 0.7f), legs, Math.Max(1, size * 0.06f));
        batch.DrawLine(at + new Vector2(size * 0.1f, 0), at + new Vector2(size * 0.15f, size * 0.7f), legs, Math.Max(1, size * 0.06f));
        batch.FillEllipse(at, new Vector2(size * 0.5f, size * 0.28f), new Color(190, 110, 60));
        var head = at + new Vector2(size * 0.5f * face, (-0.25f + (0.15f * bob)) * size);
        batch.FillCircle(head, size * 0.15f, new Color(200, 120, 70));
        batch.DrawLine(head, head + new Vector2(size * 0.7f * face, size * (0.15f + (0.25f * bob))), new Color(80, 60, 50), Math.Max(1, size * 0.06f));
    }

    /// <summary>The herd, each cow in its own spot in a field it grazes, at its own pace; cows the meadow can't feed by the barn.</summary>
    private void DrawHerd(SpriteBatch batch, Rectangle[] fields, Rectangle meadow, float unit)
    {
        // A place for every cow a field feeds, grass and herbs alike; the rest stand along the top of the meadow.
        var places = new List<Rectangle>();
        for (var i = 0; i < fields.Length; i++)
        {
            places.AddRange(Enumerable.Repeat(fields[i], Meadow.Feeds(Farm.Fields[i])));
        }

        var spare = new Rectangle(meadow.X, meadow.Y, meadow.Width / 3, (int)unit);
        var cows = Enumerable.Range(0, Farm.Herd.Count)
            .Select(cow =>
            {
                var random = new Random(HashCode.Combine(cow, 7919));
                var field = cow < places.Count ? places[cow] : spare;
                var at = new Vector2(field.X + (field.Width * (0.15f + (0.7f * (float)random.NextDouble()))), field.Y + (field.Height * (0.2f + (0.6f * (float)random.NextDouble()))));
                return (cow, at);
            })
            .OrderBy(c => c.at.Y);
        foreach (var (cow, at) in cows)
        {
            var size = unit * (0.75f + (0.35f * (at.Y - meadow.Y) / Math.Max(1, meadow.Height)));

            // A calf is smaller, growing towards a cow's size over her weeks.
            var age = Farm.Day - Farm.Herd[cow].Born;
            if (age < Herd.CalfDays)
            {
                size *= 0.5f + (0.3f * age / Herd.CalfDays);
            }

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
