using Jentl.Multiplayer;

namespace Uno;

/// <summary>Plays any legal move at random, and forgets to call Uno one time in three.</summary>
public sealed class RandomUnoBot : IBot<UnoGame, UnoMove>
{
    public UnoMove Choose(UnoGame game, int seat, Random random)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(random);
#pragma warning disable CA5394 // Game play, not security relevant.
        var moves = game.LegalMoves(seat);
        var move = moves[random.Next(moves.Count)];
        return move is PlayCard { CallUno: true } play && random.Next(3) == 0 ? play with { CallUno = false } : move;
#pragma warning restore CA5394
    }
}

/// <summary>
/// A sensible player: matches colour before number, keeps wilds (and a Wild Draw Four above all) for when
/// nothing else fits, attacks with Skip, Reverse and Draw Two when the next player is close to winning,
/// otherwise sheds high cards first, picks the colour it holds most of, and always calls Uno.
/// </summary>
public sealed class SmartUnoBot : IBot<UnoGame, UnoMove>
{
    public UnoMove Choose(UnoGame game, int seat, Random random)
    {
        ArgumentNullException.ThrowIfNull(game);
        var moves = game.LegalMoves(seat);
        var plays = moves.OfType<PlayCard>().ToArray();
        if (plays.Length == 0)
        {
            return moves[0]; // draw, or pass after drawing
        }

        var hand = game.Hand(seat);
        var next = game.Hand(NextSeat(game, seat)).Count;
        var favourite = hand.Where(c => !c.IsWild).GroupBy(c => c.Color!.Value).OrderByDescending(g => g.Count()).Select(g => (UnoColor?)g.Key).FirstOrDefault()
            ?? game.Color;

        var best = plays
            .Where(p => !p.Card.IsWild || p.Chosen == favourite)
            .OrderByDescending(p => Score(p.Card, game.Color, next))
            .First();
        return best with { CallUno = hand.Count == 2 };
    }

    private static int Score(UnoCard card, UnoColor color, int nextHand) => card.Value switch
    {
        UnoValue.WildDrawFour => nextHand <= 2 ? 60 : -100,
        UnoValue.Wild => -50,
        UnoValue.Skip or UnoValue.Reverse or UnoValue.DrawTwo => nextHand <= 2 ? 80 : 15,
        _ => (int)card.Value + (card.Color == color ? 20 : 0),
    } + (card.Color == color ? 5 : 0);

    private static int NextSeat(UnoGame game, int seat) => ((seat + game.Direction) % game.Players + game.Players) % game.Players;
}

/// <summary>
/// Several rounds of Uno: each round's winner scores the cards left in the other hands, the deal moves
/// one seat on, and the first to <see cref="Target"/> points wins the match.
/// </summary>
public sealed class UnoMatch
{
    private readonly int[] _scores;
    private readonly int _seed;

    public UnoMatch(int players, int seed, int target = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        Players = players;
        Target = target;
        _seed = seed;
        _scores = new int[players];
        Round = new UnoGame(players, seed, dealer: players - 1);
    }

    public int Players { get; }

    public int Target { get; }

    public UnoGame Round { get; private set; }

    public int RoundNumber { get; private set; } = 1;

    public IReadOnlyList<int> Scores => _scores;

    /// <summary>Whoever has reached the target, once the current round is over.</summary>
    public int? Champion => Round.IsOver ? Enumerable.Range(0, Players).Where(s => _scores[s] >= Target).Cast<int?>().FirstOrDefault() : null;

    private bool Scored { get; set; }

    /// <summary>Adds the finished round's points to its winner (once). Returns them.</summary>
    public int ScoreRound()
    {
        if (Round.Winner is not { } winner)
        {
            throw new InvalidOperationException("The round is still going.");
        }

        var points = Enumerable.Range(0, Players).Where(s => s != winner).Sum(Round.HandPoints);
        if (!Scored)
        {
            _scores[winner] += points;
            Scored = true;
        }

        return points;
    }

    /// <summary>Deals the next round, the deal passing one seat on.</summary>
    public UnoGame NextRound()
    {
        if (!Round.IsOver)
        {
            throw new InvalidOperationException("The round is still going.");
        }

        ScoreRound();
        RoundNumber++;
        Scored = false;
        Round = new UnoGame(Players, unchecked(_seed + RoundNumber * 7919), dealer: (Players - 1 + RoundNumber - 1) % Players);
        return Round;
    }
}
