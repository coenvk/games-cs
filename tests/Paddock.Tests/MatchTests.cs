using static Paddock.Tests.TestCars;

namespace Paddock.Tests;

public class MatchTests
{
    [Fact]
    public void Decide_IsDeterministic()
    {
        var a = Rounds.Decide(Hatch, Rally, Twisty, Condition.Wet);
        var b = Rounds.Decide(Hatch, Rally, Twisty, Condition.Wet);

        a.Winner.Should().Be(b.Winner);
        a.Margin.Should().Be(b.Margin);
        a.Deciding.Should().Equal(b.Deciding);
    }

    [Fact]
    public void Decide_WinnerIsTheFaster_MarginIsTheGap()
    {
        var round = Rounds.Decide(Hatch, Rally, Twisty, Condition.Dry);

        round.Winner.Should().Be(1);
        round.Margin.Should().BeApproximately(round.Player.Time - round.Opponent.Time, 1e-12);
    }

    [Fact]
    public void Decide_NeverDraws_SameCarGoesToThePlayer()
    {
        var round = Rounds.Decide(Hatch, Hatch, Twisty, Condition.Dry);

        round.Winner.Should().Be(0);
        round.Margin.Should().Be(0);
    }

    [Fact]
    public void Decide_EqualTimes_BetterHandlingWins()
    {
        // Handling can't matter on a straight drag, so a better-handling copy runs the exact same time.
        var round = Rounds.Decide(Hatch, Hatch with { Handling = 70 }, Drag402, Condition.Dry);

        round.Winner.Should().Be(1);
    }

    [Fact]
    public void Deciding_NamesTyresOnGravel()
    {
        var round = Rounds.Decide(Muscle, Rally, Circuit, Condition.Gravel);

        round.Winner.Should().Be(1);
        round.Deciding.Should().Contain(Factor.Tyres).And.HaveCountLessThanOrEqualTo(3);
    }

    [Fact]
    public void Deciding_NamesTopSpeedOrAccelerationOnADrag()
    {
        var round = Rounds.Decide(Muscle, Hatch, Drag402, Condition.Dry);

        round.Winner.Should().Be(0);
        round.Deciding.Should().Contain(f => f == Factor.TopSpeed || f == Factor.Acceleration);
        round.Deciding.Should().NotContain(Factor.Handling);
    }

    [Fact]
    public void Weight_Matters()
    {
        var heavy = Hatch with { WeightKg = 1800 };

        Time(heavy, Twisty, Condition.Dry).Should().BeGreaterThan(Time(Hatch, Twisty, Condition.Dry));
    }

    [Fact]
    public void Play_MostRoundsWins()
    {
        var setup = new MatchSetup(
            [(Drag402, Condition.Dry), (Twisty, Condition.Dry), (Circuit, Condition.Gravel), (Twisty, Condition.Wet), (Drag402, Condition.Wet)],
            [Car("hatch"), Car("hatch"), Car("hatch"), Car("hatch"), Car("hatch")]);
        List<CarInstance> mine = [Car("muscle"), Car("rally"), Car("rally"), Car("hatch"), Car("muscle")];

        var result = Matches.Play(setup, mine, TestRoster);

        result.Rounds.Should().HaveCount(5);
        result.Rounds.Count(r => r.Winner == 0).Should().BeGreaterThanOrEqualTo(3);
        result.PlayerWon.Should().BeTrue();
    }

    [Fact]
    public void Play_UsesUpgrades()
    {
        var setup = new MatchSetup([(Drag402, Condition.Dry)], [Car("hatch")]);
        var tuned = Car("hatch") with { Stage = 3, Tune = new Tune(9, 9, 6) };

        var result = Matches.Play(setup, [tuned], TestRoster);

        result.Rounds[0].Winner.Should().Be(0);
        result.Rounds[0].Margin.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_EqualWins_LargerMarginWins()
    {
        Matches.Compare(2, 1.5, 2, 0.4).Should().BePositive();
        Matches.Compare(2, 0.4, 2, 1.5).Should().BeNegative();
        Matches.Compare(3, 0.1, 2, 9).Should().BePositive();
    }

    [Fact]
    public void Hands_RejectsDuplicatesOverLimitAndRuleBreakers()
    {
        var five = new[] { Car("hatch"), Car("hatch"), Car("rally"), Car("hatch"), Car("hatch") };

        Hands.Fits(five, TestRoster, rqLimit: 125).Should().BeTrue();
        Hands.Fits(five, TestRoster, rqLimit: 124).Should().BeFalse();
        Hands.Fits(five[..4], TestRoster, rqLimit: 500).Should().BeFalse();
        Hands.Fits([five[0], five[0], five[2], five[3], five[4]], TestRoster, rqLimit: 500).Should().BeFalse();
        Hands.Fits(five, TestRoster, 500, model => model.Body == Body.Hatchback).Should().BeFalse();
    }

    [Fact]
    public void Hands_UnknownModel_DoesNotFit() =>
        Hands.Fits([Car("hatch"), Car("hatch"), Car("hatch"), Car("hatch"), Car("gone")], TestRoster, 500).Should().BeFalse();
}
