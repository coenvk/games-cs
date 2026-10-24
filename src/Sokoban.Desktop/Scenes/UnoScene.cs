using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Multiplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Desktop.UI;
using Uno;
using Jentl.Core.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Who plays: a name for the menu and the players by seat.</summary>
internal sealed record UnoSetup(string Name, IReadOnlyList<Player> Players)
{
    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila"];

    /// <summary>You in seat 0 and <paramref name="bots"/> computer opponents.</summary>
    public static UnoSetup VersusBots(int bots) => new(
        bots == 1 ? "You vs 1 bot" : $"You vs {bots} bots",
        [new Player(0, "You", PlayerKind.Human), .. Enumerable.Range(1, bots).Select(i => new Player(i, BotNames[i - 1], PlayerKind.Bot))]);

    /// <summary>People sharing the device.</summary>
    public static UnoSetup PassAndPlay(int people) => new(
        $"Pass and play: {people} players",
        [.. Enumerable.Range(0, people).Select(i => new Player(i, $"Player {i + 1}", PlayerKind.Human))]);
}

/// <summary>
/// Uno at one table: the other players along the top (with their card counts and scores), the draw pile and
/// the discard in the middle, and the viewer's hand at the bottom. Tap a card to play it, Draw to draw, and
/// Uno! before playing your second-to-last card. Bots take their turns by themselves; with several people
/// at the device, hands are hidden while it is passed on.
/// </summary>
internal sealed class UnoScene : Scene
{
    private const int HudHeight = 64;
    private const int LogLines = 2;
    private static readonly TimeSpan FlightTime = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan RoundEndDelay = TimeSpan.FromMilliseconds(1100);

