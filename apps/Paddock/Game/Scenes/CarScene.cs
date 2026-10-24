using System.Globalization;
using Games.Input;
using Games.UI;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>One car, large, with the workshop beside it: buy upgrade points, fuse another car in, or reset the tune.</summary>
internal sealed class CarScene : Scene
{
    private readonly GameContext _context;
    private readonly Guid _carId;
    private Menu _menu;

    public CarScene(GameContext context, Guid carId)
    {
        _context = context;
        _carId = carId;
        _menu = BuildMenu();
    }

    private Profile Profile => _context.Paddock.Profile;

    private Roster Roster => _context.PaddockData.Roster;

    private CarInstance? Car => Profile.Car(_carId);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back) || Car is null)
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        if (Car is not { } car)
        {
            return;
        }

        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(24);
        var portrait = viewport.Height > viewport.Width;

        // The card on the left (on top in portrait), the workshop on the right (below).
        var cardHeight = portrait ? (viewport.Height / 2) - pad : viewport.Height - (2 * pad);
        var card = CarCard.Size((int)(cardHeight * CarCard.Aspect));
        var cardBounds = portrait
            ? new Rectangle(viewport.Center.X - (card.X / 2), viewport.Y + pad, card.X, card.Y)
            : new Rectangle(viewport.X + pad + Math.Max(0, ((viewport.Width / 2) - card.X) / 2), viewport.Y + pad, card.X, card.Y);
        batch.Restart(sampler: SamplerState.PointClamp);
        CarCard.Draw(batch, fonts, _context.PaddockData, car, cardBounds);
        batch.Restart();

        var side = portrait
            ? new Rectangle(viewport.X + pad, cardBounds.Bottom + pad, viewport.Width - (2 * pad), viewport.Bottom - cardBounds.Bottom - (2 * pad))
            : new Rectangle(viewport.Center.X, viewport.Y + pad, (viewport.Width / 2) - pad, viewport.Height - (2 * pad));
        var y = side.Y;
        var cash = T("Cash: {0}", Money(Profile.Cash));
        batch.DrawText(fonts.Body, cash, new Vector2(side.Center.X, y), Palette.Accent, Align.Center);
        y += (int)fonts.Body.LineHeight;
        var points = T("Stage {0}: {1} of {2} points", car.Stage, Math.Max(0, Upgrades.PointsInStage(car)), Upgrades.PointsPerStage);
        batch.DrawText(fonts.Small, points, new Vector2(side.Center.X, y), Palette.Muted, Align.Center, shadow: false);
        y += (int)fonts.Small.LineHeight + fonts.Px(12);
        _menu.Draw(batch, fonts.Body, side.Center.X, y, side.Bottom - y);
    }

    protected override void OnUncovered() => Rebuild();

    private static string Money(long cash) => cash.ToString("#,0", CultureInfo.InvariantCulture);

    private void Rebuild()
    {
        var selected = _menu.Selected;
        _menu = BuildMenu();
        _menu.Selected = Math.Min(selected, _menu.Items.Count - 1);
    }

    private Menu BuildMenu()
    {
        var items = new List<MenuItem>();
        if (Car is { } car)
        {
            var rarity = Roster.Class(car.ModelId);
            foreach (var area in Enum.GetValues<Area>())
            {
                var label = Upgrades.CanUpgrade(car, area)
                    ? T("{0} +1 ({1})", Words.Of(area), Money(Upgrades.Cost(car, area, rarity)))
                    : T("{0}: full", Words.Of(area));
                items.Add(new MenuItem(label, () => Upgrade(area)));
            }

            items.Add(new MenuItem(T("Fuse"), Fuse));
            if (car.Tune.Total > 0)
            {
                items.Add(new MenuItem(T("Reset tune"), () => Scenes.Push(MessageScene.Confirm(
                    _context, T("Reset tune"), T("Take every upgrade point back? You pay for them again."), T("Reset"), () =>
                    {
                        Workshop.Reset(Profile, _carId);
                        _context.Paddock.Save();
                        Rebuild();
                    }))));
            }
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        return new Menu(_context.Sounds, [.. items]);
    }

    private void Upgrade(Area area)
    {
        if (Car is not { } car || !Upgrades.CanUpgrade(car, area))
        {
            var why = Car is { } c && Upgrades.IsStageFull(c) ? T("This stage is full: fuse another car into it to go on.") : T("This area is full for this stage.");
            Scenes.Push(new MessageScene(_context, T("Can't upgrade"), why));
            return;
        }

        if (!Workshop.Upgrade(Profile, _carId, area, Roster))
        {
            _context.Sounds.Play(Sound.Error);
            Scenes.Push(new MessageScene(_context, T("Not enough cash"), T("Win races or events to earn more.")));
            return;
        }

        _context.Sounds.Play(Sound.LevelUp);
        _context.Paddock.Save();
        Rebuild();
    }

    private void Fuse()
    {
        if (Car is not { } car)
        {
            return;
        }

        if (car.Stage >= Upgrades.MaxStage)
        {
            Scenes.Push(new MessageScene(_context, T("Fuse"), T("This car is at its last stage.")));
        }
        else if (!Upgrades.IsStageFull(car))
        {
            Scenes.Push(new MessageScene(_context, T("Fuse"), T("Spend all of this stage's points first, then fuse another car into it.")));
        }
        else
        {
            Scenes.Push(new FuseScene(_context, _carId));
        }
    }
}
