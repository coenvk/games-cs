namespace Sokoban.Solver;

/// <param name="MaxNodes">Positions to expand before giving up with <see cref="SolveStatus.LimitReached"/>.</param>
/// <param name="Weight">
/// Heuristic weight for weighted A*. 1 keeps solutions push-optimal under the heuristic; higher values
/// search far fewer positions at the cost of longer solutions, which is the right trade for hints.
/// </param>
/// <param name="MaxTime">Wall-clock budget; also ends the search with <see cref="SolveStatus.LimitReached"/>.</param>
public sealed record SolverOptions(int MaxNodes = 300_000, int Weight = 1, TimeSpan? MaxTime = null)
{
    public static SolverOptions Default { get; } = new();

    internal TimeSpan TimeBudget => MaxTime ?? TimeSpan.MaxValue;
}
