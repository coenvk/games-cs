using System.Globalization;
using Jentl.Core.Content;
using Jentl.Core.Storage;
using Sokoban.Desktop;

// Options:
//   --level <n>          start at level n (1-based)
//   --keys "<k1> <k2>"   play these keys (MonoGame key names, or Wait) instead of the keyboard
//   --screenshot <path>  after the keys have played, render one frame to a PNG and exit
//   --save-dir <path>    keep progress in this directory instead of the user data folder
//   --settle <frames>    idle frames after the keys before the screenshot (default 60)
//   --deal <n>           deal solitaire game n instead of a random one
//   --size <w>x<h>       window size in pixels (default 1280x800)
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
        case "--settle":
            options = options with { SettleFrames = int.Parse(args[i + 1], CultureInfo.InvariantCulture) };
            break;
        case "--deal":
            options = options with { Deal = int.Parse(args[i + 1], CultureInfo.InvariantCulture) };
            break;
        case "--size":
            var size = args[i + 1].Split('x');
            options = options with { Size = new Microsoft.Xna.Framework.Point(int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture)) };
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'.");
            return 1;
    }
}

// Scripted runs must not touch the player's real saves unless a directory is given.
ISaveStore? store = options switch
{
    { SaveDirectory: { } directory } => new SaveStore(directory),
    { Script: not null } => null,
    _ => SaveStore.ForApp("Sokoban"),
};

// Game files sit next to the executable (also inside a macOS .app bundle).
GameFiles.Root = AppContext.BaseDirectory;

using var game = new SokobanGame(options, store, PlatformFeatures.Desktop);
game.Run();
return 0;
