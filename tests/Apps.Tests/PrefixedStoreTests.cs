using Games;
using Jentl.Core.Storage;

namespace Apps.Tests;

/// <summary>A suite's games share one save folder, each in its own corner: the same file name never meets.</summary>
public sealed class PrefixedStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "prefixed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TwoGames_KeepTheSameFileApart()
    {
        var shared = new SaveStore(_folder);
        var words = new PrefixedStore(shared, "ConnectWord-");
        var cards = new PrefixedStore(shared, "AssociateWords-");

        words.WriteText("progress.json", "connect");
        cards.WriteText("progress.json", "associate");

        words.ReadText("progress.json").Should().Be("connect");
        cards.ReadText("progress.json").Should().Be("associate");
        shared.ReadText("ConnectWord-progress.json").Should().Be("connect");
        shared.ReadText("progress.json").Should().BeNull("nothing lands outside a game's corner");
    }

    [Fact]
    public void Deleting_TouchesOnlyTheGamesOwnFile()
    {
        var shared = new SaveStore(_folder);
        shared.WriteText("settings.json", "suite");
        var game = new PrefixedStore(shared, "Bricks-");
        game.WriteText("settings.json", "bricks");

        game.Delete("settings.json");

        game.ReadText("settings.json").Should().BeNull();
        shared.ReadText("settings.json").Should().Be("suite");
    }
}
