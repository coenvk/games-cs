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

/// <summary>Every car the player owns, as cards: sort and filter them, tap one to upgrade or fuse it.</summary>
internal sealed class GarageScene : Scene
{
    private readonly GameContext _context;
    private readonly CardGrid _grid;
    private readonly ButtonBar _buttons;
    private Sort _sort;
    private Rarity? _class;

    public GarageScene(GameContext context)
    {
        _context = context;
        _grid = new CardGrid(context, []);
        _buttons = new ButtonBar();
        Refresh();
    }

    private enum Sort
    {
        Rq,
        Name,
        Year,
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (_grid.Update(input) is { } car)
        {
            Scenes.Push(new CarScene(_context, car.Id));
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Garage"), new Vector2(viewport.X + pad, viewport.Y + pad), Palette.Accent);
        var count = N(_context.Paddock.Profile.Garage.Count, "{0} car", "{0} cars", _context.Paddock.Profile.Garage.Count);
        batch.DrawText(fonts.Small, count, new Vector2(viewport.Right - pad, viewport.Y + pad + fonts.Px(14)), Palette.Muted, Align.Right, shadow: false);

        var barTop = viewport.Y + pad + (int)fonts.Heading.LineHeight;
        var bar = new Rectangle(viewport.X, barTop, viewport.Width, ButtonBar.Height(fonts.Small) + fonts.Px(10));
        _buttons.Draw(batch, fonts.Small, bar);
        _grid.Draw(batch, new Rectangle(viewport.X + pad, bar.Bottom, viewport.Width - (2 * pad), viewport.Bottom - bar.Bottom), fonts.Px(250));
    }

    protected override void OnUncovered() => Refresh();

    // Re-reads the garage (upgrades and fuses change it) and the buttons' labels.
    private void Refresh()
    {
        var roster = _context.PaddockData.Roster;
        var cars = _context.Paddock.Profile.Garage.Where(c => roster.Find(c.ModelId) is not null && (_class is null || roster.Class(c.ModelId) == _class));
        cars = _sort switch
        {
            Sort.Name => cars.OrderBy(c => roster.Find(c.ModelId)!.Name, StringComparer.CurrentCulture),
            Sort.Year => cars.OrderBy(c => roster.Find(c.ModelId)!.Year),
            _ => cars.OrderByDescending(c => roster.Rq(c.ModelId)).ThenByDescending(c => c.Tune.Total),
        };
        _grid.Show([.. cars]);

        var sortLabel = _sort switch
        {
            Sort.Name => T("Name"),
            Sort.Year => T("Year"),
            _ => "RQ",
        };
        _buttons.Set(
            new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())),
            new Button(T("Sort: {0}", sortLabel), () =>
            {
                _sort = (Sort)(((int)_sort + 1) % 3);
                Refresh();
            }),
            new Button(T("Class: {0}", _class?.ToString() ?? T("All")), () =>
            {
                _class = _class switch
                {
                    null => Rarity.F,
                    Rarity.S => null,
                    { } c => c + 1,
                };
                Refresh();
            }));
    }
}
