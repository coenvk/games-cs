using Jentl.Cards;
using Poker;
using static Games.UI.Tr;

namespace Games.Poker;

/// <summary>
/// What a Hold'em table says about its hand: a line in the log for each move, street and win, a label under each seat
/// (its last move, its blind, or its hand at a showdown), the seats that showed their cards, and the chips' sounds.
/// </summary>
internal sealed class HoldemLog(GameContext context)
{
    private const int Kept = 20;

    private readonly List<string> _lines = [];
    private readonly Dictionary<int, string> _lastAction = [];
    private readonly HashSet<int> _shown = [];

    /// <summary>The log, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Each seat's label: its last move, its blind, or its hand once shown.</summary>
    public IReadOnlyDictionary<int, string> LastAction => _lastAction;

    /// <summary>Whether <paramref name="seat"/> showed its cards at the showdown.</summary>
    public bool Showed(int seat) => _shown.Contains(seat);

    /// <summary>A new hand: the labels and shown cards go; the log goes on.</summary>
    public void NewHand()
    {
        _lastAction.Clear();
        _shown.Clear();
    }

    public void Say(string message)
    {
        _lines.Add(message);
        if (_lines.Count > Kept)
        {
            _lines.RemoveAt(0);
        }
    }

    /// <summary>Tells the log what happened, with the players' <paramref name="names"/> by seat.</summary>
    public void Show(IReadOnlyList<PokerEvent> events, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(names);
        foreach (var e in events)
        {
            Describe(e, names);
        }
    }

    private void Describe(PokerEvent e, IReadOnlyList<string> names)
    {
        switch (e)
        {
            case BlindPosted b:
                _lastAction[b.Seat] = b.Big ? T("Big blind") : T("Small blind");
                break;
            case Acted a:
                if (a.Amount > 0)
                {
                    context.Sounds.Play(Sound.Chips);
                }

                var label = HoldemText.Action(a);
                _lastAction[a.Seat] = label;
                Say($"{names[a.Seat]}: {label}");
                break;
            case StreetDealt d:
                HoldemText.NewStreet(_lastAction);
                Say(T("{0}: {1}", HoldemText.StreetName(d.Street), Card.Symbols(d.Cards)));
                break;
            case HandShown s:
                _shown.Add(s.Seat);
                _lastAction[s.Seat] = HoldemText.Describe(s.Value);
                break;
            case PotWon w:
                context.Sounds.Play(Sound.Chips);
                Say(w.Value is { } value
                    ? T("{0} wins {1}: {2}", names[w.Seat], w.Amount, HoldemText.Describe(value))
                    : T("{0} takes {1}", names[w.Seat], w.Amount));
                break;
        }
    }
}
