using Jentl.Net;
using Poker.Arena;

namespace Poker.Tests;

public sealed class BotCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("bots").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Catalog_FindsProgramsAndDlls()
    {
        File.WriteAllText(Path.Combine(_folder, "bot.bot.json"), """{ "name": "Prog", "command": "python3", "args": ["bot.py"], "workingDirectory": "sub" }""");
        File.WriteAllText(Path.Combine(_folder, "Mine.dll"), "");
        File.WriteAllText(Path.Combine(_folder, "Poker.Bots.dll"), "");
        File.WriteAllText(Path.Combine(_folder, "broken.bot.json"), "{ nope");
        var host = new AgentCommand("dotnet", ["host.dll"]);

        var entries = BotCatalog.Scan(_folder, host, []);

        entries.Select(e => e.Name).Should().Equal("broken", "Mine", "Prog");
        var program = entries.Single(e => e.Name == "Prog");
        program.Kind.Should().Be(BotKind.Program);
        program.Command.Should().NotBeNull();
        program.Command!.FileName.Should().Be("python3");
        program.Command.Arguments.Should().Equal("bot.py");
        program.Command.WorkingDirectory.Should().Be(Path.Combine(_folder, "sub"));
        program.Source.Should().Be(Path.Combine(_folder, "bot.bot.json"));

        var dll = entries.Single(e => e.Name == "Mine");
        dll.Kind.Should().Be(BotKind.CSharp);
        dll.Command!.FileName.Should().Be("dotnet");
        dll.Command.Arguments.Should().Equal("host.dll", Path.Combine(_folder, "Mine.dll"));

        var broken = entries.Single(e => e.Name == "broken");
        broken.Kind.Should().Be(BotKind.Program);
        broken.Command.Should().BeNull();
        broken.Source.Should().Contain("broken.bot.json");
    }

    [Fact]
    public void ADescriptionWithoutACommand_IsUnreadable()
    {
        File.WriteAllText(Path.Combine(_folder, "empty.bot.json"), "{ \"name\": \"Empty\" }");

        var entry = BotCatalog.Scan(_folder, new AgentCommand("dotnet", [])).Single();

        entry.Command.Should().BeNull();
        entry.Source.Should().Contain("empty.bot.json").And.Contain("command");
    }

    [Fact]
    public void ARelativeCommand_IsResolvedAgainstItsWorkingDirectory()
    {
        File.WriteAllText(Path.Combine(_folder, "rel.bot.json"), "{ \"command\": \"./run.sh\", \"workingDirectory\": \"sub\" }");

        var entry = BotCatalog.Scan(_folder, new AgentCommand("dotnet", [])).Single();

        entry.Command!.FileName.Should().Be(Path.Combine(_folder, "sub", "run.sh"));
    }

    [Fact]
    public void TheFolder_MayBeRelative()
    {
        File.WriteAllText(Path.Combine(_folder, "Mine.dll"), "");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), _folder);

        var entry = BotCatalog.Scan(relative, new AgentCommand("dotnet", [])).Single();

        entry.Source.Should().Be(Path.Combine(_folder, "Mine.dll"));
    }

    [Fact]
    public void ACatalog_OfAMissingFolder_IsEmpty() =>
        BotCatalog.Scan(Path.Combine(_folder, "nowhere"), new AgentCommand("dotnet", [])).Should().BeEmpty();

    [Fact]
    public void ABareCommand_IsLookedUpInTheSearchFolders()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_folder, "bin")).FullName;
        var program = Path.Combine(bin, OperatingSystem.IsWindows() ? "my-interpreter.exe" : "my-interpreter");
        File.WriteAllText(program, "");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(_folder, "found.bot.json"), "{ \"command\": \"my-interpreter\" }");
        File.WriteAllText(Path.Combine(_folder, "lost.bot.json"), "{ \"command\": \"no-such-interpreter\" }");

        var entries = BotCatalog.Scan(_folder, new AgentCommand("dotnet", []), [Path.Combine(_folder, "nowhere"), bin]);

        entries.Single(e => e.Name == "found").Command!.FileName.Should().Be(program);
        entries.Single(e => e.Name == "lost").Command!.FileName.Should().Be("no-such-interpreter", "a program found nowhere is left to the system");
    }
}
