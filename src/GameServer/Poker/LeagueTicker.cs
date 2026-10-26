using System.Globalization;
using System.Text.Json;
using Jentl.Net;

namespace GameServer.Poker;

/// <summary>
/// Keeps the poker league going: at start-up it logs what went wrong reading the data folder, it greets every runner
/// that connects with its bot's rating, and it ticks the league four times a second, so waiting bots are paired and
/// asked-for practice matches start. The league itself is a singleton the host disposes on shutdown, which drops the
/// matches still running.
/// </summary>
internal sealed partial class LeagueTicker(PokerLeague league, PokerData data, RelayHost relays, Ratings ratings, ILogger<LeagueTicker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await league.TickAsync().ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poker data: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);
}
