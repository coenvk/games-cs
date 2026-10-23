using Sokoban.Model;

namespace Sokoban.Solver.Tests;

public class LevelGeneratorTests
{
    private static readonly GeneratorOptions Options = new(Width: 9, Height: 8, Crates: 3, MinPushes: 6, Attempts: 20);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generate_ProducesSolvableLevelsOfTheRequestedSize(int seed)
    {
        var level = LevelGenerator.Generate(Options, new Random(seed));

        level.Should().NotBeNull();
        level!.CrateStarts.Should().HaveCount(3);
        var result = new Solver(level.Board).Solve(new LevelSession(level));
        result.Status.Should().Be(SolveStatus.Solved);
        result.Pushes.Should().BeGreaterThanOrEqualTo(Options.MinPushes);
    }

    [Fact]
    public void Generate_IsDeterministicForASeed()
    {
        var first = LevelGenerator.Generate(Options, new Random(42));
        var second = LevelGenerator.Generate(Options, new Random(42));

        Xsb.Write(second!).Should().Be(Xsb.Write(first!));
    }

    [Fact]
    public void Generate_ReturnsNullWhenNothingIsHardEnough()
    {
        var impossible = Options with { MinPushes = 10_000, Attempts = 3 };

        LevelGenerator.Generate(impossible, new Random(1)).Should().BeNull();
    }

    [Fact]
    public void Generate_StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        LevelGenerator.Generate(Options, new Random(1), cancellation.Token).Should().BeNull();
    }
}
