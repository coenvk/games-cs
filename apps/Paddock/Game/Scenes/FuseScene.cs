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

/// <summary>Picks the car to fuse into another: any other car of the same class. The picked car is used up.</summary>
internal sealed class FuseScene : Scene
{
    private readonly GameContext _context;
    private readonly Guid _targetId;
    private readonly CardGrid _grid;
    private readonly ButtonBar _buttons;

    public FuseScene(GameContext context, Guid targetId)
    {
        _context = context;
        _targetId = targetId;
        var roster = context.PaddockData.Roster;
        var profile = context.Paddock.Profile;
        var target = profile.Car(targetId)!;
        var rarity = roster.Class(target.ModelId);
        _grid = new CardGrid(context, [.. profile.Garage.Where(c => c.Id != targetId && roster.Class(c.ModelId) == rarity).OrderBy(c => c.Tune.Total)]);
        _buttons = new ButtonBar(new Button(T("Back"), () => Scenes.Pop()));
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_buttons.Update(input) || _grid.Update(input) is not { } donor)
        {
            return;
        }

        var name = _context.PaddockData.Roster.Find(donor.ModelId)!.Name;
        Scenes.Push(MessageScene.Confirm(_context, T("Fuse"), T("Use up the {0}? It leaves your garage.", name), T("Fuse"), () =>
        {
            if (Workshop.Fuse(_context.Paddock.Profile, _targetId, donor.Id, _context.PaddockData.Roster))
            {
                _context.Paddock.Save();
                _context.Sounds.Play(Sound.Fanfare);
                Scenes.Pop();
            }
        }));
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Fuse"), new Vector2(viewport.X + pad, viewport.Y + pad), Palette.Accent);
        var y = viewport.Y + pad + (int)fonts.Heading.LineHeight;
        var hint = _grid.Cars.Count == 0 ? T("You have no other car of this class. Packs and events bring more.") : T("Pick a car of the same class to fuse in.");
        batch.DrawText(fonts.Small, hint, new Vector2(viewport.X + pad, y), Palette.Muted, shadow: false);
        y += (int)fonts.Small.LineHeight;
        var bar = new Rectangle(viewport.X, y, viewport.Width, ButtonBar.Height(fonts.Small) + fonts.Px(10));
        _buttons.Draw(batch, fonts.Small, bar);
        _grid.Draw(batch, new Rectangle(viewport.X + pad, bar.Bottom, viewport.Width - (2 * pad), viewport.Bottom - bar.Bottom), fonts.Px(250));
    }
}
