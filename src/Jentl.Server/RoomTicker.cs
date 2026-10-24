using Jentl.Net;

namespace Jentl.Server;

/// <summary>Lets time pass in every room ten times a second, so bots move and finished rounds move on.</summary>
internal sealed class RoomTicker(RoomServer rooms) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = clock.Elapsed;
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var now = clock.Elapsed;
            rooms.Update(now - last);
            last = now;
        }
    }
}
