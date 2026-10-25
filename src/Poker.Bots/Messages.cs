using System.Text.Json.Serialization;

namespace PokerBots;

// Cards are two-character codes: a rank from "23456789TJQKA" and a suit from "shdc" (spades, hearts, diamonds, clubs),
// so "As" is the ace of spades and "Td" the ten of diamonds. Seats are numbered from 0. Chip amounts are whole numbers.

/// <summary>Something sent over the wire. On the wire it is a JSON object whose <c>"type"</c> names the message.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(MatchStart), "match-start")]
[JsonDerivedType(typeof(HandStart), "hand-start")]
[JsonDerivedType(typeof(Decision), "decision")]
[JsonDerivedType(typeof(HandEnd), "hand-end")]
[JsonDerivedType(typeof(MatchEnd), "match-end")]
[JsonDerivedType(typeof(BotAction), "action")]
public abstract record Message;

/// <summary>
/// The greeting, sent in both directions. The app sends <c>{ protocol }</c> first; the bot answers with its
/// <paramref name="Name"/> as well. <see cref="BotRunner"/> does this for you.
/// </summary>
/// <param name="Protocol">The protocol version, <see cref="PokerBots.Protocol.Version"/>.</param>
/// <param name="Name">The bot's name. Left out by the app, filled in by the bot.</param>
public sealed record Hello(int Protocol, string? Name = null) : Message;

/// <summary>App to bot: the match is starting.</summary>
/// <param name="Seat">Your seat number.</param>
/// <param name="Seats">How many seats the table has.</param>
/// <param name="Format"><c>"sit-and-go"</c> (play until one player has all the chips, blinds going up) or
/// <c>"fixed-stacks"</c> (every hand starts from the starting stack, so only the single hand counts).</param>
/// <param name="StartingStack">The chips everyone starts with.</param>
/// <param name="SmallBlind">The small blind at the start of the match.</param>
/// <param name="BigBlind">The big blind at the start of the match.</param>
/// <param name="TimeLimitMs">How long you get to answer each decision, in milliseconds.</param>
/// <param name="Seed">The seed the match's shuffling was derived from, so a match can be replayed.</param>
public sealed record MatchStart(
    int Seat,
    int Seats,
    string Format,
    int StartingStack,
    int SmallBlind,
    int BigBlind,
    int TimeLimitMs,
    long Seed) : Message;

/// <summary>App to bot: a new hand is starting.</summary>
/// <param name="Hand">The hand number, counting from 1.</param>
/// <param name="Button">The seat holding the dealer button.</param>
/// <param name="SmallBlind">The small blind for this hand.</param>
/// <param name="BigBlind">The big blind for this hand.</param>
/// <param name="Seats">Every seat with its stack before the blinds are posted.</param>
public sealed record HandStart(
    int Hand,
    int Button,
    int SmallBlind,
    int BigBlind,
    IReadOnlyList<HandSeat> Seats) : Message;

/// <summary>A seat at the start of a hand.</summary>
/// <param name="Seat">The seat number.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Stack">The chips behind, before the blinds.</param>
/// <param name="State"><c>"active"</c> or <c>"out"</c> (busted, takes no part in the hand).</param>
public sealed record HandSeat(int Seat, string Name, int Stack, string State);

/// <summary>
/// App to bot: it is your turn, answer with a <see cref="BotAction"/>. Everything you need to decide is in here, so a
/// bot does not have to remember anything between decisions.
/// </summary>
/// <param name="Id">Counts up through the match. Your answer must carry the same id, so a late answer to an earlier
/// decision is never mistaken for the current one. <see cref="BotRunner"/> does this for you.</param>
/// <param name="Hand">The hand number.</param>
/// <param name="Street"><c>"preflop"</c>, <c>"flop"</c>, <c>"turn"</c> or <c>"river"</c>.</param>
/// <param name="You">Your seat number.</param>
/// <param name="Hole">Your two hole cards.</param>
/// <param name="Board">The community cards so far: none, three, four or five.</param>
/// <param name="Button">The seat holding the dealer button.</param>
/// <param name="SmallBlindSeat">The seat that posted the small blind.</param>
/// <param name="BigBlindSeat">The seat that posted the big blind.</param>
/// <param name="SmallBlind">The small blind.</param>
/// <param name="BigBlind">The big blind.</param>
/// <param name="Level">The blind level, counting from 1; it goes up as a sit-and-go goes on.</param>
/// <param name="Seats">Every seat with its stack, its bet this street and its state.</param>
/// <param name="Pots">The main pot and any side pots, chips from earlier streets included.</param>
/// <param name="ToCall">The chips you must add to stay in. Zero when you can check.</param>
/// <param name="Legal">What you may do.</param>
/// <param name="Actions">Everything that happened this hand so far, oldest first.</param>
/// <param name="TimeLimitMs">How long you have to answer, in milliseconds.</param>
public sealed record Decision(
    int Id,
    int Hand,
    string Street,
    int You,
    IReadOnlyList<string> Hole,
    IReadOnlyList<string> Board,
    int Button,
    int SmallBlindSeat,
    int BigBlindSeat,
    int SmallBlind,
    int BigBlind,
    int Level,
    IReadOnlyList<TableSeat> Seats,
    IReadOnlyList<Pot> Pots,
    int ToCall,
    LegalActions Legal,
    IReadOnlyList<PlayedAction> Actions,
    int TimeLimitMs) : Message;

