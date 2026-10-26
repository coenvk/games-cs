using System.Globalization;
using System.Text.Json;
using Jentl.Net;

namespace GameServer.Poker;

/// <summary>
/// Keeps the poker league going: at start-up it logs what went wrong reading the data folder, it greets every runner
/// that connects with its bot's rating, and it ticks the league four times a second, so waiting bots are paired and
/// asked-for practice matches start; a tick that fails is logged, and the ticking goes on. The league itself is a
/// singleton the host disposes on shutdown, which drops the matches still running.
/// </summary>
internal sealed partial class LeagueTicker(PokerLeague league, PokerData data, RelayHost relays, Ratings ratings, ILogger<LeagueTicker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest wait between ticks while the league keeps failing.</summary>
    private static readonly TimeSpan LongestBackOff = TimeSpan.FromSeconds(30);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var problem in data.Problems)
        {
            LogProblem(logger, problem);
        }

        relays.Connected += Welcome;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        relays.Connected -= Welcome;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => KeepTickingAsync(league.TickAsync, Tick, logger, stoppingToken);

    /// <summary>
    /// Calls <paramref name="tick"/> every <paramref name="period"/> until <paramref name="stop"/> is cancelled. A tick
    /// that throws is logged and the ticking goes on: a background service that ends with an exception stops the whole
    /// server, Uno rooms and all. Each failure in a row doubles the wait before the next tick, up to
    /// <see cref="LongestBackOff"/>, so a league that keeps failing does not fill the log.
    /// </summary>
    internal static async Task KeepTickingAsync(Func<Task> tick, TimeSpan period, ILogger logger, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(period);
        var failures = 0;
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
        {
            try
            {
                await tick().ConfigureAwait(false);
                failures = 0;
            }
#pragma warning disable CA1031 // Whatever the league throws is logged: the server and its other games go on.
            catch (Exception e) when (!stop.IsCancellationRequested)
#pragma warning restore CA1031
            {
                failures++;
                LogTickFailed(logger, e, failures);
                if (failures > 1)
                {
                    var backOff = period * Math.Pow(2, Math.Min(failures - 1, 16));
                    await Task.Delay(backOff < LongestBackOff ? backOff : LongestBackOff, stop).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Tells a runner that has just connected where its bot stands, before it waits for an opponent.</summary>
    private void Welcome(string bot)
    {
        var record = ratings.Of(bot);
        var range = 2 * record.Rating.Deviation;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"connected as {bot}: rating {record.Rating.Rating:0} ± {range:0} after {record.Matches} ranked matches; waiting for an opponent");
        var status = new WelcomeStatus("waiting", text, record.Rating.Rating, range, record.Matches);
        relays.Agent(bot)?.Status(JsonSerializer.SerializeToElement(status, PokerJson.Default.WelcomeStatus));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The poker league's tick failed ({Failures} in a row); it ticks on")]
    private static partial void LogTickFailed(ILogger logger, Exception error, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poker data: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);
}