    private readonly GameContext _context;
    private readonly UnoSetup _setup;
    private readonly int _seed;
    private readonly IBot<UnoGame, UnoMove> _bot = new SmartUnoBot();
    private readonly ButtonBar _buttons;
    private readonly List<string> _log = [];
    private readonly List<Flight> _flights = [];
    private readonly ActionRepeater<GameAction> _repeater = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(110), GameAction.Left, GameAction.Right);
    private readonly Dictionary<int, Rectangle> _seats = [];
    private List<(UnoCard Card, Rectangle Bounds)> _hand = [];
    private UnoArt? _art;
    private Rectangle _drawPile;
    private Rectangle _discard;
    private Match<UnoGame, UnoMove> _runner;
    private bool _callUno;
    private int _selected;
    private TimeSpan _roundOverFor;
    private bool _roundShown;

    public UnoScene(GameContext context, UnoSetup setup, int? seed = null)
    {
        _context = context;
        _setup = setup;
        _seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);
        Match = new UnoMatch(setup.Players.Count, _seed);
        _runner = NewRunner();
        _buttons = new ButtonBar(
            new Button("Draw", DrawOrKeep),
            new Button("Uno!", () => _callUno = !_callUno),
            new Button("Menu", () => Scenes.Push(new UnoPauseScene(context, this))));
        Say(Match.Round.Events);
        Match.Round.TakeEvents();
    }

    public UnoMatch Match { get; }

    public UnoSetup Setup => _setup;

    public IReadOnlyList<Player> Players => _setup.Players;

    public void NewMatch() => Scenes.Replace(new UnoScene(_context, _setup), Navigation.Fade());

    /// <summary>Deals the next round after the round-over panel.</summary>
    public void NextRound()
    {
        Match.NextRound();
        _runner = NewRunner();
        _roundShown = false;
        _roundOverFor = TimeSpan.Zero;
        _flights.Clear();
        _log.Clear();
        Say(Match.Round.TakeEvents());
    }

    /// <summary>The handover panel's "it's me" button.</summary>
    public void ConfirmHandover() => _runner.ConfirmHandover();

    /// <summary>Plays the chosen colour for a wild picked from the hand.</summary>
    public void PlayWild(UnoCard card, UnoColor colour) => PlayHuman(new PlayCard(card, colour, _callUno));

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Table);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        foreach (var flight in _flights)
        {
            flight.Tween.Update(elapsed);
        }

        _flights.RemoveAll(f => f.Tween.IsDone);
        var game = Match.Round;
        if (game.IsOver)
        {
            _roundOverFor += elapsed;
            if (!_roundShown && _roundOverFor >= RoundEndDelay)
            {
                _roundShown = true;
                Match.ScoreRound();
                _context.Sounds.Play(Sound.Fanfare);
                Scenes.Push(new UnoRoundScene(_context, this));
            }

            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new UnoPauseScene(_context, this));
            return;
        }

        if (_runner.AwaitingHandover)
        {
            Scenes.Push(new UnoHandoverScene(_context, this, _runner.Current.Name));
            return;
        }

        if (_runner.Update(elapsed) is not null)
        {
            Show(game.TakeEvents());
            return;
        }

        if (!_runner.IsHumansTurn || _buttons.Update(input))
        {
            return;
        }

        var actions = _context.Actions;
        var hand = game.Hand(_runner.Current.Seat);
        switch (_repeater.Update(input, actions, elapsed))
        {
            case GameAction.Left:
                _selected = Math.Max(0, _selected - 1);
                return;
            case GameAction.Right:
                _selected = Math.Min(hand.Count - 1, _selected + 1);
                return;
        }

        if (actions.WasPressed(input, GameAction.Down))
        {
            DrawOrKeep();
        }
        else if (actions.WasPressed(input, GameAction.Up))
        {
            _callUno = !_callUno;
        }
        else if (actions.WasPressed(input, GameAction.Confirm) && _selected < hand.Count)
        {
            Choose(hand[_selected]);
        }
        else if (input.Tap is { } tap)
        {
            // Cards overlap, so the last one drawn under the tap is the one on top.
            for (var i = _hand.Count - 1; i >= 0; i--)
            {
                if (_hand[i].Bounds.Contains(tap))
                {
                    _selected = i;
                    Choose(_hand[i].Card);
                    return;
                }
            }

            if (_drawPile.Contains(tap))
            {
                DrawOrKeep();
            }
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        _art ??= new UnoArt(batch.GraphicsDevice, _context.Cards.Art, new FontTextDrawer(_context.Fonts));
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var game = Match.Round;
        var viewer = _runner.Viewer;

        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, string.Create(CultureInfo.InvariantCulture, $"Uno: round {Match.RoundNumber}"), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Small, string.Create(CultureInfo.InvariantCulture, $"first to {Match.Target}"), text, Palette.Muted, Align.Right, shadow: false);

        // The others along the top, in turn order starting after whoever sits at the bottom.
        var bottomSeat = viewer ?? (_runner.Current.IsHuman ? _runner.Current.Seat : Players.First(p => p.IsHuman).Seat);
        var others = Enumerable.Range(1, Players.Count - 1).Select(i => (bottomSeat + i) % Players.Count).ToArray();
        var badgeWidth = Math.Min(fonts.Px(220), (viewport.Width - 2 * margin) / others.Length - margin / 2);
        var badgeHeight = fonts.Px(96);
        var badgesLeft = viewport.Center.X - (others.Length * badgeWidth + (others.Length - 1) * margin / 2) / 2;
        _seats.Clear();
        for (var i = 0; i < others.Length; i++)
        {
            var badge = new Rectangle(badgesLeft + i * (badgeWidth + margin / 2), hud.Bottom + margin / 2, badgeWidth, badgeHeight);
            _seats[others[i]] = badge;
            DrawBadge(batch, badge, others[i]);
        }

        // Footer: messages above the buttons.
        _buttons.Set(
            new Button(game.Drawn is null ? "Draw" : "Keep", DrawOrKeep),
            new Button(_callUno ? "Uno! (on)" : "Uno!", () => _callUno = !_callUno, Highlight: _callUno),
            new Button("Menu", () => Scenes.Push(new UnoPauseScene(_context, this))));
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var lineHeight = (int)fonts.Small.LineHeight;
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var log = new Rectangle(viewport.X, buttons.Top - LogLines * lineHeight - fonts.Px(8), viewport.Width, LogLines * lineHeight);
        var lines = _log.TakeLast(LogLines).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            batch.DrawText(fonts.Small, lines[i], new Vector2(viewport.Center.X, log.Y + i * lineHeight), i == lines.Length - 1 ? Palette.Text : Palette.Muted, Align.Center, shadow: false);
        }

        // The hand: as wide as fits, overlapping when there are many cards.
        var space = log.Top - (hud.Bottom + badgeHeight + margin);
        var cardHeight = Math.Min(fonts.Px(150), space * 2 / 5);
        var cardWidth = (int)(cardHeight * UnoArt.AspectRatio);
        _seats[bottomSeat] = new Rectangle(viewport.Center.X - cardWidth / 2, log.Top - margin - cardHeight, cardWidth, cardHeight);
        DrawHand(batch, viewport, bottomSeat, log.Top - margin / 2 - cardHeight, cardWidth, cardHeight);

        // The table: draw pile, discard, and the colour and direction of play.
        var tableTop = hud.Bottom + badgeHeight + margin;
        var tableHeight = _seats[bottomSeat].Top - fonts.Px(26) - tableTop;
        var pileHeight = Math.Min(cardHeight, tableHeight - fonts.Px(30));
        var pileWidth = (int)(pileHeight * UnoArt.AspectRatio);
        var pileTop = tableTop + (tableHeight - pileHeight - fonts.Px(30)) / 2;
        _drawPile = new Rectangle(viewport.Center.X - pileWidth - margin, pileTop, pileWidth, pileHeight);
        _discard = new Rectangle(viewport.Center.X + margin, pileTop, pileWidth, pileHeight);
        if (game.DrawPileCount > 0)
        {
            _art.DrawBack(batch, _drawPile);
        }

        var swatch = new Rectangle(_discard.X - fonts.Px(6), _discard.Y - fonts.Px(6), _discard.Width + fonts.Px(12), _discard.Height + fonts.Px(12));
        batch.FillRectangle(swatch, UnoArt.ColorOf(game.Color) * 0.55f);
        _art.Draw(batch, game.Top, _discard);
        var next = Players[(game.CurrentSeat + game.Direction + Players.Count) % Players.Count].Name;
        var turn = game.IsOver ? "" : $"{Players[game.CurrentSeat].Name} to play, then {next}   ({game.Color}, {(game.Direction > 0 ? "clockwise" : "counter-clockwise")})";
        batch.DrawText(fonts.Small, turn, new Vector2(viewport.Center.X, _drawPile.Bottom + fonts.Px(10)), Palette.Muted, Align.Center, shadow: false);

        foreach (var flight in _flights)
        {
            var at = flight.Tween.Value;
            var bounds = new Rectangle((int)at.X - pileWidth / 2, (int)at.Y - pileHeight / 2, pileWidth, pileHeight);
            if (flight.Card is { } card)
            {
                _art.Draw(batch, card, bounds);
            }
            else
            {
                _art.DrawBack(batch, bounds);
            }
        }

        _buttons.Draw(batch, buttonFont, buttons);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _art?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Match<UnoGame, UnoMove> NewRunner() =>
        new(Match.Round, Players, _ => _bot, unchecked(_seed + Match.RoundNumber));

    private void DrawBadge(SpriteBatch batch, Rectangle badge, int seat)
    {
        var fonts = _context.Fonts;
        var game = Match.Round;
        var current = !game.IsOver && game.CurrentSeat == seat;
        batch.FillRectangle(badge, current ? Palette.Accent : Palette.Panel);
        var inner = badge;
        inner.Inflate(-3, -3);
        batch.FillRectangle(inner, Palette.Panel);
        var count = game.Hand(seat).Count;
        var name = string.Create(CultureInfo.InvariantCulture, $"{Players[seat].Name} ({Match.Scores[seat]}){(count == 1 ? "  UNO!" : "")}");
        batch.DrawText(fonts.Small, name, new Vector2(badge.Center.X, badge.Y + fonts.Px(6)), current ? Palette.Accent : Palette.Text, Align.Center, shadow: false);

        // A small fan of backs, one per card (up to a dozen), with the count beside it.
        var backHeight = badge.Height / 2;
        var backWidth = (int)(backHeight * UnoArt.AspectRatio);
        var shown = Math.Min(count, 12);
        var step = Math.Max(2, Math.Min(backWidth / 3, (badge.Width / 2 - backWidth) / Math.Max(1, shown - 1)));
        var left = badge.Center.X - (backWidth + step * (shown - 1)) / 2 - fonts.Px(16);
        for (var i = 0; i < shown; i++)
        {
            _art!.DrawBack(batch, new Rectangle(left + i * step, badge.Bottom - backHeight - fonts.Px(6), backWidth, backHeight));
        }

        var label = count.ToString(CultureInfo.InvariantCulture);
        batch.DrawText(fonts.Small, label, new Vector2(left + backWidth + step * Math.Max(0, shown - 1) + fonts.Px(8), badge.Bottom - backHeight / 2 - fonts.Px(14)), Palette.Muted, shadow: false);
    }

    private void DrawHand(SpriteBatch batch, Rectangle viewport, int seat, int top, int width, int height)
    {
        var fonts = _context.Fonts;
        var game = Match.Round;
        var cards = game.Hand(seat);
        var visible = _runner.Viewer == seat;
        var playing = visible && _runner.IsHumansTurn && game.CurrentSeat == seat;
        var avail = viewport.Width - 2 * fonts.Px(16);
        var step = cards.Count <= 1 ? 0 : Math.Min(width + fonts.Px(8), (avail - width) / (cards.Count - 1));
        var left = viewport.Center.X - (width + step * Math.Max(0, cards.Count - 1)) / 2;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, cards.Count - 1));
        _hand = [];
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var playable = playing && (game.Drawn is null ? game.CanPlay(seat, card) : game.Drawn == card);
            var raise = playable ? height / 7 : 0;
            var bounds = new Rectangle(left + i * step, top - raise, width, height);
            _hand.Add((card, bounds));
            if (!visible)
            {
                _art!.DrawBack(batch, bounds);
                continue;
            }

            if (playing && i == _selected)
            {
                var outline = bounds;
                outline.Inflate(4, 4);
                batch.FillRectangle(outline, Palette.Accent);
            }

            _art!.Draw(batch, card, bounds, dim: playing && !playable);
        }

        if (!visible && cards.Count > 0)
        {
            batch.DrawText(fonts.Body, $"{Players[seat].Name}'s hand", new Vector2(viewport.Center.X, top + height / 2 - fonts.Body.LineHeight / 2), Palette.Text, Align.Center);
        }
    }

    private void Choose(UnoCard card)
    {
        var game = Match.Round;
        var seat = _runner.Current.Seat;
        if (game.Drawn is { } drawn && drawn != card)
        {
            Say("Play the card you drew, or keep it");
            return;
        }

        if (!game.CanPlay(seat, card))
        {
            Say(card.Value == UnoValue.WildDrawFour
                ? "A Wild +4 only goes when you have nothing in the current colour"
                : $"That doesn't match the {game.Color} {game.Top.Label}");
            return;
        }

        if (card.IsWild)
        {
            Scenes.Push(new UnoColourScene(_context, this, card));
            return;
        }

        PlayHuman(new PlayCard(card, null, _callUno));
    }

    private void DrawOrKeep()
    {
        if (_runner.IsHumansTurn)
        {
            PlayHuman(Match.Round.Drawn is null ? DrawCard.Instance : Uno.Pass.Instance);
        }
    }

    private void PlayHuman(UnoMove move)
    {
        if (!_runner.IsHumansTurn)
        {
            return;
        }

        var seat = _runner.Current.Seat;
        _runner.Play(move);
        if (move is PlayCard)
        {
            _callUno = false;
        }

        Show(Match.Round.TakeEvents());

        // Passing the device on: nobody else may see this hand.
        if (Match.Round.CurrentSeat != seat)
        {
            _runner.HideHands();
        }
    }

    private void Show(IReadOnlyList<UnoEvent> events)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case CardPlayed played:
                    _context.Sounds.Play(Sound.Card);
                    Fly(played.Card, Centre(played.Seat), _discard.Center.ToVector2());
                    break;
                case CardsDrawn drawn:
                    for (var i = 0; i < Math.Min(drawn.Count, 4); i++)
                    {
                        Fly(null, _drawPile.Center.ToVector2(), Centre(drawn.Seat));
                    }

                    break;
            }
        }

        Say(events);
    }

    private void Say(IEnumerable<UnoEvent> events)
    {
        string Name(int seat) => Players[seat].Name;
        foreach (var e in events)
        {
            Say(e switch
            {
                CardPlayed { Card.IsWild: true } p => $"{Name(p.Seat)} plays {p.Card} and picks {p.Color}",
                CardPlayed p => $"{Name(p.Seat)} plays {p.Card}",
                CardsDrawn d => d.Count == 1 ? $"{Name(d.Seat)} draws a card" : $"{Name(d.Seat)} draws {d.Count}",
                PlayerSkipped s => $"{Name(s.Seat)} is skipped",
                DirectionReversed => "Play turns around",
                UnoCalled u => $"{Name(u.Seat)}: Uno!",
                UnoForgotten u => $"{Name(u.Seat)} forgot to call Uno: two cards",
                DrawPileReshuffled => "The discards are shuffled into a new draw pile",
                RoundWon w => $"{Name(w.Seat)} wins the round!",
                _ => e.ToString(),
            });
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

    private void Fly(UnoCard? card, Vector2 from, Vector2 to) => _flights.Add(new Flight(card, new Tween(from, to, FlightTime, Easing.CubicOut)));

    private Vector2 Centre(int seat) => _seats.TryGetValue(seat, out var at) ? at.Center.ToVector2() : _discard.Center.ToVector2();

    private sealed record Flight(UnoCard? Card, Tween Tween);
}
