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
    private readonly Queue<Stage> _stages = new();
    private Stage? _stage;
    private Texture2D? _circle;
    private HoldemHand? _hand;

    // The pots in the middle as they stood when the bets were last gathered.
    private IReadOnlyList<Pot> _pots = [];
    private bool _awarding;
    private Spots? _spots;

    /// <summary>The seat drawn at the bottom of the table (the one whose cards the viewer holds).</summary>
    public int BottomSeat { get; set; }

    /// <summary>True while chips are on their way to the pot or to a winner.</summary>
    public bool IsAnimating => _stage is not null || _stages.Count > 0;

    /// <summary>
    /// The seats' centres on the felt's ellipse in pixels: seat 0 at the bottom centre, then clockwise. See
    /// <see cref="SeatLayout.Positions"/>.
    /// </summary>
    public static Vector2[] SeatPositions(int seats, Rectangle felt) =>
        [.. SeatLayout.Positions(seats).Select(p => new Vector2(felt.Center.X + (p.X * felt.Width / 2f), felt.Center.Y + (p.Y * felt.Height / 2f)))];

    /// <summary>Follows the hand's events and moves the chips in flight on by <paramref name="elapsed"/>.</summary>
    public void Update(TimeSpan elapsed, IReadOnlyList<PokerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var won = new List<Flight>();
        foreach (var e in events)
        {
            switch (e)
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
                    if (_spots is { } spots)
                    {
                        won.Add(new Flight(new ChipFlight(w.Amount, spots.Pot, spots.Seats[w.Seat], AwardTime), w.Seat));
                    }

                    break;
            }
        }

        if (won.Count > 0)
        {
            _stages.Enqueue(new Stage(Award: true, [.. won]));
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

        // A new hand: whatever was still flying from the last one has landed.
        if (!ReferenceEquals(hand, _hand))
        {
            _hand = hand;
            _pots = [];
            _awarding = false;
            _stages.Clear();
            _stage = null;
        }

        // The pots shown are the engine's once the street's bets are in; an uncalled bet (a one-seat "pot") goes back.
        if (!hand.IsOver && !IsAnimating && Enumerable.Range(0, hand.Seats).All(s => hand.Bet(s) == 0))
        {
            _pots = [.. hand.Pots.Where(p => p.Seats.Count > 1)];
        }

        var fonts = context.Fonts;
        var seats = hand.Seats;
        var crowded = seats > 6;
        var seatWidth = Math.Min(fonts.Px(crowded ? 160 : 190), viewport.Width / (crowded ? 4 : 3));
        var seatHeight = Math.Min(fonts.Px(crowded ? 130 : 150), viewport.Height / 3);
        var felt = new Rectangle(viewport.X + seatWidth / 3, viewport.Y + seatHeight / 2, viewport.Width - seatWidth * 2 / 3, viewport.Height - seatHeight);
        var rim = felt;
        rim.Inflate(fonts.Px(8), fonts.Px(8));
        batch.Draw(_circle, rim, Rim);
        batch.Draw(_circle, felt, Felt);

        // The seats round the edge, turned so that the bottom seat is at the bottom.
        var around = SeatPositions(seats, felt);
        var boxes = new Rectangle[seats];
        var bets = new Vector2[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var centre = around[(seat - BottomSeat + seats) % seats];
            var box = new Rectangle((int)centre.X - seatWidth / 2, (int)centre.Y - seatHeight / 2, seatWidth, seatHeight);
            box.X = Math.Clamp(box.X, viewport.X, viewport.Right - seatWidth);
            box.Y = Math.Clamp(box.Y, viewport.Y, viewport.Bottom - seatHeight);
            boxes[seat] = box;
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
        for (var i = 0; i < 5; i++)
        {
            var bounds = new Rectangle(felt.Center.X - boardWidth / 2 + i * (cardWidth + gap), boardTop, cardWidth, cardHeight);
            if (i < hand.Board.Count)
            {
                context.Cards.Draw(batch, new PlacedCard(hand.Board[i], FaceUp: true), bounds);
            }
            else
            {
                context.Cards.DrawSlot(batch, bounds);
            }
        }

        var potBottom = boardTop - gap - fonts.Small.LineHeight * 2;
        _spots = new Spots([.. boxes.Select(b => b.Center.ToVector2())], bets, new Vector2(felt.Center.X, potBottom));
        DrawPots(batch, hand, names, felt.Center.X, potBottom, chipWidth);

        for (var seat = 0; seat < seats; seat++)
        {
            DrawSeat(batch, hand, seat, boxes[seat], names[seat], faceUp(seat), lastAction.GetValueOrDefault(seat));

            // A hand won by a fold ends with the street's bets still out; they have gone to the winner with the pot.
            if (!hand.IsOver && hand.Bet(seat) > 0)
            {
                DrawChips(batch, bets[seat], hand.Bet(seat), chipWidth, Palette.Accent);
            }
        }

        foreach (var flight in _stage?.Flights ?? [])
        {
            ChipRenderer.DrawAmount(batch, flight.Chips.Position, flight.Chips.Amount, chipWidth, MaxPerStack);
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
        if (_spots is { } spots && _bets.Any(b => b.Value > 0))
        {
            _stages.Enqueue(new Stage(Award: false, [.. _bets.Where(b => b.Value > 0).Select(b => new Flight(new ChipFlight(b.Value, spots.Bets[b.Key], spots.Pot, GatherTime), b.Key))]));
        }

        _bets.Clear();
    }

    private void Next()
    {
        _stage = _stages.TryDequeue(out var next) ? next : null;
        _awarding |= _stage?.Award == true;
    }

    /// <summary>Chips still flying to <paramref name="seat"/> as winnings: its stack shows them once they land.</summary>
    private int Arriving(int seat) =>
        (_stage is null ? _stages : _stages.Prepend(_stage))
            .Where(s => s.Award)
            .SelectMany(s => s.Flights)
            .Where(f => f.Seat == seat && !f.Chips.IsDone)
            .Sum(f => f.Chips.Amount);

    /// <summary>
    /// The main pot and the side pots side by side above the board; once the hand is won, each contested pot with
    /// its winners instead (the chips have flown to them).
    /// </summary>
    private void DrawPots(SpriteBatch batch, HoldemHand hand, IReadOnlyList<string> names, int middle, float bottom, float chipWidth)
    {
        var fonts = context.Fonts;
        var results = hand.IsOver && _awarding
            ? hand.PotResults.Where(p => p.Seats.Count > 1).Select(p => (p.Amount, Winners: string.Join(", ", p.Winners.Select(w => names[w.Seat])))).ToArray()
            : [.. _pots.Select(p => (p.Amount, Winners: ""))];
        if (results.Length == 0)
        {
            return;
        }

        var labels = results.Select((r, i) => i == 0 ? T("Pot {0}", r.Amount) : T("Side pot {0}", r.Amount)).ToArray();
        var gap = fonts.Px(24);
        var widths = results.Select((r, i) => Math.Max(Math.Max(fonts.Small.MeasureString(labels[i]).X, fonts.Small.MeasureString(r.Winners).X), ChipRenderer.Width(r.Amount, chipWidth, MaxPerStack))).ToArray();
        var x = middle - (widths.Sum() + gap * (widths.Length - 1)) / 2;
        for (var i = 0; i < results.Length; i++)
        {
            var centre = x + widths[i] / 2;
            if (results[i].Winners.Length == 0)
            {
                ChipRenderer.DrawAmount(batch, new Vector2(centre, bottom), results[i].Amount, chipWidth, MaxPerStack);
            }
            else
            {
                batch.DrawText(fonts.Small, results[i].Winners, new Vector2(centre, bottom - fonts.Small.LineHeight), Palette.Text, Align.Center);
            }

            batch.DrawText(fonts.Small, labels[i], new Vector2(centre, bottom + fonts.Px(2)), Palette.Accent, Align.Center);
            x += widths[i] + gap;
        }
    }

    /// <summary>An amount as chips with its number beside them.</summary>
    private void DrawChips(SpriteBatch batch, Vector2 bottomCentre, int amount, float chipWidth, Color color)
    {
        var font = context.Fonts.Small;
        ChipRenderer.DrawAmount(batch, bottomCentre, amount, chipWidth, MaxPerStack);
        var right = bottomCentre.X + ChipRenderer.Width(amount, chipWidth, MaxPerStack) / 2 + context.Fonts.Px(4);
        batch.DrawText(font, amount.ToString(CultureInfo.InvariantCulture), new Vector2(right, bottomCentre.Y - font.LineHeight * 0.8f), color, Align.Left);
    }

    private void DrawSeat(SpriteBatch batch, HoldemHand hand, int seat, Rectangle box, string name, bool faceUp, string? lastAction)
    {
        var fonts = context.Fonts;
        var font = fonts.Small;
        var current = !hand.IsOver && hand.CurrentSeat == seat;
        var isOut = !hand.InHand(seat);
        batch.FillRectangle(box, current ? Palette.Accent : Palette.Panel);
        var inner = box;
        inner.Inflate(-3, -3);
        batch.FillRectangle(inner, isOut || hand.HasFolded(seat) ? Palette.Background : Palette.Panel);

        // The name and the stack's number on top, the hole cards under them, the stack's chips, then the last action.
        var pad = fonts.Px(6);
        var stack = hand.Stack(seat) - Arriving(seat);
        var top = new Rectangle(inner.X + pad, inner.Y + fonts.Px(2), inner.Width - 2 * pad, (int)font.LineHeight);
        batch.DrawText(font, name, new Vector2(top.X, top.Y), current ? Palette.Accent : Palette.Text, Align.Left, shadow: false);
        batch.DrawText(font, stack.ToString(CultureInfo.InvariantCulture), new Vector2(top.Right, top.Y), Palette.Muted, Align.Right, shadow: false);

        var statusTop = inner.Bottom - (int)font.LineHeight - fonts.Px(2);
        var chipWidth = (float)fonts.Px(16);
        var chipsWidth = ChipRenderer.Width(stack, chipWidth, MaxPerStack);
        if (chipsWidth > inner.Width - 2 * pad)
        {
            chipWidth *= (inner.Width - 2 * pad) / chipsWidth;
        }

        var chipsBottom = statusTop - fonts.Px(2);
        var cardTop = top.Bottom + fonts.Px(4);
        var cardHeight = Math.Max(0, (int)(chipsBottom - chipWidth * (0.45f + 0.2f * (MaxPerStack - 1)) - fonts.Px(4)) - cardTop);
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
            ChipRenderer.DrawAmount(batch, new Vector2(inner.Center.X, chipsBottom), stack, chipWidth, MaxPerStack);
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

    /// <summary>Chips on their way somewhere, for <see cref="Arriving"/> the seat they belong to.</summary>
    private sealed record Flight(ChipFlight Chips, int Seat);

    /// <summary>Flights that go together: the bets into the pot, or the pots to their winners.</summary>
    private sealed record Stage(bool Award, Flight[] Flights)
    {
        public bool IsDone => Flights.All(f => f.Chips.IsDone);
    }

    /// <summary>Where chips go, in pixels, as last drawn: each seat's box, each seat's bet, and the pot.</summary>
    private sealed record Spots(Vector2[] Seats, Vector2[] Bets, Vector2 Pot);
}
