using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Multiplayer;
using Games.Input;
using Games.Poker;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Poker;
using Poker.Arena;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>
/// A recorded hand played again on the chip table with every seat's cards face up, one action at a time: the hand is
/// dealt again from its seed and its actions applied (see <see cref="HandReplay"/>). Previous and Next action step
/// through it (going back deals it again up to the action before), Previous and Next hand move through the match.
/// What has happened so far is logged on the right, with the bots' faults in this hand at the top.
/// </summary>
internal sealed class ReplayScene : Scene
{
    private const int HudHeight = 64;
    private static readonly Color Problem = new(236, 120, 108);

    private readonly GameContext _context;
    private readonly MatchResult _match;
    private readonly PokerTableView _view;
    private readonly ButtonBar _buttons = new();
    private readonly Dictionary<int, string> _labels = [];

    // The log of the whole hand, each line with the number of actions after which it happened (0: the deal).
    private readonly List<(string Text, int Step, bool Fault)> _log = [];
    private int _index;
    private int _step;
    private HoldemHand? _hand;
    private string? _problem;

    /// <param name="hand">The hand to start at, an index into the match's hands.</param>
    public ReplayScene(WorkshopContext workshop, MatchResult match, int hand)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(match);
        if (match.Hands.Count == 0)
        {
            throw new ArgumentException("A match without hands has nothing to replay.", nameof(match));
        }

