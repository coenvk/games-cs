using Jentl.Core.Input;
using Jentl.Core.UI;
using Games.Input;
using Microsoft.Xna.Framework.Input;
using Poker;
using static Games.UI.Tr;

namespace Games.Poker;

/// <summary>
/// A person's controls on their turn at a Hold'em table: Fold, Check or Call, a raise amount stepped by the big blind
/// with - and +, Bet or Raise, and All in. On the keyboard Enter checks or calls, Up raises, Left and Right change the
/// amount and F folds. The moves go to the act callback; what a move does is up to the table.
/// </summary>
internal sealed class HoldemControls
{
    private int _raiseTo;

    /// <summary>A new hand: the raise amount starts from the smallest raise.</summary>
    public void Reset(HoldemHand hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        _raiseTo = hand.MinRaiseTo;
    }

    /// <summary>The buttons for <paramref name="seat"/>'s turn in <paramref name="hand"/>, followed by <paramref name="extra"/>.</summary>
    public Button[] Buttons(HoldemHand hand, int seat, Action<PokerAction> act, params Button[] extra)
    {
        ArgumentNullException.ThrowIfNull(hand);
        _raiseTo = Math.Clamp(_raiseTo, Math.Min(hand.MinRaiseTo, hand.MaxRaiseTo(seat)), hand.MaxRaiseTo(seat));
        var toCall = hand.ToCall(seat);
        var max = hand.MaxRaiseTo(seat);
        var moves = hand.LegalMoves(seat);
        var canRaise = moves.Any(m => m is RaiseTo);
        var buttons = new List<Button>();
        if (toCall > 0)
        {
            buttons.Add(new Button(T("Fold"), () => act(Fold.Instance)));
        }

        buttons.Add(toCall == 0 ? new Button(T("Check"), () => act(Check.Instance), Highlight: true)
            : moves.Contains(CallBet.Instance) ? new Button(T("Call {0}", toCall), () => act(CallBet.Instance), Highlight: true)
            : new Button(T("Call all in {0}", toCall), () => act(AllIn.Instance), Highlight: true));
        if (canRaise)
        {
            buttons.Add(new Button("-", () => Step(hand, seat, -1)));
            buttons.Add(new Button(hand.CurrentBet == 0 ? T("Bet {0}", _raiseTo) : T("Raise to {0}", _raiseTo), () => Raise(hand, seat, act)));
            buttons.Add(new Button("+", () => Step(hand, seat, 1)));
        }

        if (moves.Contains(AllIn.Instance) && max > toCall + hand.Bet(seat))
        {
            buttons.Add(new Button(T("All in"), () => act(AllIn.Instance)));
        }

        buttons.AddRange(extra);
        return [.. buttons];
    }

    /// <summary>Plays the keys pressed this frame for <paramref name="seat"/>'s turn.</summary>
    public void Update(ActionMap<GameAction> actions, InputState input, HoldemHand hand, int seat, Action<PokerAction> act)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hand);
        if (actions.WasPressed(input, GameAction.Confirm))
        {
            act(hand.ToCall(seat) > 0 ? CallBet.Instance : Check.Instance);
        }
        else if (actions.WasPressed(input, GameAction.Up))
        {
            Raise(hand, seat, act);
        }
        else if (actions.WasPressed(input, GameAction.Left))
        {
            Step(hand, seat, -1);
        }
        else if (actions.WasPressed(input, GameAction.Right))
        {
            Step(hand, seat, 1);
        }
        else if (input.WasPressed(Keys.F) && hand.ToCall(seat) > 0)
        {
            act(Fold.Instance);
        }
    }

    private void Step(HoldemHand hand, int seat, int direction)
    {
        // A short stack may have less than a full raise: then all it has is the only amount.
        var max = hand.MaxRaiseTo(seat);
        _raiseTo = Math.Clamp(_raiseTo + direction * hand.BigBlind, Math.Min(hand.MinRaiseTo, max), max);
    }

    private void Raise(HoldemHand hand, int seat, Action<PokerAction> act)
    {
        if (!hand.LegalMoves(seat).Any(m => m is RaiseTo))
        {
            return;
        }

        act(_raiseTo >= hand.MaxRaiseTo(seat) ? AllIn.Instance : new RaiseTo(_raiseTo));
    }
}
