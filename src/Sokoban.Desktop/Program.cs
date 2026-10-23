using System.Globalization;
using Sokoban.Desktop;

// Options:
//   --level <n>          start at level n (1-based)
//   --screenshot <path>  render one frame to a PNG and exit
var options = new GameOptions();
for (var i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--level":
            options = options with { StartLevel = int.Parse(args[i + 1], CultureInfo.InvariantCulture) - 1 };
            break;
        case "--screenshot":
            options = options with { ScreenshotPath = args[i + 1] };
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'.");
            return 1;
    }
}

using var game = new SokobanGame(options);
game.Run();
return 0;
