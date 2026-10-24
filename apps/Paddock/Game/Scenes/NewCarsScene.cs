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

/// <summary>Cars just won or unpacked, shown one card at a time; a car the player had none of yet is marked New.</summary>
internal sealed class NewCarsScene : Scene
{
    private readonly GameContext _context;
    private readonly IReadOnlyList<CarInstance> _cars;
    private readonly HashSet<string> _new;
    private int _index;
    private double _shown;

    public NewCarsScene(GameContext context, IReadOnlyList<CarInstance> cars)
    {
        _context = context;
        _cars = cars;
        var ids = cars.Select(c => c.Id).ToHashSet();
        var owned = context.Paddock.Profile.Garage.Where(c => !ids.Contains(c.Id)).Select(c => c.ModelId).ToHashSet();
        _new = [.. cars.Select(c => c.ModelId).Where(m => !owned.Contains(m))];
        context.Sounds.Play(Sound.Fanfare);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _shown += elapsed.TotalSeconds;
        if (input.Tap is not null || _context.Actions.WasPressed(input, GameAction.Confirm) || _context.Actions.WasPressed(input, GameAction.Back))
        {
            if (++_index >= _cars.Count)
            {
                Scenes.Pop();
                return;
            }

            _shown = 0;
            _context.Sounds.Play(Sound.Card);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, new Color(0, 0, 0, 200));
        var car = _cars[Math.Min(_index, _cars.Count - 1)];
        var title = _new.Contains(car.ModelId) ? T("New car!") : T("Another one for the garage");
        batch.DrawText(fonts.Heading, title, new Vector2(viewport.Center.X, viewport.Y + fonts.Px(16)), Palette.Accent, Align.Center);

        // The card grows in over a moment.
        var grow = (float)Math.Min(1, 0.7 + (_shown * 1.5));
        var height = (int)((viewport.Height - fonts.Heading.LineHeight - fonts.Small.LineHeight - fonts.Px(60)) * grow);
        var size = CarCard.Size((int)(height * CarCard.Aspect));
        var bounds = new Rectangle(viewport.Center.X - (size.X / 2), viewport.Y + (int)fonts.Heading.LineHeight + fonts.Px(24), size.X, size.Y);
        batch.Restart(sampler: SamplerState.PointClamp);
        CarCard.Draw(batch, fonts, _context.PaddockData, car, bounds);
        batch.Restart();
        var hint = _cars.Count > 1 ? T("{0} of {1} · tap to go on", _index + 1, _cars.Count) : T("Tap to go on");
        batch.DrawText(fonts.Small, hint, new Vector2(viewport.Center.X, viewport.Bottom - fonts.Small.LineHeight - fonts.Px(10)), Palette.Muted, Align.Center, shadow: false);
    }
}
