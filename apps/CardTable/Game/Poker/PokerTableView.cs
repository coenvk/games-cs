using System.Globalization;
using Jentl.Cards;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker;
using static Games.UI.Tr;

namespace Games.Poker;

/// <summary>
/// A Hold'em table drawn from a hand, for 2 to 9 seats: an oval of felt with the seats round its edge, each
/// with its name, its stack as chips and a number, its cards and its bet in front of it; the board, the pot and
/// each side pot in the middle; the dealer button and the blinds. Fed the hand's events, it flies the bets into
/// the pot when a street's betting ends and the pot to the winners when the hand is won.
/// </summary>
internal sealed class PokerTableView(GameContext context) : IDisposable
{
    private const int MaxPerStack = 10;
    private static readonly TimeSpan GatherTime = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AwardTime = TimeSpan.FromMilliseconds(650);
    private static readonly Color Felt = new(28, 110, 70);
    private static readonly Color Rim = new(92, 58, 34);
    private static readonly Color BlindMarker = new(98, 176, 230);

    // The bets in front of the seats as the events tell them, to fly into the pot when the street ends.
    private readonly Dictionary<int, int> _bets = [];
    private readonly List<Flight> _won = [];
    private readonly Queue<Stage> _stages = new();
    private Stage? _stage;
    private Texture2D? _circle;
    private HoldemHand? _hand;
    private Layout? _layout;
    private SeatText[] _texts = [];

    // The pots in the middle as they stood when the bets were last gathered, and the next ones, shown once the
    // chips flying in have landed.
    private Pot[] _pots = [];
    private Pot[]? _nextPots;
    private Street _potsStreet;
    private bool _awarding;
    private PotLabel[]? _middle;

    /// <summary>The seat drawn at the bottom of the table (the one whose cards the viewer holds).</summary>
    public int BottomSeat { get; set; }

    /// <summary>True while chips are on their way to the pot or to a winner.</summary>
    public bool IsAnimating => _stage is not null || _stages.Count > 0;

    /// <summary>
    /// The seats' centres on the felt's ellipse in pixels: seat 0 at the bottom centre, then clockwise. See
    /// <see cref="SeatLayout.Positions"/>.
    /// </summary>
    public static Vector2[] SeatPositions(int seats, Rectangle felt)
    {
        var unit = SeatLayout.Positions(seats);
        var positions = new Vector2[unit.Length];
        for (var i = 0; i < unit.Length; i++)
        {
            positions[i] = new Vector2(felt.Center.X + (unit[i].X * felt.Width / 2f), felt.Center.Y + (unit[i].Y * felt.Height / 2f));
        }

        return positions;
    }

    /// <summary>Follows the hand's events and moves the chips in flight on by <paramref name="elapsed"/>.</summary>
    public void Update(TimeSpan elapsed, IReadOnlyList<PokerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _won.Clear();
        for (var i = 0; i < events.Count; i++)
        {
            switch (events[i])
            {
                case BlindPosted b:
                    _bets[b.Seat] = _bets.GetValueOrDefault(b.Seat) + b.Amount;
                    break;
                case Acted a:
                    _bets[a.Seat] = a.Total;
                    break;
                case StreetDealt:
                    Gather();
                    break;
                case PotWon w:
                    // A hand can end mid-street, with bets still out: those go to the pot first.
                    Gather();
                    if (_layout is { } layout && w.Seat < layout.Centres.Length)
                    {
                        _won.Add(new Flight(new ChipFlight(w.Amount, layout.Pot, layout.Centres[w.Seat], AwardTime), w.Seat));
                    }

                    break;
            }
        }

        if (_won.Count > 0)
        {
            _stages.Enqueue(new Stage(Award: true, [.. _won]));
        }

        if (_stage is null || _stage.IsDone)
        {
            Next();
        }

        if (_stage is { } stage)
        {
            foreach (var flight in stage.Flights)
            {
                flight.Chips.Update(elapsed);
            }

            if (stage.IsDone)
            {
                Next();
            }
        }
    }

    /// <summary>
    /// Draws the table in <paramref name="viewport"/>: <paramref name="names"/> by seat, hole cards face up where
    /// <paramref name="faceUp"/> says so, and each seat's last action (or blind, or hand at a showdown) under it.
    /// </summary>
    public void Draw(SpriteBatch batch, Rectangle viewport, HoldemHand hand, IReadOnlyList<string> names, Func<int, bool> faceUp, IReadOnlyDictionary<int, string> lastAction)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(faceUp);
        ArgumentNullException.ThrowIfNull(lastAction);
        _circle ??= Circle(batch.GraphicsDevice);
        Follow(hand);
        var layout = Arrange(viewport, hand.Seats);
        _middle ??= Middle(hand, names, layout.ChipWidth);

