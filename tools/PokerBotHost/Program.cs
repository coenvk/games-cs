using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using PokerBots;

// Usage: PokerBotHost <bot.dll> [TypeName]. Exit codes: 0 done, 2 no single bot (or the file cannot be read), 3 SDK major mismatch.
if (args.Length is < 1 or > 2)
{
    return Fail(2, "usage: PokerBotHost <bot.dll> [TypeName]");
}

var path = Path.GetFullPath(args[0]);
var file = Path.GetFileName(path);
var sdkName = typeof(IPokerBot).Assembly.GetName();

BotLoadContext context;
Assembly assembly;
try
{
    context = new BotLoadContext(path, sdkName);
    assembly = context.LoadFromBytes(path);
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException)
{
    return Fail(2, $"cannot read {file}: {e.Message}");
}

var reference = assembly.GetReferencedAssemblies().FirstOrDefault(a => a.Name == sdkName.Name);
if (reference?.Version is { } version && version.Major != sdkName.Version!.Major)
{
    return Fail(3, $"{file} was built against Poker.Bots {version}, this host has Poker.Bots {sdkName.Version}");
}

IPokerBot bot;
try
{
    var type = PickBot(assembly, file, args.Length > 1 ? args[1] : null, out var problem);
    if (type is null)
    {
        return Fail(2, problem);
    }

    bot = (IPokerBot)Activator.CreateInstance(type)!;
}
catch (Exception e) when (e is TargetInvocationException or ReflectionTypeLoadException or FileNotFoundException or BadImageFormatException)
{
    return Fail(2, $"{file} could not be loaded: {(e as TargetInvocationException)?.InnerException?.Message ?? e.Message}");
}

// UTF-8 without a byte order mark, and written as it comes: the runner flushes after every line.
var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
return BotRunner.Run(bot, Console.In, output, Console.Error);

static int Fail(int code, string message)
{
    Console.Error.WriteLine(message);
    return code;
}

static Type? PickBot(Assembly assembly, string file, string? name, out string problem)
{
    problem = "";
    var bots = BotTypes(assembly);
    if (name is not null)
    {
        var named = bots.Where(t => t.FullName == name || t.Name == name).ToList();
        if (named.Count == 1)
        {
            return named[0];
        }

        problem = named.Count == 0
            ? $"{file} has no bot named {name}" + (bots.Count > 0 ? $"; it has {string.Join(", ", bots.Select(t => t.FullName))}" : "")
            : $"{file} has {named.Count} bots named {name}: {string.Join(", ", named.Select(t => t.FullName))}; use the full name";
        return null;
    }

    if (bots.Count == 1)
    {
        return bots[0];
    }

    problem = bots.Count == 0
        ? $"{file} has no bot"
        : $"{file} has {bots.Count} bots: {string.Join(", ", bots.Select(t => t.Name))}; name one";
    return null;
}

static List<Type> BotTypes(Assembly assembly)
{
    Type[] types;
    try
    {
        types = assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException e)
    {
        types = [.. e.Types.OfType<Type>()];
    }

    return [.. types.Where(t => t is { IsPublic: true, IsAbstract: false, IsClass: true }
            && typeof(IPokerBot).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) is not null)
        .OrderBy(t => t.FullName, StringComparer.Ordinal)];
}

/// <summary>
/// Holds one bot DLL. Poker.Bots comes from the host (so <see cref="IPokerBot"/> is the same type on both sides), the
/// bot's other dependencies from its own folder; everything is read into memory so no file stays locked.
/// </summary>
internal sealed class BotLoadContext(string botPath, AssemblyName sdk) : AssemblyLoadContext("bot", isCollectible: true)
{
    private readonly string _folder = Path.GetDirectoryName(botPath)!;

    public Assembly LoadFromBytes(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return LoadFromStream(stream);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == sdk.Name)
        {
            return typeof(IPokerBot).Assembly;
        }

        var candidate = Path.Combine(_folder, assemblyName.Name + ".dll");
        // Framework assemblies are not beside the bot: null sends them to the default context.
        return File.Exists(candidate) ? LoadFromBytes(candidate) : null;
    }
}
