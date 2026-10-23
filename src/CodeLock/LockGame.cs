namespace CodeLock;

/// <summary>How a lock is made: how many wheels, which digits, whether digits may repeat, how many tries.</summary>
public sealed record LockRules(int Length = 4, int Digits = 10, bool Repeats = false, int Attempts = 10)
{
    public static LockRules Easy { get; } = new(Length: 3);

    public static LockRules Normal { get; } = new(Length: 4);

    public static LockRules Hard { get; } = new(Length: 5, Repeats: true);

    /// <summary>Throws if these rules cannot make a lock (say, five different digits out of three).</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(Digits, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Digits, 10);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Attempts);
        if (!Repeats && Length > Digits)
        {
            throw new ArgumentException($"A {Length}-wheel lock needs repeats with only {Digits} digits.");
        }
    }

    /// <summary>Why <paramref name="code"/> is not a valid try under these rules, or null if it is.</summary>
    public string? Problem(IReadOnlyList<int> code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Count != Length)
        {
            return $"The code has {Length} digits.";
        }

        if (code.Any(d => d < 0 || d >= Digits))
        {
            return $"Digits run from 0 to {Digits - 1}.";
        }

        return !Repeats && code.Distinct().Count() != code.Count ? "Every digit in this lock is different." : null;
    }
}

/// <summary>What the lock tells you about a try: digits in the right place, and right digits in the wrong place.</summary>
public readonly record struct Feedback(int Exact, int Misplaced)
{
    /// <summary>Scores <paramref name="guess"/> against <paramref name="secret"/>, counting each digit at most as often as the secret has it.</summary>
    public static Feedback Score(IReadOnlyList<int> secret, IReadOnlyList<int> guess)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(guess);
        Span<int> inSecret = stackalloc int[10];
        Span<int> inGuess = stackalloc int[10];
        var exact = 0;
        for (var i = 0; i < secret.Count; i++)
        {
            if (secret[i] == guess[i])
            {
                exact++;
            }
            else
            {
                inSecret[secret[i]]++;
                inGuess[guess[i]]++;
            }
        }

        var misplaced = 0;
        for (var d = 0; d < 10; d++)
        {
            misplaced += Math.Min(inSecret[d], inGuess[d]);
        }

        return new Feedback(exact, misplaced);
    }

    public override string ToString() => $"{Exact} exact, {Misplaced} misplaced";
}

/// <summary>A try and what the lock said about it.</summary>
public sealed record Attempt(IReadOnlyList<int> Code, Feedback Feedback);

/// <summary>
/// One lock to crack: a secret code, the tries so far, and the codes that still fit everything the lock has
/// said (for hints). Generated from a seed, so a lock number always makes the same lock.
/// </summary>
public sealed class LockGame
{
    private readonly int[] _secret;
    private readonly List<Attempt> _attempts = [];
    private List<int[]> _candidates;

    public LockGame(LockRules rules, IReadOnlyList<int> secret)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(secret);
        rules.Validate();
        if (rules.Problem(secret) is { } problem)
        {
            throw new ArgumentException(problem, nameof(secret));
        }

        Rules = rules;
        _secret = [.. secret];
        _candidates = [.. LockSolver.AllCodes(rules)];
    }

    public LockRules Rules { get; }

    public IReadOnlyList<Attempt> Attempts => _attempts;

    public int AttemptsLeft => Rules.Attempts - _attempts.Count;

    public bool IsSolved => _attempts.Count > 0 && _attempts[^1].Feedback.Exact == Rules.Length;

    /// <summary>Solved, or out of tries.</summary>
    public bool IsOver => IsSolved || AttemptsLeft == 0;

    /// <summary>The code; only meant to be shown once the game is over.</summary>
    public IReadOnlyList<int> Secret => _secret;

    /// <summary>How many codes still fit every answer so far.</summary>
    public int PossibleCodes => _candidates.Count;

    /// <summary>A new lock with a random code from <paramref name="seed"/>.</summary>
    public static LockGame Create(LockRules rules, int seed)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        var random = new Random(seed);
        var digits = Enumerable.Range(0, rules.Digits).ToList();
        var secret = new int[rules.Length];
#pragma warning disable CA5394 // A puzzle, not security relevant.
        for (var i = 0; i < secret.Length; i++)
        {
            var pick = random.Next(digits.Count);
            secret[i] = digits[pick];
            if (!rules.Repeats)
            {
                digits.RemoveAt(pick);
            }
        }
#pragma warning restore CA5394

        return new LockGame(rules, secret);
    }

    /// <summary>Tries a code. Throws if the game is over or the code breaks the rules (see <see cref="LockRules.Problem"/>).</summary>
    public Feedback Try(IReadOnlyList<int> code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (IsOver)
        {
            throw new InvalidOperationException("This lock is done with.");
        }

        if (Rules.Problem(code) is { } problem)
        {
            throw new ArgumentException(problem, nameof(code));
        }

        var feedback = Feedback.Score(_secret, code);
        _attempts.Add(new Attempt([.. code], feedback));
        _candidates = [.. _candidates.Where(c => Feedback.Score(c, code) == feedback)];
        return feedback;
    }

    /// <summary>A good next try: one that fits every answer so far and splits the remaining codes best.</summary>
    public IReadOnlyList<int> Hint() => LockSolver.BestGuess(_candidates);
}
