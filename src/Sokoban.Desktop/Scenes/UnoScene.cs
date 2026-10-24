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
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>Who plays: a name for the menu and the players by seat.</summary>
internal sealed record UnoSetup(string Name, IReadOnlyList<Player> Players)
{
    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila"];

    /// <summary>You in seat 0 and <paramref name="bots"/> computer opponents.</summary>
    public static UnoSetup VersusBots(int bots) => new(
        N(bots, "You vs {0} bot", "You vs {0} bots"),
        [new Player(0, T("You"), PlayerKind.Human), .. Enumerable.Range(1, bots).Select(i => new Player(i, BotNames[i - 1], PlayerKind.Bot))]);

    /// <summary>People sharing the device.</summary>
    public static UnoSetup PassAndPlay(int people) => new(
        T("Pass and play: {0} players", people),
        [.. Enumerable.Range(0, people).Select(i => new Player(i, T("Player {0}", i + 1), PlayerKind.Human))]);
}

/// <summary>
/// Uno at one table, on this device or online (<see cref="IUnoTable"/>): the other players along the top (with their card counts and scores), the draw pile and
/// the discard in the middle, and the viewer's hand at the bottom. Tap a card to play it, Draw to draw, and
/// Uno! before playing your second-to-last card. Bots take their turns by themselves; with several people
/// at the device, hands are hidden while it is passed on. Online, the server deals and checks every move.
/// </summary>
internal sealed class UnoScene : Scene
{
    private const int HudHeight = 64;
    private const int LogLines = 2;
    private static readonly TimeSpan FlightTime = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan RoundEndDelay = TimeSpan.FromMilliseconds(1100);

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly List<string> _log = [];
    private readonly List<Flight> _flights = [];
    private readonly ActionRepeater<GameAction> _repeater = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(110), GameAction.Left, GameAction.Right);
    private readonly Dictionary<int, Rectangle> _seats = [];
    private List<(UnoCard Card, Rectangle Bounds)> _hand = [];
    private UnoArt? _art;
    private Rectangle _drawPile;
    private Rectangle _discard;
    private bool _callUno;
    private int _selected;
    private TimeSpan _roundOverFor;
    private bool _roundShown;
    private int _round;
    private bool _problemShown;
    private string? _lastProblem;

    public UnoScene(GameContext context, UnoSetup setup, int? seed = null)
        : this(context, new LocalUnoTable(setup, seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000)))
    {
    }

    public UnoScene(GameContext context, IUnoTable table)
    {
        _context = context;
        Table = table;
        _buttons = new ButtonBar(
            new Button(T("Draw"), DrawOrKeep),
            new Button(T("Uno!"), () => _callUno = !_callUno),
            new Button(T("Menu"), () => Scenes.Push(new UnoPauseScene(context, this))));
        Say(Table.TakeEvents());
    }

    public IUnoTable Table { get; }

    public IReadOnlyList<string> Names => Table.Names;

    /// <summary>A fresh match: dealt here for a local table, asked of the server online.</summary>
    public void NewMatch()
    {
        if (Table is LocalUnoTable local)
        {
            Scenes.Replace(new UnoScene(_context, local.Setup), Navigation.Fade());
        }
        else
        {
            Table.NextRound();
        }
    }

    /// <summary>Deals the next round after the round-over panel.</summary>
    public void NextRound()
    {
        Table.NextRound();
        _flights.Clear();
        _log.Clear();
        Say(Table.TakeEvents());
    }

    /// <summary>Keeps an online table talking to the server while a panel covers this screen.</summary>
    public void KeepAlive(TimeSpan elapsed)
    {
        if (Table.IsOnline)
        {
            Table.Update(elapsed);
        }
    }

    /// <summary>The handover panel's "it's me" button.</summary>
    public void ConfirmHandover() => Table.ConfirmHandover();

    /// <summary>Plays the chosen colour for a wild picked from the hand.</summary>
    public void PlayWild(UnoCard card, UnoColor colour) => PlayHuman(new PlayCard(card, colour, _callUno));

    /// <summary>The table and its connection are this screen's to close.</summary>
    public void Leave() => Navigation.ToMainMenu(Scenes, _context);

    protected override void OnEnter() => _context.Sounds.PlayMusic(Music.Table);

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"{Table.Name}  round {Table.View?.RoundNumber}"),
    ];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        foreach (var flight in _flights)
        {
            flight.Tween.Update(elapsed);
        }

        _flights.RemoveAll(f => f.Tween.IsDone);
        Table.Update(elapsed);
        Show(Table.TakeEvents());
        if (Table.Problem is { } problem && problem != _lastProblem)
        {
            Say(problem);
        }

        _lastProblem = Table.Problem;
        if (Table.IsGone && !_problemShown)
        {
            _problemShown = true;
            Scenes.Push(new UnoGoneScene(_context, this, Table.Problem ?? T("The game has ended.")));
            return;
        }

        if (Table.View is not { } view)
        {
            return;
        }

        // A new round (dealt here, or by someone else online): start afresh.
        if (view.RoundNumber != _round)
        {
            _round = view.RoundNumber;
            _roundShown = false;
            _roundOverFor = TimeSpan.Zero;
        }

        if (view.IsOver)
        {
            _roundOverFor += elapsed;
            if (!_roundShown && _roundOverFor >= RoundEndDelay)
            {
                _roundShown = true;
                _context.Sounds.Play(Sound.Fanfare);
                Celebration.Confetti(Scenes);
                Scenes.Push(new UnoRoundScene(_context, this));
            }

            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new UnoPauseScene(_context, this));
            return;
        }

        if (Table.AwaitingHandover)
        {
            Scenes.Push(new UnoHandoverScene(_context, this, Names[view.CurrentSeat]));
            return;
        }

        if (!Table.CanAct || _buttons.Update(input))
        {
            return;
        }

        var actions = _context.Actions;
        var hand = view.Hand;
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
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        if (Table.View is not { } game)
        {
            batch.DrawText(fonts.Body, Table.Name, text, Palette.Text, Align.Left);
            batch.DrawText(fonts.Body, Table.Problem ?? T("Waiting for the deal…"), viewport, Palette.Muted);
            return;
        }

        var title = Table.IsOnline ? T("{0}, round {1}", Table.Name, game.RoundNumber) : T("Uno: round {0}", game.RoundNumber);
        batch.DrawText(fonts.Body, title, text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Small, T("first to {0}", game.Target), text, Palette.Muted, Align.Right, shadow: false);

        // The others along the top, in turn order starting after whoever sits at the bottom.
        var players = game.Players;
        var bottomSeat = game.Seat;
        var others = Enumerable.Range(1, players - 1).Select(i => (bottomSeat + i) % players).ToArray();
        var badgeWidth = Math.Min(fonts.Px(220), (viewport.Width - 2 * margin) / others.Length - margin / 2);
        var badgeHeight = fonts.Px(96);
        var badgesLeft = viewport.Center.X - (others.Length * badgeWidth + (others.Length - 1) * margin / 2) / 2;
        _seats.Clear();
        for (var i = 0; i < others.Length; i++)
        {
            var badge = new Rectangle(badgesLeft + i * (badgeWidth + margin / 2), hud.Bottom + margin / 2, badgeWidth, badgeHeight);
            _seats[others[i]] = badge;
            DrawBadge(batch, game, badge, others[i]);
        }

        // Footer: messages above the buttons.
        _buttons.Set(
            new Button(game.Drawn is null ? T("Draw") : T("Keep"), DrawOrKeep),
            new Button(_callUno ? T("Uno! (on)") : T("Uno!"), () => _callUno = !_callUno, Highlight: _callUno),
            new Button(T("Menu"), () => Scenes.Push(new UnoPauseScene(_context, this))));
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
        DrawHand(batch, game, viewport, log.Top - margin / 2 - cardHeight, cardWidth, cardHeight);

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
        var next = Name((game.CurrentSeat + game.Direction + players) % players);
        var direction = game.Direction > 0 ? T("clockwise") : T("counter-clockwise");
        var turn = game.IsOver ? ""
            : IsYou(game.CurrentSeat) ? T("You to play, then {0}   ({1}, {2})", next, ColorName(game.Color), direction)
            : T("{0} to play, then {1}   ({2}, {3})", Name(game.CurrentSeat), next, ColorName(game.Color), direction);
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
            Table.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A colour in the player's language.</summary>
    public static string ColorName(UnoColor color) => color switch
    {
        UnoColor.Red => T("Red"),
        UnoColor.Yellow => T("Yellow"),
        UnoColor.Green => T("Green"),
        _ => T("Blue"),
    };

    /// <summary>"Red 7", "Blue Skip", "Wild +4", in the player's language.</summary>
    public static string CardName(UnoCard card) => card.Value switch
    {
        UnoValue.Wild => T("Wild"),
        UnoValue.WildDrawFour => T("Wild +4"),
        _ => T("{0} {1}", ColorName(card.Color!.Value), Label(card)),
    };

    private static string Label(UnoCard card) => card.Value switch
    {
        UnoValue.Skip => T("Skip"),
        UnoValue.Reverse => T("Reverse"),
        _ => card.Label,
    };

    private string Name(int seat) => seat < Names.Count ? Names[seat] : T("Player {0}", seat + 1);

    /// <summary>Whether <paramref name="seat"/> is the person at this device (sentences about them read "You ...").</summary>
    private bool IsYou(int seat) => Name(seat) == T("You");

    private void DrawBadge(SpriteBatch batch, UnoView game, Rectangle badge, int seat)
    {
        var fonts = _context.Fonts;
        var current = !game.IsOver && game.CurrentSeat == seat;
        batch.FillRectangle(badge, current ? Palette.Accent : Palette.Panel);
        var inner = badge;
        inner.Inflate(-3, -3);
        batch.FillRectangle(inner, Palette.Panel);
        var count = game.HandCounts[seat];
        var name = string.Create(CultureInfo.InvariantCulture, $"{Name(seat)} ({game.Scores[seat]}){(count == 1 ? "  UNO!" : "")}");
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

    private void DrawHand(SpriteBatch batch, UnoView game, Rectangle viewport, int top, int width, int height)
    {
        var fonts = _context.Fonts;
        var visible = Table.HandVisible;
        var count = visible ? game.Hand.Count : game.HandCounts[game.Seat];
        var playing = visible && Table.CanAct;
        var avail = viewport.Width - 2 * fonts.Px(16);
        var step = count <= 1 ? 0 : Math.Min(width + fonts.Px(8), (avail - width) / (count - 1));
        var left = viewport.Center.X - (width + step * Math.Max(0, count - 1)) / 2;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, count - 1));
        _hand = [];
        for (var i = 0; i < count; i++)
        {
            var bounds = new Rectangle(left + i * step, top, width, height);
            if (!visible)
            {
                _art!.DrawBack(batch, bounds);
                continue;
            }

            var card = game.Hand[i];
            var playable = playing && game.CanPlay(card);
            bounds.Y -= playable ? height / 7 : 0;
            _hand.Add((card, bounds));
            if (playing && i == _selected)
            {
                var outline = bounds;
                outline.Inflate(4, 4);
                batch.FillRectangle(outline, Palette.Accent);
            }

            _art!.Draw(batch, card, bounds, dim: playing && !playable);
        }

        if (!visible && count > 0)
        {
            batch.DrawText(fonts.Body, T("{0}'s hand", Name(game.Seat)), new Vector2(viewport.Center.X, top + height / 2 - fonts.Body.LineHeight / 2), Palette.Text, Align.Center);
        }
    }

    private void Choose(UnoCard card)
    {
        if (Table.View is not { } game)
        {
            return;
        }

        if (game.Drawn is { } drawn && drawn != card)
        {
            Say(T("Play the card you drew, or keep it"));
            return;
        }

        if (!game.CanPlay(card))
        {
            Say(card.Value == UnoValue.WildDrawFour
                ? T("A Wild +4 only goes when you have nothing in the current colour")
                : T("That doesn't match the {0} {1}", ColorName(game.Color), Label(game.Top)));
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
        if (Table.CanAct && Table.View is { } game)
        {
            PlayHuman(game.Drawn is null ? DrawCard.Instance : Uno.Pass.Instance);
        }
    }

    private void PlayHuman(UnoMove move)
    {
        if (!Table.CanAct)
        {
            return;
        }

        Table.Play(move);
        if (move is PlayCard)
        {
            _callUno = false;
        }

        Show(Table.TakeEvents());
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
        foreach (var e in events)
        {
            Say(e switch
            {
                CardPlayed { Card.IsWild: true } p => IsYou(p.Seat)
                    ? T("You play {0} and pick {1}", CardName(p.Card), ColorName(p.Color))
                    : T("{0} plays {1} and picks {2}", Name(p.Seat), CardName(p.Card), ColorName(p.Color)),
                CardPlayed p => IsYou(p.Seat) ? T("You play {0}", CardName(p.Card)) : T("{0} plays {1}", Name(p.Seat), CardName(p.Card)),
                CardsDrawn d => IsYou(d.Seat)
                    ? N(d.Count, "You draw a card", "You draw {0} cards")
                    : N(d.Count, "{1} draws a card", "{1} draws {0} cards", Name(d.Seat)),
                PlayerSkipped s => IsYou(s.Seat) ? T("You are skipped") : T("{0} is skipped", Name(s.Seat)),
                DirectionReversed => T("Play turns around"),
                UnoCalled u => T("{0}: Uno!", Name(u.Seat)),
                UnoForgotten u => IsYou(u.Seat) ? T("You forgot to call Uno: two cards") : T("{0} forgot to call Uno: two cards", Name(u.Seat)),
                DrawPileReshuffled => T("The discards are shuffled into a new draw pile"),
                RoundWon w => IsYou(w.Seat) ? T("You win the round!") : T("{0} wins the round!", Name(w.Seat)),
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
