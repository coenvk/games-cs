using Jentl.Cards;
using Jentl.Multiplayer;

namespace Poker.Arena;

/// <summary>
/// Everything about one hand the arena played, enough to replay it. It is the app's record, never sent to a bot:
/// it holds every seat's hole cards.
/// </summary>
/// <param name="Number">The hand number, counting from 1.</param>
/// <param name="Seed">The seed the deck was shuffled from.</param>
/// <param name="SmallBlindSeat">The seat that posted the small blind, as the engine dealt it.</param>
/// <param name="BigBlindSeat">The seat that posted the big blind.</param>
/// <param name="SmallBlindPosted">What the small blind posted; less than the blind when it was short of chips.</param>
/// <param name="BigBlindPosted">What the big blind posted.</param>
/// <param name="Names">The players' names by seat.</param>
/// <param name="StartStacks">Chips per seat before the blinds; 0 for a seat that was out.</param>
/// <param name="Holes">Every seat's hole cards; empty for a seat that was out.</param>
/// <param name="Actions">Everything the players did, in order (the blinds are not actions).</param>
/// <param name="Board">The community cards dealt.</param>
/// <param name="Pots">The main pot and the side pots at the end of the hand.</param>
/// <param name="Won">What each seat won, including chips returned to it.</param>
/// <param name="Shown">The seats that showed their cards at showdown.</param>
/// <param name="Faults">Every decision in the hand that was not the bot's own answer, with the reason.</param>
public sealed record HandRecord(
    int Number,
    int Seed,
    int Button,
    int SmallBlind,
    int BigBlind,
    int SmallBlindSeat,
    int BigBlindSeat,
    int SmallBlindPosted,
    int BigBlindPosted,
    IReadOnlyList<string> Names,
    IReadOnlyList<int> StartStacks,
    IReadOnlyList<IReadOnlyList<Card>> Holes,
    IReadOnlyList<ActionRecord> Actions,
    IReadOnlyList<Card> Board,
    IReadOnlyList<Pot> Pots,
    IReadOnlyDictionary<int, int> Won,
    IReadOnlySet<int> Shown,
    IReadOnlyList<(int Seat, Fault Fault)> Faults);
