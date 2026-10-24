using Jentl.Assets;
using Jentl.Core.Localization;
using Jentl.Rpg.Maps;

namespace Apps.Tests;

public class AppFileTests
{
    private static readonly string Root = RepositoryRoot();

    public static TheoryData<string> Apps => ["Sokoban", "Solitaire", "CardTable", "Mossbrook", "CodeLock", "Bricks"];

    /// <summary>One language file serves every app, so it must cover the shell's texts and all the apps'.</summary>
    [Fact]
    public void EveryText_IsTranslated_AndNoTranslationLingers()
    {
        var used = new HashSet<string>(TranslationCheck.SourceTexts(Path.Combine(Root, "shell")), StringComparer.Ordinal);
        used.UnionWith(TranslationCheck.SourceTexts(Path.Combine(Root, "apps")));
        used.Should().Contain(["Options", "Select level", "{0} move"], "the scan finds texts in the shell and the apps");

        // Looked up from data rather than written in the code: Sokoban's level packs and their title prefix.
        TranslationCheck.Problems(used, Path.Combine(Root, "shell", "Assets", "lang"), indirect: ["Classic", "Ice", "Level"])
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void TheAppsAssets_PassTheCheck(string app)
    {
        var assets = Path.Combine(Root, "apps", app, "Assets");
        if (!Directory.Exists(assets))
        {
            return;
        }

        var check = new AssetCheck(assets);
        check.Checks[".tmx"] = MapCheck.Map;
        check.Checks[".tsx"] = MapCheck.Tileset;

        check.Run().Should().BeEmpty("rebuild a stale atlas with build/atlas.sh");
    }

    [Fact]
    public void TheSharedAssets_PassTheCheck() =>
        new AssetCheck(Path.Combine(Root, "shell", "Assets")).Run().Should().BeEmpty();

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Games.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root (with Games.slnx) was not found.");
    }
}
