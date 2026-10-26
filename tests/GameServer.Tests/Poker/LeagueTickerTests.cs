using GameServer.Poker;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameServer.Tests.Poker;

/// <summary>The ticker that keeps the league going: a tick that throws is no reason to stop the server.</summary>
public sealed class LeagueTickerTests
{
    [Fact]
    public async Task ATickThatThrows_DoesNotStopTheTicking()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ticks = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Tick()
        {
            switch (Interlocked.Increment(ref ticks))
            {
                case 1:
                    throw new InvalidOperationException("a league that broke once");
                case 3:
                    third.TrySetResult();
                    break;
            }

            return Task.CompletedTask;
        }

        var ticking = LeagueTicker.KeepTickingAsync(Tick, TimeSpan.FromMilliseconds(10), NullLogger.Instance, stop.Token);

        await Task.WhenAny(third.Task, ticking).WaitAsync(stop.Token);
        ticking.IsCompleted.Should().BeFalse("the ticker goes on after a tick that threw");
        third.Task.IsCompleted.Should().BeTrue();
        await stop.CancelAsync();
        var stopping = () => ticking;
        await stopping.Should().ThrowAsync<OperationCanceledException>("cancelling is how the host stops it");
    }
}
