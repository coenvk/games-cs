using System.Globalization;
using Sokoban.Desktop;

// Options:
//   --level <n>          start at level n (1-based)
//   --keys "<k1> <k2>"   play these keys (MonoGame key names, or Wait) instead of the keyboard
//   --screenshot <path>  after the keys have played, render one frame to a PNG and exit
//   --save-dir <path>    keep progress in this directory instead of the user data folder
var options = new GameOptions();
for (var i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--level":
            options = options with { StartLevel = int.Parse(args[i + 1], CultureInfo.InvariantCulture) - 1 };
            break;
        case "--keys":
            options = options with { Script = args[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries) };
            break;
        case "--screenshot":
            options = options with { ScreenshotPath = args[i + 1] };
            break;
        case "--save-dir":
            options = options with { SaveDirectory = args[i + 1] };
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'.");
            return 1;
    }
}

using var game = new SokobanGame(options);
game.Run();
return 0;
