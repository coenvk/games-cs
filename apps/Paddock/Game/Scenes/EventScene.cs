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

/// <summary>One event: its ladder of twelve matches with the rewards along it, the tickets, and Race.</summary>
internal sealed class EventScene : Scene
{
    private readonly GameContext _context;
    private readonly LiveEvent _event;
    private readonly ButtonBar _buttons;

    public EventScene(GameContext context, LiveEvent ev)
    {
        _context = context;
        _event = ev;
        _buttons = new ButtonBar(new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())), new Button(T("Race"), Race, Highlight: true));
    }

    private PaddockStore Store => _context.Paddock;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        if (!_buttons.Update(input) && _context.Actions.WasPressed(input, GameAction.Confirm))
        {
            Race();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var progress = EventPlay.Progress(Store.Profile, _event, Store.Now);
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T(_event.Def.Name), new Vector2(viewport.Center.X, viewport.Y + pad), Palette.Accent, Align.Center);
        var y = viewport.Y + pad + (int)fonts.Heading.LineHeight;
        var info = $"{EventsScene.Tickets(progress, Store.Now)} · {T("Ends in {0}", Format.Duration(_event.Ends - Store.Now))}";
        batch.DrawText(fonts.Small, info, new Vector2(viewport.Center.X, y), Palette.Text, Align.Center, shadow: false);
        y += (int)fonts.Small.LineHeight;
        batch.DrawText(fonts.Small, string.Join(" · ", _event.Def.Rules.Describe(T, Words.Name)), new Vector2(viewport.Center.X, y), Palette.Muted, Align.Center, shadow: false);
        y += (int)fonts.Small.LineHeight + pad;

        // The ladder: twelve steps, done ones lit, rewards at 4, 8 and 12.
        var buttonsHeight = ButtonBar.Height(fonts.Body) + pad;
        var ladder = new Rectangle(viewport.X + pad, y, viewport.Width - (2 * pad), viewport.Bottom - y - buttonsHeight - pad);
        var columns = viewport.Height > viewport.Width ? 3 : 6;
        var rows = EventPlay.Rungs / columns;
        var gap = fonts.Px(10);
        var cell = new Point((ladder.Width - ((columns - 1) * gap)) / columns, Math.Min(fonts.Px(110), (ladder.Height - ((rows - 1) * gap)) / rows));
        for (var i = 0; i < EventPlay.Rungs; i++)
        {
            var box = new Rectangle(ladder.X + (i % columns * (cell.X + gap)), ladder.Y + (i / columns * (cell.Y + gap)), cell.X, cell.Y);
            var done = i < progress.Rung;
            var next = i == progress.Rung;
            batch.FillRectangle(box, done ? new Color(56, 128, 84) : next ? Palette.PanelLight : Palette.Panel);
            if (next)
            {
                batch.StrokeRectangle(box, Palette.Accent, Math.Max(2, fonts.Px(3)));
            }

            batch.DrawText(fonts.Body, (i + 1).ToString(Culture), new Vector2(box.X + fonts.Px(10), box.Y + fonts.Px(4)), Palette.Text, shadow: false);
            var reward = (i + 1) switch
            {
                4 => T(Packs.Starter.Name),
                8 => T(Packs.Classic.Name),
                EventPlay.Rungs => T("Prize car"),
                _ => null,
            };
            if (reward is not null)
            {
                batch.DrawText(fonts.Small, reward, new Vector2(box.X + fonts.Px(10), box.Bottom - fonts.Small.LineHeight - fonts.Px(6)), Palette.Accent, shadow: false);
            }
        }

        var bar = new Rectangle(viewport.X, viewport.Bottom - buttonsHeight, viewport.Width, ButtonBar.Height(fonts.Body));
        _buttons.Draw(batch, fonts.Body, bar);
    }

    private void Race()
    {
        var profile = Store.Profile;
        var data = _context.PaddockData;
        if (!EventPlay.CanEnter(profile, _event, data.Roster, Store.Now, out var why))
        {
            _context.Sounds.Play(Sound.Error);
            var extra = why == "No tickets left" ? EventsScene.Tickets(EventPlay.Progress(profile, _event, Store.Now), Store.Now) : string.Empty;
            Scenes.Push(new MessageScene(_context, T("Can't race now"), T(why!), extra));
            return;
        }

        var rung = EventPlay.Progress(profile, _event, Store.Now).Rung;
        var setup = EventPlay.Next(_event, rung, data.Roster, data.Tracks);
        var rules = _event.Def.Rules;
        Scenes.Push(new MatchSetupScene(_context, T(_event.Def.Name), setup, rules.HandRqLimit, m => rules.Allows(m, data.Roster.Rq(m.Id)), order =>
        {
            var cars = order.Select(id => profile.Car(id)!).ToList();
            var outcome = EventPlay.Race(profile, _event, order, data.Roster, data.Tracks, Store.Now, new Random());
            Store.Save();
            Scenes.Replace(new RaceScene(_context, setup, cars, outcome.Result, () => Rewards.Show(Scenes, _context, outcome.Result.PlayerWon, outcome.Cash, outcome.Xp, outcome.NewCars)));
        }));
    }
}
