using Sokoban.Model;
using Sokoban.Solver;

namespace Sokoban.Desktop;

/// <summary>
/// Runs the solver on a background thread so the game keeps rendering while it thinks.
/// Results are tagged with the position they were computed for, so a result for a
/// position the player has since moved away from is dropped.
/// </summary>
internal sealed class HintRunner : IDisposable
{
    private static readonly SolverOptions Options = new(MaxNodes: 1_000_000, Weight: 3, MaxTime: TimeSpan.FromSeconds(3));

    private readonly Solver.Solver _solver;
    private CancellationTokenSource? _cancellation;
    private Task<SolveResult>? _task;
    private int _version;

    public HintRunner(Solver.Solver solver) => _solver = solver;

    public bool IsThinking => _task is { IsCompleted: false };

    /// <param name="version">Identifies the position; pass the same counter to <see cref="TakeResult"/>.</param>
    public void Start(LevelSession session, int version)
    {
        Cancel();
        var player = session.Player;
        var crates = session.Crates.ToArray();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _version = version;
        _task = Task.Run(() => _solver.Solve(player, crates, Options, token), token);
    }

    /// <summary>The finished result, if there is one for <paramref name="currentVersion"/>; stale results are discarded.</summary>
    public SolveResult? TakeResult(int currentVersion)
    {
        if (_task is not { IsCompleted: true } task)
        {
            return null;
        }

        _task = null;
        if (_version != currentVersion || !task.IsCompletedSuccessfully)
        {
            return null;
        }

        return task.Result;
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _task = null;
    }

    public void Dispose() => Cancel();
}