/// <summary>A seat during a hand.</summary>
/// <param name="Seat">The seat number.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Stack">The chips behind.</param>
/// <param name="Bet">The chips this seat has put in on the current street.</param>
/// <param name="State"><c>"active"</c>, <c>"folded"</c>, <c>"all-in"</c> or <c>"out"</c> (busted, not in this hand).</param>
public sealed record TableSeat(int Seat, string Name, int Stack, int Bet, string State);

/// <summary>A pot and who can win it.</summary>
/// <param name="Amount">The chips in the pot.</param>
/// <param name="Seats">The seats that are still in the hand and can win it.</param>
public sealed record Pot(int Amount, IReadOnlyList<int> Seats);

/// <summary>What the player to act may do. Answering with anything else counts as a fold.</summary>
/// <param name="Fold">Always true.</param>
/// <param name="Check">True when there is nothing to call.</param>
/// <param name="Call">The chips a call costs (at most your stack), or null when there is nothing to call.</param>
/// <param name="MinRaiseTo">The smallest legal raise, as the total bet for this street, or null when you cannot raise.</param>
/// <param name="MaxRaiseTo">The largest raise, as the total bet for this street (your whole stack), or null.</param>
/// <param name="AllIn">True when you may push all your chips in.</param>
public sealed record LegalActions(
    bool Fold,
    bool Check,
    int? Call = null,
    int? MinRaiseTo = null,
    int? MaxRaiseTo = null,
    bool AllIn = false);

/// <summary>Something a player did earlier in the hand.</summary>
/// <param name="Street">The street it happened on.</param>
/// <param name="Seat">The seat that acted.</param>
/// <param name="Action">What it did: <c>"fold"</c>, <c>"check"</c>, <c>"call"</c>, <c>"raise"</c> or <c>"all-in"</c>
/// (blinds show up as a <c>"raise"</c> or <c>"call"</c> as posted).</param>
/// <param name="Amount">The chips it added with this action.</param>
/// <param name="Total">Its total bet on this street afterwards.</param>
public sealed record PlayedAction(string Street, int Seat, string Action, int Amount, int Total);

/// <summary>App to bot: the hand is over.</summary>
/// <param name="Hand">The hand number.</param>
/// <param name="Board">The community cards that were dealt.</param>
/// <param name="Actions">Everything that happened this hand, oldest first.</param>
/// <param name="Shown">The hands revealed at showdown. Empty when everyone else folded.</param>
/// <param name="Won">How much each winning seat collected.</param>
/// <param name="Stacks">Every seat's stack after the hand, indexed by seat number.</param>
public sealed record HandEnd(
    int Hand,
    IReadOnlyList<string> Board,
    IReadOnlyList<PlayedAction> Actions,
    IReadOnlyList<ShownHand> Shown,
    IReadOnlyList<Win> Won,
    IReadOnlyList<int> Stacks) : Message;

/// <summary>A hand revealed at showdown.</summary>
/// <param name="Seat">The seat that showed.</param>
/// <param name="Cards">Its two hole cards.</param>
/// <param name="Rank">The best five-card hand's name, such as <c>"flush"</c>.</param>
public sealed record ShownHand(int Seat, IReadOnlyList<string> Cards, string Rank);

/// <summary>Chips collected from the pots.</summary>
/// <param name="Seat">The seat that won.</param>
/// <param name="Amount">The chips it won, over all the pots it took part of.</param>
public sealed record Win(int Seat, int Amount);

/// <summary>App to bot: the match is over.</summary>
/// <param name="Standings">Every seat with its final stack and place; place 1 is the winner.</param>
public sealed record MatchEnd(IReadOnlyList<Standing> Standings) : Message;

/// <summary>A seat's result at the end of the match.</summary>
/// <param name="Seat">The seat number.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Stack">The chips it finished with.</param>
/// <param name="Place">Its finishing place, 1 for the winner.</param>
public sealed record Standing(int Seat, string Name, int Stack, int Place);

/// <summary>
/// Bot to app: your answer to a <see cref="Decision"/>. Use <see cref="Fold"/>, <see cref="Check"/>, <see cref="Call"/>,
/// <see cref="Raise"/> or <see cref="AllIn"/>. Anything that is not legal right now (or an action name the app does not
/// know) is treated as a fold.
/// </summary>
/// <param name="Action"><c>"fold"</c>, <c>"check"</c>, <c>"call"</c>, <c>"raise"</c> or <c>"all-in"</c>.</param>
/// <param name="To">For a raise: the total bet for this street, between <see cref="LegalActions.MinRaiseTo"/> and
/// <see cref="LegalActions.MaxRaiseTo"/>.</param>
/// <param name="Id">The <see cref="Decision.Id"/> this answers. Optional; the app ignores an answer whose id is present
/// but is not the decision being asked. <see cref="BotRunner"/> fills it in.</param>
public sealed record BotAction(string Action, int? To = null, int? Id = null) : Message
{
    /// <summary>Give up the hand.</summary>
    public static BotAction Fold { get; } = new("fold");

    /// <summary>Pass without betting; only legal when <see cref="Decision.ToCall"/> is zero.</summary>
    public static BotAction Check { get; } = new("check");

    /// <summary>Match the current bet.</summary>
    public static BotAction Call { get; } = new("call");

    /// <summary>Push all your chips in.</summary>
    public static BotAction AllIn { get; } = new("all-in");

    /// <summary>Raise so your total bet this street is <paramref name="to"/>.</summary>
    public static BotAction Raise(int to) => new("raise", to);
}