        _context = workshop.Game;
        _match = match;
        _view = new PokerTableView(_context);
        _buttons.Set(
            new Button(T("Previous hand"), () => ShowHand(_index - 1)),
            new Button(T("Previous action"), PreviousAction),
            new Button(T("Next action"), NextAction, Highlight: true),
            new Button(T("Next hand"), () => ShowHand(_index + 1)),
            new Button(T("Back"), () => Scenes.Pop()));
        ShowHand(hand);
    }

    private HandRecord Record => _match.Hands[_index];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _view.Update(elapsed, []);
        if (_buttons.Update(input))
        {
            return;
        }

        // Left and right step through the actions, up and down through the hands.
        if (Controls.Ui.WasPressed(input, UiAction.Right) || Controls.Ui.WasPressed(input, UiAction.Confirm))
        {
            NextAction();
        }
        else if (Controls.Ui.WasPressed(input, UiAction.Left))
        {
            PreviousAction();
        }
        else if (Controls.Ui.WasPressed(input, UiAction.Down) || input.WasPressed(Keys.PageDown))
        {
            ShowHand(_index + 1);
        }
        else if (Controls.Ui.WasPressed(input, UiAction.Up) || input.WasPressed(Keys.PageUp))
        {
            ShowHand(_index - 1);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var margin = fonts.Px(16);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        var record = Record;
        var title = T("Replay: hand {0} of {1}, blinds {2}/{3}", _index + 1, _match.Hands.Count, record.SmallBlind, record.BigBlind);
        batch.DrawText(fonts.Body, title, text, Palette.Text, Align.Left);
        var where = T("Action {0} of {1}", _step, record.Actions.Count);
        batch.DrawText(fonts.Small, $"{where}  ·  {T("Every bot's cards are face up")}", text, Palette.Muted, Align.Right, shadow: false);

        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var logWidth = Math.Min(fonts.Px(340), viewport.Width * 3 / 10);
        var log = new Rectangle(viewport.Right - margin - logWidth, hud.Bottom + margin, logWidth, buttons.Top - hud.Bottom - margin);
        DrawLog(batch, log);

        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, log.Left - viewport.X - 4 * margin, buttons.Top - hud.Bottom - 2 * margin);
        if (_hand is { } hand)
        {
            _view.Draw(batch, area, hand, record.Names, _ => true, _labels);
        }
        else
        {
            batch.DrawText(fonts.Body, _problem ?? "", area, Problem, Align.Center);
        }

        _buttons.Draw(batch, buttonFont, buttons);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string FaultLine(string name, Fault fault) => fault switch
    {
        Fault.Late => T("{0} was late", name),
        Fault.Illegal => T("{0} made an illegal move", name),
        _ => T("{0} was gone", name),
    };

    /// <summary>Deals hand <paramref name="index"/> (kept within the match) and logs it from start to end.</summary>
    private void ShowHand(int index)
    {
        _index = Math.Clamp(index, 0, _match.Hands.Count - 1);
        var record = Record;
        _log.Clear();
        _log.AddRange(record.Faults.Select(f => (FaultLine(record.Names[f.Seat], f.Fault), 0, true)));
        _log.Add((T("Hand {0}, dealer: {1}", record.Number, record.Names[record.Button]), 0, false));
        try
        {
            var whole = HandReplay.Rebuild(record, 0);
            Log(whole.TakeEvents(), 0);
            for (var i = 0; i < record.Actions.Count; i++)
            {
                whole.Apply(record.Actions[i].Seat, record.Actions[i].Action);
                Log(whole.TakeEvents(), i + 1);
            }

            Rebuild(0);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Fail(e.Message);
        }
    }

    private void NextAction()
    {
        if (_hand is not { IsOver: false } hand || _step >= Record.Actions.Count)
        {
            return;
        }

        var action = Record.Actions[_step];
        hand.Apply(action.Seat, action.Action);
        _step++;
        var events = hand.TakeEvents();
        Label(events);
        _view.Update(TimeSpan.Zero, events);
    }

    private void PreviousAction()
    {
        if (_hand is not null && _step > 0)
        {
            Rebuild(_step - 1);
        }
    }

    // The hand dealt again and played up to action number step: a new hand, so the view starts again from it.
    private void Rebuild(int step)
    {
        try
        {
            _hand = HandReplay.Rebuild(Record, step);
            _step = step;
            _problem = null;
            _labels.Clear();
            Label(_hand.TakeEvents());
        }
        catch (InvalidDataException e)
        {
            Fail(e.Message);
        }
    }

    private void Fail(string message)
    {
        _hand = null;
        _step = 0;
        _problem = T("This hand cannot be replayed: {0}", message);
    }

    // What each seat last did, as the table shows it under the seat.
    private void Label(IReadOnlyList<PokerEvent> events)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case BlindPosted b:
                    _labels[b.Seat] = b.Big ? T("Big blind") : T("Small blind");
                    break;
                case Acted a:
                    _labels[a.Seat] = HoldemText.Action(a);
                    break;
                case StreetDealt:
                    HoldemText.NewStreet(_labels);
                    break;
                case HandShown s:
                    _labels[s.Seat] = HoldemText.Describe(s.Value);
                    break;
            }
        }
    }

    private void Log(IReadOnlyList<PokerEvent> events, int step)
    {
        var names = Record.Names;
        foreach (var e in events)
        {
            var line = e switch
            {
                Acted a => $"{names[a.Seat]}: {HoldemText.Action(a)}",
                StreetDealt d => T("{0}: {1}", HoldemText.StreetName(d.Street), string.Join(" ", d.Cards)),
                PotWon { Value: { } value } w => T("{0} wins {1}: {2}", names[w.Seat], w.Amount, HoldemText.Describe(value)),
                PotWon w => T("{0} takes {1}", names[w.Seat], w.Amount),
                _ => null,
            };
            if (line is not null)
            {
                _log.Add((line, step, false));
            }
        }
    }

    // The lines up to the current action, the newest at the bottom, wrapped to the width; older ones go off the top.
    private void DrawLog(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var font = fonts.Small;
        var padding = fonts.Px(10);
        batch.FillRectangle(area, Palette.Panel);
        var shown = _log.Where(l => l.Step <= _step).ToList();
        var line = (int)font.LineHeight + 2;
        var y = area.Bottom - padding;
        for (var i = shown.Count - 1; i >= 0 && y - line >= area.Y + padding; i--)
        {
            var (message, step, fault) = shown[i];
            var color = fault ? Problem : step == _step && _step > 0 ? Palette.Text : Palette.Muted;
            var parts = font.Wrap(message, area.Width - 2 * padding);
            for (var p = parts.Count - 1; p >= 0 && y - line >= area.Y + padding; p--)
            {
                y -= line;
                batch.DrawText(font, parts[p], new Vector2(area.X + padding, y), color, shadow: false);
            }
        }
    }
}
