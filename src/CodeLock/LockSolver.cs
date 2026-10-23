namespace CodeLock;

/// <summary>Enumerates codes and picks good guesses.</summary>
public static class LockSolver
{
    // Scoring every pair is quadratic; past this many candidates only this many are weighed as guesses.
    private const int MaxGuessesWeighed = 250;

    // ...and each guess is measured against at most this many of them (an even sample), so a hint stays quick in a browser.
    private const int MaxSecretsMeasured = 1500;

    /// <summary>Every code the rules allow, in counting order.</summary>
    public static IEnumerable<int[]> AllCodes(LockRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var code = new int[rules.Length];
        return Fill(0);

        IEnumerable<int[]> Fill(int position)
        {
            if (position == code.Length)
            {
                yield return [.. code];
                yield break;
            }

            for (var digit = 0; digit < rules.Digits; digit++)
            {
                if (!rules.Repeats && code.AsSpan(0, position).Contains(digit))
                {
                    continue;
                }

                code[position] = digit;
                foreach (var full in Fill(position + 1))
                {
                    yield return full;
                }
            }
        }
    }

    /// <summary>
    /// Of the codes that still fit, the one whose worst answer leaves the fewest codes (minimax, as in
    /// Knuth's Mastermind strategy, but guessing only codes that could be the answer). With many candidates
    /// evenly spread samples of them are weighed.
    /// </summary>
    public static IReadOnlyList<int> BestGuess(IReadOnlyList<int[]> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("No code fits the answers so far.", nameof(candidates));
        }

        if (candidates.Count <= 2)
        {
            return candidates[0];
        }

        var step = Math.Max(1, candidates.Count / MaxGuessesWeighed);
        var secretStep = Math.Max(1, candidates.Count / MaxSecretsMeasured);
        int[]? best = null;
        var bestWorst = int.MaxValue;
        var counts = new Dictionary<Feedback, int>();
        for (var i = 0; i < candidates.Count; i += step)
        {
            var guess = candidates[i];
            counts.Clear();
            var worst = 0;
            for (var j = 0; j < candidates.Count; j += secretStep)
            {
                var feedback = Feedback.Score(candidates[j], guess);
                var count = counts.GetValueOrDefault(feedback) + 1;
                counts[feedback] = count;
                worst = Math.Max(worst, count);
            }

            if (worst < bestWorst)
            {
                (best, bestWorst) = (guess, worst);
            }
        }

        return best!;
    }
}
