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

/// <summary>
/// Before a match: the five rounds' tracks and conditions with the opponent's car for each, and a slot under each for
/// the player's car. Pick a slot, then a car from the garage below; the RQ total must stay within the limit. The
/// order decides which car races which track. Starts from the last hand raced.
/// </summary>
internal sealed class MatchSetupScene : Scene
{
    private const int RaceSlot = Hands.Size;

    private readonly GameContext _context;
    private readonly string _title;
    private readonly MatchSetup _setup;
    private readonly int _rqLimit;
    private readonly Func<CarModel, bool> _allowed;
    private readonly Action<IReadOnlyList<Guid>> _start;
    private readonly CarInstance?[] _hand = new CarInstance?[Hands.Size];
    private readonly Rectangle[] _slots = new Rectangle[Hands.Size + 1];
    private readonly CardGrid _garage;
    private readonly ButtonBar _buttons;
    private int _slot;
    private bool _picking;

    /// <param name="title">What is being raced (the event's or stage's name).</param>
    /// <param name="allowed">Which cars may enter.</param>
    /// <param name="start">Called with the player's cars in round order when they press Race.</param>
    public MatchSetupScene(GameContext context, string title, MatchSetup setup, int rqLimit, Func<CarModel, bool> allowed, Action<IReadOnlyList<Guid>> start)
    {
        _context = context;
        _title = title;
        _setup = setup;
        _rqLimit = rqLimit;
        _allowed = allowed;
        _start = start;
        var profile = context.Paddock.Profile;
        var roster = context.PaddockData.Roster;
        var eligible = profile.Garage.Where(c => roster.Find(c.ModelId) is { } m && allowed(m)).OrderByDescending(c => roster.Rq(c.ModelId)).ToList();
        _garage = new CardGrid(context, eligible)
        {
            Badge = car => Array.FindIndex(_hand, h => h?.Id == car.Id) is var i and >= 0 ? T("Round {0}", i + 1) : null,
        };

        // Start from the last hand, where its cars may still enter.
        for (var i = 0; i < Math.Min(Hands.Size, profile.LastHand.Count); i++)
        {
            _hand[i] = eligible.Find(c => c.Id == profile.LastHand[i]);
        }

        _slot = Array.FindIndex(_hand, h => h is null) is var empty and >= 0 ? empty : RaceSlot;
        _buttons = new ButtonBar(new Button(T("Back"), Back));
    }

    private Roster Roster => _context.PaddockData.Roster;

    private int Total => _hand.Where(c => c is not null).Sum(c => Roster.Rq(c!.ModelId));

    private bool Ready => _hand.All(c => c is not null) && Hands.Fits([.. _hand!], Roster, _rqLimit, _allowed);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Back();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        // Tapping a slot (or Race) works in either mode.
        if (input.Tap is { } tap)
        {
            for (var i = 0; i <= RaceSlot; i++)
            {
                if (_slots[i].Contains(tap))
                {
                    Choose(i);
                    return;
                }
            }
        }

        if (_picking || input.Tap is not null)
        {
            if (_garage.Update(input) is { } car)
            {
                Place(car);
            }

            return;
        }

        if (actions.WasPressed(input, GameAction.Left))
        {
            Move(-1);
        }
        else if (actions.WasPressed(input, GameAction.Right))
        {
            Move(1);
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            Choose(_slot);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(16);
        batch.DrawText(fonts.Body, _title, new Vector2(viewport.X + pad, viewport.Y + pad), Palette.Accent);
        var rq = T("RQ {0} of {1}", Total, _rqLimit);
        batch.DrawText(fonts.Body, rq, new Vector2(viewport.Right - pad, viewport.Y + pad), Total > _rqLimit ? new Color(240, 96, 80) : Palette.Text, Align.Right);
        var top = viewport.Y + pad + (int)fonts.Body.LineHeight;
        var bar = new Rectangle(viewport.X, top, viewport.Width / 3, ButtonBar.Height(fonts.Small) + fonts.Px(6));
        _buttons.Draw(batch, fonts.Small, bar);
        top = bar.Bottom;

        var rounds = new Rectangle(viewport.X + pad, top, viewport.Width - (2 * pad), (int)((viewport.Bottom - top) * 0.58f));
        var portrait = viewport.Height > viewport.Width;
        batch.Restart(sampler: SamplerState.PointClamp);
        if (portrait)
        {
            DrawRows(batch, rounds);
        }
        else
        {
            DrawColumns(batch, rounds);
        }

        batch.Restart();
        var raceArea = portrait
            ? new Rectangle(rounds.X, rounds.Bottom + fonts.Px(4), rounds.Width, (int)fonts.Body.LineHeight + fonts.Px(8))
            : new Rectangle(rounds.Right - fonts.Px(160), viewport.Y + pad, fonts.Px(160), (int)fonts.Body.LineHeight);
        _slots[RaceSlot] = portrait ? raceArea : new Rectangle(viewport.Center.X - fonts.Px(80), viewport.Y + pad, fonts.Px(160), (int)fonts.Body.LineHeight + fonts.Px(6));
        var raceColor = !Ready ? Palette.PanelLight : _slot == RaceSlot && !_picking ? Palette.Accent : new Color(86, 196, 110);
        batch.FillRectangle(_slots[RaceSlot], raceColor);
        batch.DrawText(fonts.Body, T("Race"), _slots[RaceSlot], Ready ? Palette.Ink : Palette.Muted, shadow: false);

        var gridTop = portrait ? raceArea.Bottom : rounds.Bottom;
        var hint = _picking ? T("Pick a car for round {0}", _slot + 1) : T("Pick a round, then a car for it");
        batch.DrawText(fonts.Small, hint, new Vector2(viewport.Center.X, gridTop + fonts.Px(2)), Palette.Muted, Align.Center, shadow: false);
        var grid = new Rectangle(viewport.X + pad, gridTop + (int)fonts.Small.LineHeight, viewport.Width - (2 * pad), viewport.Bottom - gridTop - (int)fonts.Small.LineHeight);
        _garage.Draw(batch, grid, Math.Min(fonts.Px(170), grid.Height * 5 / 7));
    }

