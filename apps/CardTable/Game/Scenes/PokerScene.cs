using System.Globalization;
using Jentl.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Multiplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Poker;
using Games.Input;
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
    private static readonly Color Felt = new(28, 110, 70);
    private static readonly Color Rim = new(92, 58, 34);

    private readonly GameContext _context;
    private readonly PokerSetup _setup;
    private readonly int _seed;
    private readonly PokerBot[] _bots;
    private readonly ButtonBar _buttons = new();
    private readonly List<string> _log = [];
    private readonly Dictionary<int, string> _lastAction = [];
    private readonly HashSet<int> _shown = [];
    private Texture2D? _circle;
    private Match<HoldemHand, PokerAction> _runner;
    private int _raiseTo;
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
        Table = new HoldemTable(setup.Players.Count, _seed, StartingStack);
        _runner = DealHand();
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
        _raiseTo = Math.Clamp(_raiseTo, Math.Min(Hand.MinRaiseTo, Hand.MaxRaiseTo(seat)), Hand.MaxRaiseTo(seat));
        _buttons.Set(Controls(seat));
        if (_buttons.Update(input))
        {
            return;
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Act(Hand.ToCall(seat) > 0 ? CallBet.Instance : Check.Instance);
        }
        else if (actions.WasPressed(input, GameAction.Up))
        {
            Raise();
        }
        else if (actions.WasPressed(input, GameAction.Left))
        {
            Step(-1);
        }
        else if (actions.WasPressed(input, GameAction.Right))
        {
            Step(1);
        }
        else if (input.WasPressed(Keys.F) && Hand.ToCall(seat) > 0)
        {
            Act(Fold.Instance);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        _circle ??= Circle(batch.GraphicsDevice);
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

        // The table: an oval of felt with the seats around its edge.
        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - 2 * margin, logTop - hud.Bottom - 2 * margin);
        var seatWidth = Math.Min(fonts.Px(190), area.Width / 3);
        var seatHeight = Math.Min(fonts.Px(150), area.Height / 3);
        var felt = new Rectangle(area.X + seatWidth / 3, area.Y + seatHeight / 2, area.Width - seatWidth * 2 / 3, area.Height - seatHeight);
        var rim = felt;
        rim.Inflate(fonts.Px(8), fonts.Px(8));
        batch.Draw(_circle, rim, Rim);
        batch.Draw(_circle, felt, Felt);

        var cardHeight = Math.Min(Math.Min(felt.Height / 3, fonts.Px(110)), (int)((felt.Width * 0.8f - 4 * fonts.Px(6)) / 5 / Jentl.Core.Cards.CardArt.AspectRatio));
        var cardWidth = (int)(cardHeight * Jentl.Core.Cards.CardArt.AspectRatio);
        var boardWidth = 5 * cardWidth + 4 * fonts.Px(6);
        var boardLeft = felt.Center.X - boardWidth / 2;
        var boardTop = felt.Center.Y - cardHeight / 2;
        for (var i = 0; i < 5; i++)
        {
            var bounds = new Rectangle(boardLeft + i * (cardWidth + fonts.Px(6)), boardTop, cardWidth, cardHeight);
            if (i < hand.Board.Count)
            {
                _context.Cards.Draw(batch, new PlacedCard(hand.Board[i], FaceUp: true), bounds);
            }
            else
            {
                _context.Cards.DrawSlot(batch, bounds);
            }
        }

        batch.DrawText(fonts.Body, T("Pot {0}", hand.Pot), new Vector2(felt.Center.X, boardTop - fonts.Body.LineHeight - fonts.Px(4)), Palette.Accent, Align.Center);

        var viewer = _runner.Viewer ?? (_runner.Current.IsHuman ? _runner.Current.Seat : Players.First(p => p.IsHuman).Seat);
        for (var k = 0; k < Players.Count; k++)
        {
            var seat = (viewer + k) % Players.Count;

            // Clockwise round the oval from the viewer at the bottom: the others spread from lower left over the
            // top to lower right, leaving the middle row to the board.
            var angle = k == 0 ? MathF.PI / 2
                : Players.Count == 2 ? MathF.PI * 1.5f
                : MathF.PI * (5f / 6) + (k - 1) * (MathF.PI * 4f / 3) / (Players.Count - 2);
            var centre = new Vector2(
                felt.Center.X + MathF.Cos(angle) * (felt.Width / 2f),
                felt.Center.Y + MathF.Sin(angle) * (felt.Height / 2f));
            var box = new Rectangle((int)centre.X - seatWidth / 2, (int)centre.Y - seatHeight / 2, seatWidth, seatHeight);
            box.X = Math.Clamp(box.X, area.X, area.Right - seatWidth);
            box.Y = Math.Clamp(box.Y, area.Y, area.Bottom - seatHeight);
            DrawSeat(batch, seat, box, felt.Center.ToVector2(), seat == _runner.Viewer || _shown.Contains(seat));
        }

        _buttons.Draw(batch, buttonFont, buttons);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _circle?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A white disc, stretched into the table's oval and tinted.</summary>
    private static Texture2D Circle(GraphicsDevice device)
    {
        const int Size = 256;
        var pixels = new Color[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = (x + 0.5f) / Size * 2 - 1;
                var dy = (y + 0.5f) / Size * 2 - 1;
                var edge = Math.Clamp((1 - MathF.Sqrt(dx * dx + dy * dy)) * Size / 2, 0, 1);
                pixels[y * Size + x] = new Color(Color.White, edge);
            }
        }

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(pixels);
        return texture;
    }

    private Match<HoldemHand, PokerAction> DealHand()
    {
        var hand = Table.DealHand();
        _lastAction.Clear();
        _shown.Clear();
        var runner = new Match<HoldemHand, PokerAction>(hand, Players, s => _bots[s], unchecked(_seed + Table.HandNumber)) { BotDelay = BotDelay };
        Say(T("Hand {0}, dealer: {1}", Table.HandNumber, Players[hand.Button].Name));
        foreach (var e in hand.TakeEvents())
        {
            Describe(e);
        }

        _raiseTo = hand.MinRaiseTo;
        return runner;
    }

    private Button[] Controls(int seat)
    {
        var hand = Hand;
        var toCall = hand.ToCall(seat);
        var max = hand.MaxRaiseTo(seat);
        var moves = hand.LegalMoves(seat);
        var canRaise = moves.Any(m => m is RaiseTo);
        var buttons = new List<Button>();
        if (toCall > 0)
        {
            buttons.Add(new Button(T("Fold"), () => Act(Fold.Instance)));
        }

        buttons.Add(toCall == 0 ? new Button(T("Check"), () => Act(Check.Instance), Highlight: true)
            : moves.Contains(CallBet.Instance) ? new Button(T("Call {0}", toCall), () => Act(CallBet.Instance), Highlight: true)
            : new Button(T("Call all in {0}", toCall), () => Act(AllIn.Instance), Highlight: true));
        if (canRaise)
        {
            buttons.Add(new Button("-", () => Step(-1)));
            buttons.Add(new Button(hand.CurrentBet == 0 ? T("Bet {0}", _raiseTo) : T("Raise to {0}", _raiseTo), Raise));
            buttons.Add(new Button("+", () => Step(1)));
        }

        if (moves.Contains(AllIn.Instance) && max > toCall + hand.Bet(seat))
        {
            buttons.Add(new Button(T("All in"), () => Act(AllIn.Instance)));
        }

        buttons.Add(new Button(T("Menu"), Pause));
        return [.. buttons];
    }

    private void Step(int direction)
    {
        if (_runner.IsHumansTurn)
        {
            var seat = _runner.Current.Seat;
            _raiseTo = Math.Clamp(_raiseTo + direction * Table.BigBlind, Hand.MinRaiseTo, Hand.MaxRaiseTo(seat));
        }
    }

    private void Raise()
    {
        if (!_runner.IsHumansTurn || !Hand.LegalMoves(_runner.Current.Seat).Any(m => m is RaiseTo))
        {
            return;
        }

        Act(_raiseTo >= Hand.MaxRaiseTo(_runner.Current.Seat) ? AllIn.Instance : new RaiseTo(_raiseTo));
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

                var label = a.Action switch
                {
                    Fold => T("Fold"),
                    Check => T("Check"),
                    CallBet => T("Call {0}", a.Amount),
                    AllIn => T("All in {0}", a.Total),
                    _ when a.Opened => T("Bet {0}", a.Total),
                    _ => T("Raise to {0}", a.Total),
                };
                _lastAction[a.Seat] = label;
                Say($"{Name(a.Seat)}: {label}");
                break;
            case StreetDealt d:
                foreach (var seat in _lastAction.Keys.Where(s => _lastAction[s] != T("Fold") && !_lastAction[s].StartsWith(T("All in"), StringComparison.Ordinal)).ToArray())
                {
                    _lastAction.Remove(seat);
                }

                Say(T("{0}: {1}", StreetName(d.Street), string.Join(" ", d.Cards)));
                break;
            case HandShown s:
                _shown.Add(s.Seat);
                _lastAction[s.Seat] = Describe(s.Value);
                break;
            case PotWon w:
                _context.Sounds.Play(Sound.Chips);
                Say(w.Value is { } value
                    ? T("{0} wins {1}: {2}", Name(w.Seat), w.Amount, Describe(value))
                    : T("{0} takes {1}", Name(w.Seat), w.Amount));
                break;
        }
    }

    private void DrawSeat(SpriteBatch batch, int seat, Rectangle box, Vector2 middle, bool faceUp)
    {
        var fonts = _context.Fonts;
        var hand = Hand;
        var current = !hand.IsOver && hand.CurrentSeat == seat;
        var isOut = !hand.InHand(seat);
        batch.FillRectangle(box, current ? Palette.Accent : Palette.Panel);
        var inner = box;
        inner.Inflate(-3, -3);
        batch.FillRectangle(inner, isOut || hand.HasFolded(seat) ? Palette.Background : Palette.Panel);

        var nameLine = string.Create(CultureInfo.InvariantCulture, $"{Players[seat].Name}  {hand.Stack(seat)}");
        batch.DrawText(fonts.Small, nameLine, new Vector2(box.Center.X, box.Y + fonts.Px(4)), current ? Palette.Accent : Palette.Text, Align.Center, shadow: false);

        // Hole cards: face up for the viewer and at a showdown, backs otherwise, none once folded or out.
        var cardHeight = box.Height - (int)fonts.Small.LineHeight * 2 - fonts.Px(14);
        var cardWidth = (int)(cardHeight * Jentl.Core.Cards.CardArt.AspectRatio);
        var hole = hand.Hole(seat);
        if (!isOut && !hand.HasFolded(seat))
        {
            for (var i = 0; i < hole.Count; i++)
            {
                var bounds = new Rectangle(box.Center.X - cardWidth + i * (cardWidth + 2) - 1, box.Y + (int)fonts.Small.LineHeight + fonts.Px(6), cardWidth, cardHeight);
                _context.Cards.Draw(batch, new PlacedCard(hole[i], faceUp), bounds);
            }
        }

        var status = isOut ? T("Out") : _lastAction.GetValueOrDefault(seat) ?? (hand.IsAllIn(seat) ? T("All in") : "");
        batch.DrawText(fonts.Small, status, new Vector2(box.Center.X, box.Bottom - fonts.Small.LineHeight - fonts.Px(4)), Palette.Muted, Align.Center, shadow: false);

        // The bet in front of the seat, towards the middle, and the dealer button.
        var towards = Vector2.Lerp(box.Center.ToVector2(), middle, 0.42f);
        if (hand.Bet(seat) > 0)
        {
            batch.DrawText(fonts.Small, hand.Bet(seat).ToString(CultureInfo.InvariantCulture), towards, Palette.Accent, Align.Center);
        }

        if (seat == hand.Button)
        {
            var size = fonts.Px(26);
            var at = new Rectangle(box.Right - size / 2 - fonts.Px(4), box.Y - size / 3, size, size);
            batch.Draw(_circle!, at, Color.White);
            batch.DrawText(fonts.Sized(size * 0.6f), "D", at, Palette.Ink, shadow: false);
        }
    }

    /// <summary>A poker hand in words, in the player's language: "Two pair, kings and fives".</summary>
    private static string Describe(HandValue value)
    {
        var r = value.Ranks;
        return value.Category switch
        {
            HandCategory.StraightFlush when r[0] == 14 => T("Royal flush"),
            HandCategory.StraightFlush => T("Straight flush, {0} high", RankName(r[0])),
            HandCategory.FourOfAKind => T("Four {0}", RankNames(r[0])),
            HandCategory.FullHouse => T("Full house, {0} over {1}", RankNames(r[0]), RankNames(r[1])),
            HandCategory.Flush => T("Flush, {0} high", RankName(r[0])),
            HandCategory.Straight => T("Straight, {0} high", RankName(r[0])),
            HandCategory.ThreeOfAKind => T("Three {0}", RankNames(r[0])),
            HandCategory.TwoPair => T("Two pair, {0} and {1}", RankNames(r[0]), RankNames(r[1])),
            HandCategory.Pair => T("Pair of {0}", RankNames(r[0])),
            _ => T("{0} high", RankName(r[0])),
        };
    }

    private static string RankName(int rank) => rank switch
    {
        14 => T("ace"),
        13 => T("king"),
        12 => T("queen"),
        11 => T("jack"),
        10 => T("ten"),
        9 => T("nine"),
        8 => T("eight"),
        7 => T("seven"),
        6 => T("six"),
        5 => T("five"),
        4 => T("four"),
        3 => T("three"),
        _ => T("two"),
    };

    private static string RankNames(int rank) => rank switch
    {
        14 => T("aces"),
        13 => T("kings"),
        12 => T("queens"),
        11 => T("jacks"),
        10 => T("tens"),
        9 => T("nines"),
        8 => T("eights"),
        7 => T("sevens"),
        6 => T("sixes"),
        5 => T("fives"),
        4 => T("fours"),
        3 => T("threes"),
        _ => T("twos"),
    };

    private static string StreetName(Street street) => street switch
    {
        Street.Flop => T("Flop"),
        Street.Turn => T("Turn"),
        Street.River => T("River"),
        _ => street.ToString(),
    };

    private void Say(string message)
    {
        _log.Add(message);
        if (_log.Count > 20)
        {
            _log.RemoveAt(0);
        }
    }
}