        batch.Draw(_circle, layout.Rim, Rim);
        batch.Draw(_circle, layout.Felt, Felt);
        for (var i = 0; i < layout.Board.Length; i++)
        {
            if (i < hand.Board.Count)
            {
                context.Cards.Draw(batch, new PlacedCard(hand.Board[i], FaceUp: true), layout.Board[i]);
            }
            else
            {
                context.Cards.DrawSlot(batch, layout.Board[i]);
            }
        }

        DrawPots(batch, layout);
        for (var seat = 0; seat < hand.Seats; seat++)
        {
            DrawSeat(batch, hand, seat, layout, names[seat], faceUp(seat), lastAction.GetValueOrDefault(seat));

            // A hand won by a fold ends with the street's bets still out; they have gone to the winner with the pot.
            if (!hand.IsOver && hand.Bet(seat) > 0)
            {
                DrawBet(batch, layout, seat, hand.Bet(seat));
            }
        }

        foreach (var flight in _stage?.Flights ?? [])
        {
            ChipRenderer.DrawAmount(batch, flight.Chips.Position, flight.Chips.Amount, layout.ChipWidth, MaxPerStack);
        }
    }

    public void Dispose() => _circle?.Dispose();

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

    /// <summary>The pots that can be won: an uncalled bet (a one-seat "pot") goes back, so it is left out.</summary>
    private static Pot[] Contested(HoldemHand hand)
    {
        var pots = new List<Pot>();
        foreach (var pot in hand.Pots)
        {
            if (pot.Seats.Count > 1)
            {
                pots.Add(pot);
            }
        }

        return [.. pots];
    }

    /// <summary>Notices a new hand, a new street and the end of the hand, and picks the pots to show.</summary>
    private void Follow(HoldemHand hand)
    {
        if (!ReferenceEquals(hand, _hand))
        {
            // A new hand: whatever was still flying from the last one has landed.
            _hand = hand;
            _pots = [];
            _nextPots = null;
            _potsStreet = hand.Street;
            _awarding = false;
            _stages.Clear();
            _stage = null;
            _middle = null;
        }

        // The first frame of a street, before anyone has bet on it: the engine's pots are the gathered chips.
        if (!hand.IsOver && hand.Street != _potsStreet)
        {
            _potsStreet = hand.Street;
            _nextPots = Contested(hand);
        }

        if (_nextPots is not null && !Gathering())
        {
            _pots = _nextPots;
            _nextPots = null;
            _middle = null;
        }
    }

    /// <summary>Where everything goes for this viewport and seat count, worked out again only when either changes.</summary>
    private Layout Arrange(Rectangle viewport, int seats)
    {
        var fonts = context.Fonts;
        if (_layout is { } cached && cached.Viewport == viewport && cached.Centres.Length == seats && cached.BottomSeat == BottomSeat && cached.Scale == fonts.Scale)
        {
            return cached;
        }

        var crowded = seats > 6;
        var seatWidth = Math.Min(fonts.Px(crowded ? 160 : 190), viewport.Width / (crowded ? 4 : 3));
        var seatHeight = Math.Min(fonts.Px(crowded ? 130 : 150), viewport.Height / 3);
        var felt = new Rectangle(viewport.X + seatWidth / 3, viewport.Y + seatHeight / 2, viewport.Width - seatWidth * 2 / 3, viewport.Height - seatHeight);
        var rim = felt;
        rim.Inflate(fonts.Px(8), fonts.Px(8));

        // The seats round the edge, turned so that the bottom seat is at the bottom.
        var around = SeatPositions(seats, felt);
        var boxes = new Rectangle[seats];
        var centres = new Vector2[seats];
        var bets = new Vector2[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var centre = around[((seat - BottomSeat) % seats + seats) % seats];
            var box = new Rectangle((int)centre.X - seatWidth / 2, (int)centre.Y - seatHeight / 2, seatWidth, seatHeight);
            box.X = Math.Clamp(box.X, viewport.X, viewport.Right - seatWidth);
            box.Y = Math.Clamp(box.Y, viewport.Y, viewport.Bottom - seatHeight);
            boxes[seat] = box;
            centres[seat] = box.Center.ToVector2();
            bets[seat] = BetSpot(box, felt.Center.ToVector2());
        }

        // The board across the middle, the pots above it.
        var gap = fonts.Px(6);
        var chipWidth = (float)fonts.Px(20);
        var potHeight = chipWidth * 1.4f + fonts.Small.LineHeight * 2;
        var cardHeight = Math.Min(Math.Min(felt.Height / 4, fonts.Px(100)), (int)((felt.Width * 0.45f - 4 * gap) / 5 / CardArt.AspectRatio));
        var cardWidth = (int)(cardHeight * CardArt.AspectRatio);
        var boardWidth = 5 * cardWidth + 4 * gap;
        var boardTop = (int)(felt.Center.Y - (cardHeight - potHeight) / 2);
        var board = new Rectangle[5];
        for (var i = 0; i < board.Length; i++)
        {
            board[i] = new Rectangle(felt.Center.X - boardWidth / 2 + i * (cardWidth + gap), boardTop, cardWidth, cardHeight);
        }

        var pot = new Vector2(felt.Center.X, boardTop - gap - fonts.Small.LineHeight * 2);
        _layout = new Layout(viewport, BottomSeat, fonts.Scale, felt, rim, boxes, centres, bets, board, pot, chipWidth, fonts.Px(16));
        _texts = new SeatText[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            _texts[seat] = new SeatText();
        }

        _middle = null;
        return _layout;
    }

    /// <summary>In front of the seat's box, a little way towards the middle of the table.</summary>
    private Vector2 BetSpot(Rectangle box, Vector2 middle)
    {
        var centre = box.Center.ToVector2();
        var direction = middle - centre;
        if (direction.LengthSquared() < 1)
        {
            return centre;
        }

        direction.Normalize();
        var toEdge = MathF.Min(
            direction.X == 0 ? float.MaxValue : box.Width / 2f / MathF.Abs(direction.X),
            direction.Y == 0 ? float.MaxValue : box.Height / 2f / MathF.Abs(direction.Y));
        return centre + direction * (toEdge + context.Fonts.Px(30));
    }

    /// <summary>Puts every bet out into a flight to the pot.</summary>
    private void Gather()
    {
        if (_layout is { } layout)
        {
            var flights = new List<Flight>();
            foreach (var (seat, amount) in _bets)
            {
                if (amount > 0 && seat < layout.Bets.Length)
                {
                    flights.Add(new Flight(new ChipFlight(amount, layout.Bets[seat], layout.Pot, GatherTime), seat));
                }
            }

            if (flights.Count > 0)
            {
                _stages.Enqueue(new Stage(Award: false, [.. flights]));
            }
        }

        _bets.Clear();
    }

    private void Next()
    {
        _stage = _stages.TryDequeue(out var next) ? next : null;
        if (_stage is { Award: true } && !_awarding)
        {
            _awarding = true;
            _middle = null;
        }
    }

    /// <summary>True while bets are flying into the pot (or waiting to).</summary>
    private bool Gathering()
    {
        if (_stage is { Award: false })
        {
            return true;
        }

        foreach (var stage in _stages)
        {
            if (!stage.Award)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Chips still flying to <paramref name="seat"/> as winnings: its stack shows them once they land.</summary>
    private int Arriving(int seat)
    {
        var amount = 0;
        if (_stage is { Award: true })
        {
            amount += Arriving(_stage, seat);
        }

        foreach (var stage in _stages)
        {
            if (stage.Award)
            {
                amount += Arriving(stage, seat);
            }
        }

        return amount;
    }

    private static int Arriving(Stage stage, int seat)
    {
        var amount = 0;
        foreach (var flight in stage.Flights)
        {
            if (flight.Seat == seat && !flight.Chips.IsDone)
            {
                amount += flight.Chips.Amount;
            }
        }

        return amount;
    }

    /// <summary>
    /// What the middle shows: while the hand runs, the main pot and the side pots as chips; once it is won (and the
    /// chips have flown), each contested pot with its winners, or the pot the last player standing took.
    /// </summary>
    private PotLabel[] Middle(HoldemHand hand, IReadOnlyList<string> names, float chipWidth)
    {
        var labels = new List<PotLabel>();
        if (hand.IsOver && _awarding)
        {
            var live = -1;
            var liveCount = 0;
            for (var seat = 0; seat < hand.Seats; seat++)
            {
                if (hand.IsLive(seat))
                {
                    live = seat;
                    liveCount++;
                }
            }

            if (liveCount == 1)
            {
                // Won by folds: the winner's chips above what anyone else put in were never called and went back.
                var called = 0;
                for (var seat = 0; seat < hand.Seats; seat++)
                {
                    if (seat != live)
                    {
                        called = Math.Max(called, hand.Committed(seat));
                    }
                }

                var uncalled = Math.Max(0, hand.Committed(live) - called);
                labels.Add(Label(0, hand.Pot - uncalled, names[live], chipWidth));
            }
            else
            {
                foreach (var result in hand.PotResults)
                {
                    if (result.Seats.Count > 1)
                    {
                        var winners = new string[result.Winners.Count];
                        for (var i = 0; i < winners.Length; i++)
                        {
                            winners[i] = names[result.Winners[i].Seat];
                        }

                        labels.Add(Label(labels.Count, result.Amount, string.Join(", ", winners), chipWidth));
                    }
                }
            }
        }
        else
        {
            foreach (var pot in _pots)
            {
                labels.Add(Label(labels.Count, pot.Amount, "", chipWidth));
            }
        }

        return [.. labels];
    }

    private PotLabel Label(int index, int amount, string winners, float chipWidth)
    {
        var font = context.Fonts.Small;
        var text = index == 0 ? T("Pot {0}", amount) : T("Side pot {0}", amount);
        var width = Math.Max(Math.Max(font.MeasureString(text).X, font.MeasureString(winners).X), ChipRenderer.Width(amount, chipWidth, MaxPerStack));
        return new PotLabel(amount, text, winners, width);
    }

    /// <summary>The pots side by side above the board: chips while the hand runs, winners' names once it is won.</summary>
    private void DrawPots(SpriteBatch batch, Layout layout)
    {
        var pots = _middle!;
        if (pots.Length == 0)
        {
            return;
        }

        var fonts = context.Fonts;
        var gap = fonts.Px(24);
        var total = gap * (pots.Length - 1f);
        foreach (var pot in pots)
        {
            total += pot.Width;
        }

        var x = layout.Pot.X - total / 2;
        foreach (var pot in pots)
        {
            var centre = x + pot.Width / 2;
            if (pot.Winners.Length == 0)
            {
                ChipRenderer.DrawAmount(batch, new Vector2(centre, layout.Pot.Y), pot.Amount, layout.ChipWidth, MaxPerStack);
            }
            else
            {
                batch.DrawText(fonts.Small, pot.Winners, new Vector2(centre, layout.Pot.Y - fonts.Small.LineHeight), Palette.Text, Align.Center);
            }

            batch.DrawText(fonts.Small, pot.Label, new Vector2(centre, layout.Pot.Y + fonts.Px(2)), Palette.Accent, Align.Center);
            x += pot.Width + gap;
        }
    }

    /// <summary>A seat's bet as chips in front of it, with its number beside them.</summary>
    private void DrawBet(SpriteBatch batch, Layout layout, int seat, int amount)
    {
        var text = _texts[seat];
        if (text.Bet != amount)
        {
            text.Bet = amount;
            text.BetText = amount.ToString(CultureInfo.InvariantCulture);
            text.BetWidth = ChipRenderer.Width(amount, layout.ChipWidth, MaxPerStack);
        }

        var font = context.Fonts.Small;
        var spot = layout.Bets[seat];
        ChipRenderer.DrawAmount(batch, spot, amount, layout.ChipWidth, MaxPerStack);
        batch.DrawText(font, text.BetText, new Vector2(spot.X + text.BetWidth / 2 + context.Fonts.Px(4), spot.Y - font.LineHeight * 0.8f), Palette.Accent, Align.Left);
    }

    private void DrawSeat(SpriteBatch batch, HoldemHand hand, int seat, Layout layout, string name, bool faceUp, string? lastAction)
    {
        var fonts = context.Fonts;
        var font = fonts.Small;
        var box = layout.Boxes[seat];
        var current = !hand.IsOver && hand.CurrentSeat == seat;
        var isOut = !hand.InHand(seat);
        batch.FillRectangle(box, current ? Palette.Accent : Palette.Panel);
        var inner = box;
        inner.Inflate(-3, -3);
        batch.FillRectangle(inner, isOut || hand.HasFolded(seat) ? Palette.Background : Palette.Panel);

        // The name and the stack's number on top, the hole cards under them, the stack's chips, then the last action.
        var pad = fonts.Px(6);
        var stack = hand.Stack(seat) - Arriving(seat);
        var text = _texts[seat];
        if (text.Stack != stack)
        {
            // Many chips are drawn narrower to fit the box.
            text.Stack = stack;
            text.StackText = stack.ToString(CultureInfo.InvariantCulture);
            var width = ChipRenderer.Width(stack, layout.SeatChipWidth, MaxPerStack);
            text.StackChipWidth = width > inner.Width - 2 * pad ? layout.SeatChipWidth * (inner.Width - 2 * pad) / width : layout.SeatChipWidth;
        }

        var top = new Rectangle(inner.X + pad, inner.Y + fonts.Px(2), inner.Width - 2 * pad, (int)font.LineHeight);
        batch.DrawText(font, name, new Vector2(top.X, top.Y), current ? Palette.Accent : Palette.Text, Align.Left, shadow: false);
        batch.DrawText(font, text.StackText, new Vector2(top.Right, top.Y), Palette.Muted, Align.Right, shadow: false);

        // The cards leave room for the tallest stack of full-width chips, whatever the stack is now.
        var statusTop = inner.Bottom - (int)font.LineHeight - fonts.Px(2);
        var chipsBottom = statusTop - fonts.Px(2);
        var cardTop = top.Bottom + fonts.Px(4);
        var cardHeight = Math.Max(0, (int)(chipsBottom - layout.SeatChipWidth * (0.45f + 0.2f * (MaxPerStack - 1)) - fonts.Px(4)) - cardTop);
        var cardWidth = (int)(cardHeight * CardArt.AspectRatio);
        var hole = hand.Hole(seat);
        if (!isOut && !hand.HasFolded(seat))
        {
            for (var i = 0; i < hole.Count; i++)
            {
                var bounds = new Rectangle(inner.Center.X - cardWidth + i * (cardWidth + 2) - 1, cardTop, cardWidth, cardHeight);
                context.Cards.Draw(batch, new PlacedCard(hole[i], faceUp), bounds);
            }
        }

        if (stack > 0)
        {
            ChipRenderer.DrawAmount(batch, new Vector2(inner.Center.X, chipsBottom), stack, text.StackChipWidth, MaxPerStack);
        }

        var status = isOut ? T("Out") : lastAction ?? (hand.IsAllIn(seat) ? T("All in") : "");
        batch.DrawText(font, status, new Vector2(inner.Center.X, statusTop), Palette.Muted, Align.Center, shadow: false);

        // The dealer button on the top right corner, the blinds on the top left.
        var size = fonts.Px(30);
        if (seat == hand.Button)
        {
            DrawMarker(batch, new Rectangle(box.Right - size * 2 / 3, box.Y - size / 2, size, size), "D", Color.White);
        }

        if (seat == hand.SmallBlindSeat || seat == hand.BigBlindSeat)
        {
            DrawMarker(batch, new Rectangle(box.X - size / 3, box.Y - size / 2, size, size), seat == hand.BigBlindSeat ? "BB" : "SB", BlindMarker);
        }
    }

    private void DrawMarker(SpriteBatch batch, Rectangle bounds, string text, Color color)
    {
        batch.Draw(_circle!, bounds, color);
        batch.DrawText(context.Fonts.Sized(bounds.Height * (text.Length > 1 ? 0.5f : 0.65f)), text, bounds, Palette.Ink, shadow: false);
    }

    /// <summary>Chips on their way somewhere, for <see cref="Arriving(int)"/> the seat they belong to.</summary>
    private sealed record Flight(ChipFlight Chips, int Seat);

    /// <summary>Flights that go together: the bets into the pot, or the pots to their winners.</summary>
    private sealed record Stage(bool Award, Flight[] Flights)
    {
        public bool IsDone
        {
            get
            {
                foreach (var flight in Flights)
                {
                    if (!flight.Chips.IsDone)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>A pot in the middle, its label and its winners (empty while the hand runs), and how wide it is drawn.</summary>
    private sealed record PotLabel(int Amount, string Label, string Winners, float Width);

    /// <summary>
    /// Where everything goes, in pixels, for one viewport, seat count, bottom seat and font scale: the felt, each
    /// seat's box, centre and bet, the board's cards, the pot (the bottom centre of its chips), and chip sizes.
    /// </summary>
    private sealed record Layout(
        Rectangle Viewport,
        int BottomSeat,
        float Scale,
        Rectangle Felt,
        Rectangle Rim,
        Rectangle[] Boxes,
        Vector2[] Centres,
        Vector2[] Bets,
        Rectangle[] Board,
        Vector2 Pot,
        float ChipWidth,
        float SeatChipWidth);

    /// <summary>A seat's numbers as text, and its stack's chip size, kept until the number changes.</summary>
    private sealed class SeatText
    {
        public int Stack { get; set; } = -1;

        public string StackText { get; set; } = "";

        public float StackChipWidth { get; set; }

        public int Bet { get; set; } = -1;

        public string BetText { get; set; } = "";

        public float BetWidth { get; set; }
    }
}
