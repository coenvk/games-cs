using System.Globalization;
using Jentl.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Multiplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker;
using Games.Input;
using Games.Poker;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Who sits at the poker table: a name for the menu and the players by seat.</summary>
internal sealed record PokerSetup(string Name, IReadOnlyList<Player> Players)
{
    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila", "Hugo", "Iris"];

    public static PokerSetup VersusBots(int bots) => new(
        bots == 1 ? T("Heads-up vs 1 bot") : T("You vs {0} bots", bots),
        [new Player(0, T("You"), PlayerKind.Human), .. Enumerable.Range(1, bots).Select(i => new Player(i, BotNames[i - 1], PlayerKind.Bot))]);

    public static PokerSetup PassAndPlay(int people) => new(
        T("Pass and play: {0} players", people),
        [.. Enumerable.Range(0, people).Select(i => new Player(i, T("Player {0}", i + 1), PlayerKind.Human))]);
}

/// <summary>
/// No-Limit Texas Hold'em, a sit-and-go: the players around an oval table (you at the bottom), the board and
/// the pot in the middle, and Fold, Check or Call, Raise and All in below. Raise amounts step by the big
/// blind with - and +. Everyone starts with 1,000 chips; the blinds double every ten hands.
/// </summary>
internal sealed class PokerScene : Scene
{
    private const int HudHeight = 64;
    private const int LogLines = 2;
    private const int StartingStack = 1000;
    private static readonly TimeSpan BotDelay = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan FastBotDelay = TimeSpan.FromMilliseconds(250);

    private readonly GameContext _context;
    private readonly PokerSetup _setup;
    private readonly int _seed;
    private readonly PokerBot[] _bots;
    private readonly ButtonBar _buttons = new();
    private readonly HoldemControls _controls = new();
    private readonly List<string> _log = [];
    private readonly Dictionary<int, string> _lastAction = [];
    private readonly HashSet<int> _shown = [];
    private readonly PokerTableView _view;
    private readonly string[] _names;
    private readonly Func<int, bool> _faceUp;
    private Match<HoldemHand, PokerAction> _runner;
    private bool _endShown;

    public PokerScene(GameContext context, PokerSetup setup, int? seed = null)
    {
        _context = context;
        _setup = setup;
        _seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);

