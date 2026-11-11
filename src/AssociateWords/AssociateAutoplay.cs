using Jentl.Cards;

namespace AssociateWords;

/// <summary>
/// Plays a level by always taking the best hint, without looking ahead: what the levels' move limits are measured
/// against, and the proof in the tests that every level can be won.
/// </summary>
public static class AssociateAutoplay
{
    /// <summary>The moves it took to win, or null if it got stuck within <paramref name="steps"/>.</summary>
    public static int? Play(AssociateGame game, int steps = 2000)
    {
        ArgumentNullException.ThrowIfNull(game);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var step = 0; step < steps && !game.IsCleared; step++)
        {
            // Going round in circles: the same table again and again means no hint is getting anywhere.
            var state = string.Join("|", game.Table.Piles.Select(p => p.Count + ":" + (p.Top?.Card.ToString() ?? "")));
            seen[state] = seen.GetValueOrDefault(state) + 1;
            if (seen[state] > 4)
            {
                return null;
            }

            var hints = game.Hints(game.Table).ToList();
            var move = seen[state] > 1 && hints.Count > 1 ? hints[Math.Min(seen[state] - 1, hints.Count - 1)] : hints.FirstOrDefault();
            if (move is null || game.Game.Play(move) is null)
            {
                return null;
            }
        }

        return game.IsCleared ? game.MovesUsed : null;
    }
}