    // Landscape: five columns, each the track on top, the opponent's card, then the player's slot.
    private void DrawColumns(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var gap = fonts.Px(12);
        var columnWidth = (area.Width - (4 * gap)) / Hands.Size;
        var labelHeight = (int)(fonts.Small.LineHeight * 2);
        var cardHeight = Math.Min((area.Height - labelHeight - gap) / 2, (int)(columnWidth / CarCard.Aspect));
        var card = CarCard.Size((int)(cardHeight * CarCard.Aspect));
        for (var i = 0; i < Hands.Size; i++)
        {
            var x = area.X + (i * (columnWidth + gap));
            var centre = x + (columnWidth / 2);
            DrawRoundLabel(batch, i, new Vector2(centre, area.Y), Align.Center);
            var opponent = new Rectangle(centre - (card.X / 2), area.Y + labelHeight, card.X, card.Y);
            CarCard.Draw(batch, fonts, _context.PaddockData, _setup.Opponent[i], opponent);
            _slots[i] = new Rectangle(centre - (card.X / 2), opponent.Bottom + gap, card.X, card.Y);
            DrawSlot(batch, i);
        }
    }

    // Portrait: five rows, each the opponent's card, the track between, and the player's slot.
    private void DrawRows(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var gap = fonts.Px(6);
        var rowHeight = (area.Height - (4 * gap)) / Hands.Size;
        var card = CarCard.Size((int)(rowHeight * CarCard.Aspect));
        for (var i = 0; i < Hands.Size; i++)
        {
            var y = area.Y + (i * (rowHeight + gap));
            var opponent = new Rectangle(area.X, y, card.X, card.Y);
            CarCard.Draw(batch, fonts, _context.PaddockData, _setup.Opponent[i], opponent);
            DrawRoundLabel(batch, i, new Vector2(area.Center.X, y + (rowHeight / 2) - fonts.Small.LineHeight), Align.Center);
            _slots[i] = new Rectangle(area.Right - card.X, y, card.X, card.Y);
            DrawSlot(batch, i);
        }
    }

    private void DrawRoundLabel(SpriteBatch batch, int round, Vector2 at, Align align)
    {
        var font = _context.Fonts.Small;
        var (track, condition) = _setup.Rounds[round];
        batch.DrawText(font, T(track.Name), at, Palette.Text, align, shadow: false);
        batch.DrawText(font, $"{Words.Of(track.Kind)} · {Words.Of(condition)}", at + new Vector2(0, font.LineHeight), Palette.Muted, align, shadow: false);
    }

    private void DrawSlot(SpriteBatch batch, int i)
    {
        var bounds = _slots[i];
        var focused = i == _slot;
        if (_hand[i] is { } car)
        {
            CarCard.Draw(batch, _context.Fonts, _context.PaddockData, car, bounds, highlight: focused);
            return;
        }

        batch.FillRectangle(bounds, Palette.Panel);
        batch.StrokeRectangle(bounds, focused ? Palette.Accent : Palette.PanelLight, Math.Max(2, _context.Fonts.Px(3)));
        batch.DrawText(_context.Fonts.Small, T("Your car"), bounds, Palette.Muted, shadow: false);
    }

    private void Move(int step)
    {
        _slot = Math.Clamp(_slot + step, 0, RaceSlot);
        _context.Sounds.Play(Sound.Navigate);
    }

    private void Choose(int slot)
    {
        _slot = slot;
        if (slot == RaceSlot)
        {
            if (Ready)
            {
                _context.Paddock.Profile.LastHand = [.. _hand.Select(c => c!.Id)];
                _context.Paddock.Save();
                _start([.. _hand.Select(c => c!.Id)]);
            }
            else
            {
                _context.Sounds.Play(Sound.Error);
                var why = _hand.Any(c => c is null) ? T("Pick a car for every round.") : T("Your cars' RQ adds up to more than {0}.", _rqLimit);
                Scenes.Push(new MessageScene(_context, T("Not ready"), why));
            }

            return;
        }

        _picking = true;
        _context.Sounds.Play(Sound.Click);
    }

    // Puts the car in the chosen slot; if it was in another slot, the two swap.
    private void Place(CarInstance car)
    {
        var from = Array.FindIndex(_hand, h => h?.Id == car.Id);
        if (from >= 0)
        {
            _hand[from] = _hand[_slot];
        }

        _hand[_slot] = car;
        _context.Sounds.Play(Sound.Card);
        _picking = false;
        _slot = Array.FindIndex(_hand, h => h is null) is var empty and >= 0 ? empty : RaceSlot;
    }

    private void Back()
    {
        if (_picking)
        {
            _picking = false;
            return;
        }

        Scenes.Pop(Navigation.Fade());
    }
}