        // Each bot plays its own way: from tight and careful to loose and pushy.
        _bots = [.. Enumerable.Range(0, setup.Players.Count).Select(i => new PokerBot(
            tightness: 0.25 + 0.15 * (i % 4),
            aggression: 0.7 - 0.12 * (i % 4),
            bluff: 0.04 + 0.03 * (i % 3)))];
        _view = new PokerTableView(context);
        _names = [.. setup.Players.Select(p => p.Name)];
        Table = new HoldemTable(setup.Players.Count, DealSeed.FromNumber(_seed), StartingStack);
        _runner = DealHand();
        _faceUp = seat => seat == _runner.Viewer || _shown.Contains(seat);
    }

    public HoldemTable Table { get; }

    public PokerSetup Setup => _setup;

    public IReadOnlyList<Player> Players => _setup.Players;

    private HoldemHand Hand => Table.Hand!;

    public void NewGame() => Scenes.Replace(new PokerScene(_context, _setup), Navigation.Fade());

    public void ConfirmHandover() => _runner.ConfirmHandover();

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Table);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _view.Update(elapsed, []);
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new PokerPauseScene(_context, this));
            return;
        }

        if (Hand.IsOver)
        {
            UpdateBetweenHands(input);
            return;
        }

        if (_runner.AwaitingHandover)
        {
            Scenes.Push(new PokerHandoverScene(_context, this, _runner.Current.Name));
            return;
        }

        // The bots wait while the chips fly.
        if (_view.IsAnimating && !_runner.IsHumansTurn)
        {
            return;
        }

        // Once no human is left in the hand, the bots play it out quickly.
        _runner.BotDelay = Enumerable.Range(0, Players.Count).Any(s => Players[s].IsHuman && Hand.IsLive(s)) ? BotDelay : FastBotDelay;
        if (_runner.Update(elapsed) is not null)
        {
            Show(Hand.TakeEvents());
            return;
        }

        if (!_runner.IsHumansTurn)
        {
            return;
        }

        var seat = _runner.Current.Seat;
        _buttons.Set(_controls.Buttons(Hand, seat, Act, new Button(T("Menu"), Pause)));
        if (_buttons.Update(input))
        {
            return;
        }

        _controls.Update(actions, input, Hand, seat, Act);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var hand = Hand;

        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, T("Hold'em: hand {0}", Table.HandNumber), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Small, T("blinds {0}/{1}, rising every {2} hands", Table.SmallBlind, Table.BigBlind, Table.HandsPerLevel), text, Palette.Muted, Align.Right, shadow: false);

        // Footer: messages, then the buttons.
        if (!_runner.IsHumansTurn)
        {
            _buttons.Set(hand.IsOver && !_endShown
                ? [new Button(Table.Champion is null ? T("Next hand") : T("Results"), NextHand, Highlight: true), new Button(T("Menu"), Pause)]
                : [new Button(T("Menu"), Pause)]);
        }

        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var lineHeight = (int)fonts.Small.LineHeight;
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var logTop = buttons.Top - LogLines * lineHeight - fonts.Px(6);
        var lines = _log.TakeLast(LogLines).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            batch.DrawText(fonts.Small, lines[i], new Vector2(viewport.Center.X, logTop + i * lineHeight), i == lines.Length - 1 ? Palette.Text : Palette.Muted, Align.Center, shadow: false);
        }

        // The table between the header and the messages, with the viewer at the bottom.
        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - 2 * margin, logTop - hud.Bottom - 2 * margin);
        _view.BottomSeat = _runner.Viewer ?? (_runner.Current.IsHuman ? _runner.Current.Seat : Players.First(p => p.IsHuman).Seat);
        _view.Draw(batch, area, hand, _names, _faceUp, _lastAction);

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

    private Match<HoldemHand, PokerAction> DealHand()
    {
        var hand = Table.DealHand();
        _lastAction.Clear();
        _shown.Clear();
        var runner = new Match<HoldemHand, PokerAction>(hand, Players, s => _bots[s], unchecked(_seed + Table.HandNumber)) { BotDelay = BotDelay };
        Say(T("Hand {0}, dealer: {1}", Table.HandNumber, Players[hand.Button].Name));
        Show(hand.TakeEvents());
        _controls.Reset(hand);
        return runner;
    }

    private void Act(PokerAction action)
    {
        if (!_runner.IsHumansTurn)
        {
            return;
        }

        var seat = _runner.Current.Seat;
        _runner.Play(action);
        Show(Hand.TakeEvents());
        if (Hand.IsOver || Hand.CurrentSeat != seat)
        {
            _runner.HideHands();
        }
    }

    private void Pause() => Scenes.Push(new PokerPauseScene(_context, this));

    private void UpdateBetweenHands(InputState input)
    {
        _buttons.Update(input);
        if (_context.Actions.WasPressed(input, GameAction.Confirm))
        {
            NextHand();
        }
    }

    private void NextHand()
    {
        if (!Hand.IsOver || _endShown)
        {
            return;
        }

        Table.Settle();
        var humansLeft = Players.Where(p => p.IsHuman && Table.Stacks[p.Seat] > 0).ToArray();
        if (Table.Champion is not null || humansLeft.Length == 0)
        {
            _endShown = true;
            _context.Sounds.Play(Table.Champion is { } champion && Players[champion].IsHuman ? Sound.Fanfare : Sound.Lose);
            if (Table.Champion is { } winner && Players[winner].IsHuman)
            {
                Celebration.Confetti(Scenes);
            }

            Scenes.Push(new PokerEndScene(_context, this));
            return;
        }

        _runner = DealHand();
    }

    private void Show(IReadOnlyList<PokerEvent> events)
    {
        foreach (var e in events)
        {
            Describe(e);
        }

        _view.Update(TimeSpan.Zero, events);
    }

    private void Describe(PokerEvent e)
    {
        string Name(int seat) => Players[seat].Name;
        switch (e)
        {
            case BlindPosted b:
                _lastAction[b.Seat] = b.Big ? T("Big blind") : T("Small blind");
                break;
            case Acted a:
                if (a.Amount > 0)
                {
                    _context.Sounds.Play(Sound.Chips);
                }

                var label = HoldemText.Action(a);
                _lastAction[a.Seat] = label;
                Say($"{Name(a.Seat)}: {label}");
                break;
            case StreetDealt d:
                HoldemText.NewStreet(_lastAction);
                Say(T("{0}: {1}", HoldemText.StreetName(d.Street), string.Join(" ", d.Cards)));
                break;
            case HandShown s:
                _shown.Add(s.Seat);
                _lastAction[s.Seat] = HoldemText.Describe(s.Value);
                break;
            case PotWon w:
                _context.Sounds.Play(Sound.Chips);
                Say(w.Value is { } value
                    ? T("{0} wins {1}: {2}", Name(w.Seat), w.Amount, HoldemText.Describe(value))
                    : T("{0} takes {1}", Name(w.Seat), w.Amount));
                break;
        }
    }

    private void Say(string message)
    {
        _log.Add(message);
        if (_log.Count > 20)
        {
            _log.RemoveAt(0);
        }
    }
}
