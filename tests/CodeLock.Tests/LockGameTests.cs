namespace CodeLock.Tests;

public class LockGameTests
{
    [Theory]
    [InlineData("1234", "1234", 4, 0)]
    [InlineData("1234", "4321", 0, 4)]
    [InlineData("1234", "1243", 2, 2)]
    [InlineData("1234", "5678", 0, 0)]
    [InlineData("1123", "1111", 2, 0)]
    [InlineData("1123", "3211", 0, 4)]
    [InlineData("1123", "1321", 2, 2)]
    [InlineData("1000", "0111", 0, 2)]
    public void Score_CountsExactAndMisplacedDigits_EachAtMostOnce(string secret, string guess, int exact, int misplaced) =>
        Feedback.Score(Code(secret), Code(guess)).Should().Be(new Feedback(exact, misplaced));

    [Fact]
    public void Rules_ExplainInvalidTries()
    {
        var rules = LockRules.Normal;

        rules.Problem(Code("123")).Should().Be("The code has 4 digits.");
        rules.Problem(Code("1123")).Should().Be("Every digit in this lock is different.");
        rules.Problem(Code("1234")).Should().BeNull();
        LockRules.Hard.Problem(Code("11223")).Should().BeNull();
        new LockRules(Length: 3, Digits: 6).Problem([1, 2, 7]).Should().Be("Digits run from 0 to 5.");
    }

    [Fact]
    public void ImpossibleRules_AreRejected()
    {
        var act = () => new LockRules(Length: 4, Digits: 3).Validate();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(3, false, 720)]
    [InlineData(4, false, 5040)]
    [InlineData(5, true, 100_000)]
    public void AllCodes_CountsEveryAllowedCode(int length, bool repeats, int count) =>
        LockSolver.AllCodes(new LockRules(length, Repeats: repeats)).Should().HaveCount(count);

    [Fact]
    public void Create_MakesTheSameLockForTheSameSeed_FollowingTheRules()
    {
        var a = LockGame.Create(LockRules.Normal, 42);
        var b = LockGame.Create(LockRules.Normal, 42);

        a.Secret.Should().Equal(b.Secret);
        LockRules.Normal.Problem(a.Secret).Should().BeNull();
        Enumerable.Range(0, 50).Select(s => string.Concat(LockGame.Create(LockRules.Normal, s).Secret)).Distinct().Should().HaveCountGreaterThan(40);
    }

    [Fact]
    public void Trying_GivesFeedback_UntilSolvedOrOutOfTries()
    {
        var game = new LockGame(LockRules.Easy with { Attempts = 3 }, Code("123"));

        game.Try(Code("456")).Should().Be(new Feedback(0, 0));
        game.Try(Code("132")).Should().Be(new Feedback(1, 2));
        game.AttemptsLeft.Should().Be(1);
        game.IsOver.Should().BeFalse();

        game.Try(Code("123")).Exact.Should().Be(3);
        game.IsSolved.Should().BeTrue();
        game.IsOver.Should().BeTrue();
        var more = () => game.Try(Code("123"));
        more.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RunningOutOfTries_EndsTheGameUnsolved()
    {
        var game = new LockGame(LockRules.Easy with { Attempts = 1 }, Code("123"));

        game.Try(Code("124"));

        game.IsOver.Should().BeTrue();
        game.IsSolved.Should().BeFalse();
    }

    [Fact]
    public void InvalidTries_AreRefused_AndCostNothing()
    {
        var game = new LockGame(LockRules.Normal, Code("1234"));

        var act = () => game.Try(Code("1123"));

        act.Should().Throw<ArgumentException>();
        game.Attempts.Should().BeEmpty();
    }

    [Fact]
    public void PossibleCodes_ShrinkWithEveryAnswer_AndAlwaysIncludeTheSecret()
    {
        var game = new LockGame(LockRules.Normal, Code("2718"));

        game.PossibleCodes.Should().Be(5040);
        game.Try(Code("0123"));
        var afterOne = game.PossibleCodes;
        afterOne.Should().BeLessThan(5040);
        game.Try(Code("4567"));
        game.PossibleCodes.Should().BeLessThan(afterOne);
        LockRules.Normal.Problem(game.Hint()).Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FollowingTheHints_CracksANormalLock_WellWithinTenTries(int seed)
    {
        var game = LockGame.Create(LockRules.Normal, seed);

        while (!game.IsOver)
        {
            var hint = game.Hint();
            game.Attempts.Select(a => a.Feedback).Should().Equal(game.Attempts.Select(a => Feedback.Score(hint, a.Code)), "a hint is always consistent with the answers so far");
            game.Try(hint);
        }

        game.IsSolved.Should().BeTrue();
        game.Attempts.Count.Should().BeLessThanOrEqualTo(7);
    }

    [Fact]
    public void Hints_AlsoCrackAHardLock()
    {
        var game = LockGame.Create(LockRules.Hard, 9);

        while (!game.IsOver)
        {
            game.Try(game.Hint());
        }

        game.IsSolved.Should().BeTrue();
    }

    private static int[] Code(string digits) => [.. digits.Select(c => c - '0')];
}
