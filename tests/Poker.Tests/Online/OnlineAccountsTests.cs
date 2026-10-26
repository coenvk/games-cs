using Poker.Online;

namespace Poker.Tests;

/// <summary>The owner and bot tokens kept on the player's computer, in <c>workshop/online.json</c>.</summary>
public sealed class OnlineAccountsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "online-accounts-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "workshop", "online.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void SavedAccounts_AreReadBack()
    {
        var accounts = new OnlineAccounts();
        var account = accounts.For("http://localhost:5080/");
        account.OwnerToken = "owner";
        account.BotTokens["Alpha"] = "alpha";
        account.BotFiles["Alpha"] = "/bots/alpha.bot.json";

        accounts.Save(File);
        var read = OnlineAccounts.Load(File);

        read.BrokenCopy.Should().BeNull();
        var again = read.For("http://localhost:5080/");
        again.OwnerToken.Should().Be("owner");
        again.BotTokens.Should().Equal(new Dictionary<string, string> { ["Alpha"] = "alpha" });
        again.BotFiles["Alpha"].Should().Be("/bots/alpha.bot.json");
        read.For("http://elsewhere/").OwnerToken.Should().BeNull("each server has its own owner");
    }

    [Fact]
    public void NoFile_IsNoAccountYet()
    {
        var read = OnlineAccounts.Load(File);

        read.BrokenCopy.Should().BeNull();
        read.Servers.Should().BeEmpty();
    }

    [Fact]
    public void AFileThatCannotBeRead_IsMovedAside_NotOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, "{ \"servers\": { \"http://x/\": { \"ownerToken\": \"kept\" ");

        var read = OnlineAccounts.Load(File);
        read.For("http://x/").OwnerToken = "new";
        read.Save(File);

        read.BrokenCopy.Should().NotBeNull();
        Path.GetFileName(read.BrokenCopy).Should().StartWith("online.json.broken-");
        System.IO.File.ReadAllText(read.BrokenCopy!).Should().Contain("\"kept\"", "the old owner token can still be put back by hand");
        OnlineAccounts.Load(File).For("http://x/").OwnerToken.Should().Be("new");
    }
}
