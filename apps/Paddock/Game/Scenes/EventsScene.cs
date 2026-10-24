using Games.Input;
using FontStashSharp;
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

/// <summary>The events running now, side by side: each with its prize car, rules, tickets and time left.</summary>
internal sealed class EventsScene : Scene
{
    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly Rectangle[] _tiles = new Rectangle[EventCalendar.Slots];
    private IReadOnlyList<LiveEvent> _live = [];
    private int _selected;

    public EventsScene(GameContext context)
    {
        _context = context;
        _buttons = new ButtonBar(new Button(T("Back"), () => Scenes.Pop(Navigation.Fade())));
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _live = _context.PaddockData.Events.Live(_context.Paddock.Now);
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
            _selected = Math.Min(_live.Count - 1, _selected + 1);
        }

        for (var i = 0; i < _live.Count; i++)
        {
            if (input.Tapped(_tiles[i]))
            {
                Open(i);
                return;
            }
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Open(_selected);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        if (_live.Count == 0)
        {
            _live = _context.PaddockData.Events.Live(_context.Paddock.Now);
        }

        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Events"), new Vector2(viewport.X + pad, viewport.Y + pad), Palette.Accent);
        var bar = new Rectangle(viewport.Right - fonts.Px(200), viewport.Y + pad, fonts.Px(180), ButtonBar.Height(fonts.Small));
        _buttons.Draw(batch, fonts.Small, bar);

        var top = viewport.Y + pad + (int)fonts.Heading.LineHeight + pad;
        var area = new Rectangle(viewport.X + pad, top, viewport.Width - (2 * pad), viewport.Bottom - top - pad);
        var portrait = viewport.Height > viewport.Width;
        var gap = fonts.Px(16);
        for (var i = 0; i < _live.Count; i++)
        {
            _tiles[i] = portrait
                ? new Rectangle(area.X, area.Y + (i * (area.Height + gap) / _live.Count), area.Width, (area.Height / _live.Count) - gap)
                : new Rectangle(area.X + (i * (area.Width + gap) / _live.Count), area.Y, (area.Width / _live.Count) - gap, area.Height);
            DrawTile(batch, _live[i], _tiles[i], i == _selected, portrait);
        }
    }

    private void DrawTile(SpriteBatch batch, LiveEvent ev, Rectangle tile, bool selected, bool portrait)
    {
        var fonts = _context.Fonts;
        var small = fonts.Small;
        var store = _context.Paddock;
        var progress = EventPlay.Progress(store.Profile, ev, store.Now);
        batch.FillRectangle(tile, Palette.Panel);
        batch.FillRectangle(new Rectangle(tile.X, tile.Y, tile.Width, fonts.Px(5)), selected ? Palette.Accent : Palette.PanelLight);
        var pad = fonts.Px(12);

        // The prize car's card, then the words.
        var cardHeight = portrait ? tile.Height - (2 * pad) : (int)(tile.Height * 0.45f);
        var card = CarCard.Size((int)(cardHeight * CarCard.Aspect));
        var cardBounds = portrait
            ? new Rectangle(tile.X + pad, tile.Y + pad, card.X, card.Y)
            : new Rectangle(tile.Center.X - (card.X / 2), tile.Y + pad + fonts.Px(6), card.X, card.Y);
        batch.Restart(sampler: SamplerState.PointClamp);
        CarCard.Draw(batch, fonts, _context.PaddockData, CarInstance.New(ev.Def.PrizeCarId) with { Id = Guid.Empty }, cardBounds);
        batch.Restart();

        var x = portrait ? cardBounds.Right + pad : tile.X + pad;
        var y = portrait ? tile.Y + pad : cardBounds.Bottom + pad;
        void Line(string text, Color color, SpriteFontBase? font = null)
        {
            font ??= small;
            batch.DrawText(font, text, new Vector2(x, y), color, shadow: false);
            y += (int)font.LineHeight;
        }

        Line(T(ev.Def.Name), Palette.Accent, fonts.Body);
        Line(T("Prize: {0}", _context.PaddockData.Roster.Find(ev.Def.PrizeCarId)!.Name), Palette.Text);
        Line(progress.Rung >= EventPlay.Rungs ? T("Finished") : T("Match {0} of {1}", progress.Rung + 1, EventPlay.Rungs), Palette.Text);
        Line(Tickets(progress, store.Now), Palette.Text);
        Line(T("Ends in {0}", Format.Duration(ev.Ends - store.Now)), Palette.Muted);
        foreach (var rule in ev.Def.Rules.Describe(T, Words.Name))
        {
            Line(rule, Palette.Muted);
        }
    }

    /// <summary>"3 of 5 tickets · next in 12:04".</summary>
    public static string Tickets(EventProgress progress, DateTimeOffset now)
    {
        var count = progress.Tickets.Count;
        var next = EventPlay.Tickets.NextIn(progress.Tickets, now) is { } wait
            ? T(" · next in {0}", Format.Duration(wait))
            : string.Empty;
        return T("{0} of {1} tickets", count, EventPlay.Tickets.Max) + next;
    }

    private void Open(int index)
    {
        if (index < _live.Count)
        {
            Scenes.Push(new EventScene(_context, _live[index]), Navigation.Fade());
        }
    }
}
