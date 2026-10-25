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

        var entries = BotCatalog.Scan(_folder, host);

        entries.Select(e => e.Name).Should().Equal("broken", "Mine", "Prog");
        var program = entries.Single(e => e.Name == "Prog");
        program.Kind.Should().Be(BotKind.Program);
        program.Command.Should().NotBeNull();
        program.Command!.FileName.Should().Be("python3");
        program.Command.Arguments.Should().Equal("bot.py");
        program.Command.WorkingDirectory.Should().Be(Path.Combine(_folder, "sub"));

        var dll = entries.Single(e => e.Name == "Mine");
        dll.Kind.Should().Be(BotKind.CSharp);
        dll.Command!.FileName.Should().Be("dotnet");
        dll.Command.Arguments.Should().Equal("host.dll", Path.Combine(_folder, "Mine.dll"));

        var broken = entries.Single(e => e.Name == "broken");
        broken.Command.Should().BeNull();
        broken.Source.Should().Contain("broken.bot.json");
    }

    [Fact]
    public void ACatalog_OfAMissingFolder_IsEmpty() =>
        BotCatalog.Scan(Path.Combine(_folder, "nowhere"), new AgentCommand("dotnet", [])).Should().BeEmpty();
}
