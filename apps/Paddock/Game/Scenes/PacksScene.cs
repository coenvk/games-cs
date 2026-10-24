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

/// <summary>The packs on sale, with their prices and odds; buying one reveals its cars one by one.</summary>
internal sealed class PacksScene : Scene
{
    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly Rectangle[] _tiles = new Rectangle[Packs.All.Count];
    private int _selected;

    public PacksScene(GameContext context)
    {
        _context = context;
        _buttons = new ButtonBar(new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())));
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (actions.WasPressed(input, GameAction.Left) || actions.WasPressed(input, GameAction.Up))
        {
            _selected = Math.Max(0, _selected - 1);
        }
        else if (actions.WasPressed(input, GameAction.Right) || actions.WasPressed(input, GameAction.Down))
        {
            _selected = Math.Min(Packs.All.Count - 1, _selected + 1);
        }

        for (var i = 0; i < _tiles.Length; i++)
        {
            if (input.Tapped(_tiles[i]))
            {
                _selected = i;
                Buy(Packs.All[i]);
                return;
            }
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Buy(Packs.All[_selected]);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Packs"), new Vector2(viewport.X + pad, viewport.Y + pad), Palette.Accent);
        var cash = T("Cash: {0}", _context.Paddock.Profile.Cash.ToString("#,0", Culture));
        batch.DrawText(fonts.Body, cash, new Vector2(viewport.Right - pad, viewport.Y + pad + fonts.Px(8)), Palette.Text, Align.Right);
        var top = viewport.Y + pad + (int)fonts.Heading.LineHeight + pad;
        var bottom = viewport.Bottom - ButtonBar.Height(fonts.Body) - (2 * pad);
        var area = new Rectangle(viewport.X + pad, top, viewport.Width - (2 * pad), bottom - top);
        var portrait = viewport.Height > viewport.Width;
        var gap = fonts.Px(16);
        for (var i = 0; i < Packs.All.Count; i++)
        {
            var pack = Packs.All[i];
            var tile = portrait
                ? new Rectangle(area.X, area.Y + (i * (area.Height + gap) / Packs.All.Count), area.Width, (area.Height / Packs.All.Count) - gap)
                : new Rectangle(area.X + (i * (area.Width + gap) / Packs.All.Count), area.Y, (area.Width / Packs.All.Count) - gap, area.Height);
            _tiles[i] = tile;
            var best = pack.Weights.Keys.Max();
            batch.FillRectangle(tile, Palette.Panel);
            batch.FillRectangle(new Rectangle(tile.X, tile.Y, tile.Width, fonts.Px(8)), CarCard.ColorOf(best));
            if (i == _selected)
            {
                batch.StrokeRectangle(tile, Palette.Accent, Math.Max(2, fonts.Px(3)));
            }

            var y = tile.Y + fonts.Px(20);
            batch.DrawText(fonts.Body, T(pack.Name), new Vector2(tile.Center.X, y), Palette.Accent, Align.Center);
            y += (int)fonts.Body.LineHeight;
            batch.DrawText(fonts.Small, N(pack.Cars, "{0} car", "{0} cars", pack.Cars), new Vector2(tile.Center.X, y), Palette.Text, Align.Center, shadow: false);
            y += (int)fonts.Small.LineHeight + fonts.Px(6);
            var total = pack.Weights.Values.Sum();
            foreach (var (rarity, weight) in pack.Weights.OrderBy(w => w.Key))
            {
                var odds = T("Class {0}: {1:0}%", rarity, weight * 100 / total);
                batch.DrawText(fonts.Small, odds, new Vector2(tile.Center.X, y), CarCard.ColorOf(rarity), Align.Center, shadow: false);
                y += (int)fonts.Small.LineHeight;
            }

            var price = pack.Price.ToString("#,0", Culture);
            batch.DrawText(fonts.Body, price, new Vector2(tile.Center.X, tile.Bottom - fonts.Body.LineHeight - fonts.Px(12)), _context.Paddock.Profile.Cash >= pack.Price ? Palette.Text : Palette.Muted, Align.Center);
        }

        var bar = new Rectangle(viewport.X, viewport.Bottom - ButtonBar.Height(fonts.Body) - pad, viewport.Width, ButtonBar.Height(fonts.Body));
        _buttons.Draw(batch, fonts.Body, bar);
    }

    private void Buy(PackDef pack)
    {
        var store = _context.Paddock;
        if (!Packs.Buy(store.Profile, pack, _context.PaddockData.Roster, new Random(), out var cars))
        {
            _context.Sounds.Play(Sound.Error);
            Scenes.Push(new MessageScene(_context, T("Not enough cash"), T("Win races or events to earn more.")));
            return;
        }

        store.Save();
        Scenes.Push(new NewCarsScene(_context, cars));
    }
}
