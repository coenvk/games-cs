using System.Numerics;

namespace Poker.Tests;

public class SeatLayoutTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void SeatPositions_AreDistinct_SeatZeroAtTheBottom(int seats)
    {
        var positions = SeatLayout.Positions(seats);

        positions.Should().HaveCount(seats);
        positions[0].X.Should().BeApproximately(0, 1e-5f);
        positions[0].Y.Should().BeApproximately(1, 1e-5f, "seat 0 sits at the bottom centre (y grows downwards)");
        positions.Should().OnlyContain(p => p.X >= -1 - 1e-5f && p.X <= 1 + 1e-5f && p.Y >= -1 - 1e-5f && p.Y <= 1 + 1e-5f);
        for (var i = 0; i < seats; i++)
        {
            for (var j = i + 1; j < seats; j++)
            {
                Vector2.Distance(positions[i], positions[j]).Should().BeGreaterThan(0.3f, "seats {0} and {1} must not overlap", i, j);
            }
        }
    }

    [Fact]
    public void SeatPositions_GoClockwise_FromTheBottomToTheLeft()
    {
        var positions = SeatLayout.Positions(6);

        positions[1].X.Should().BeNegative("seat 1 is left of seat 0, clockwise as seen on screen");
        positions[^1].X.Should().BePositive();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void SeatPositions_OutsideTwoToNine_AreRefused(int seats)
    {
        var act = () => SeatLayout.Positions(seats);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
