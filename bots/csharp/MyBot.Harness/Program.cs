using System.Globalization;
using Poker;
using Poker.Arena;

// Usage: dotnet run [hands]. Plays MyBot against every house bot, heads-up, as a duplicate batch: every deal is
// played twice with the seats swapped, so the luck of the cards cancels out. The default is 2,000 deals (4,000 hands).
var hands = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 2000;
var settings = new BatchSettings(BatchFormat.DuplicateHeadsUp, hands, Seed: DealSeed.FromNumber(1), TimeSpan.FromSeconds(1));

Console.WriteLine($"{"Opponent",-14}{"bb/100 ± margin",18}{"hands",8}");
foreach (var (name, make) in HouseBots.All)
{
    // A fresh seat for every run, as the app does. Put a breakpoint in MyBot.Decide and start this in the debugger.
    var batch = new ArenaBatch(
        [() => new HouseSeat(new MyBot.MyBot()), () => new HouseSeat(make(1))],
        settings);
    var result = await batch.RunAsync(new Progress<double>(), CancellationToken.None);
    var mine = result.Results[0];
    var score = string.Create(CultureInfo.InvariantCulture, $"{mine.BigBlindsPer100:+0.0;-0.0} ± {mine.Margin95:0.0}");
    Console.WriteLine($"{name,-14}{score,18}{mine.Hands,8}");
}
