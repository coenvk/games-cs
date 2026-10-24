using Jentl.Assets;
using Jentl.Core.Localization;
using Jentl.Rpg.Maps;

namespace Apps.Tests;

public class AppFileTests
{
    private static readonly string Root = RepositoryRoot();

    public static TheoryData<string> Apps => ["Sokoban", "Solitaire", "CardTable", "Mossbrook", "CodeLock", "Bricks", "Paddock"];

    /// <summary>One language file serves every app, so it must cover the shell's texts and all the apps'.</summary>
    [Fact]
    public void EveryText_IsTranslated_AndNoTranslationLingers()
    {
        var used = new HashSet<string>(TranslationCheck.SourceTexts(Path.Combine(Root, "shell")), StringComparer.Ordinal);
        used.UnionWith(TranslationCheck.SourceTexts(Path.Combine(Root, "apps")));
        used.Should().Contain(["Options", "Select level", "{0} move"], "the scan finds texts in the shell and the apps");
        used.UnionWith(PaddockTexts());

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

    // Paddock shows names from its data (tracks, events, campaign regions, makers' countries) and a few texts from its
    // rules library (pack names, why an event can't be entered), all through the language file.
    private static IEnumerable<string> PaddockTexts()
    {
        var folder = Path.Combine(Root, "apps", "Paddock", "Assets", "paddock");
        IEnumerable<string> Strings(string file, string list, string field)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, file)));
            return [.. json.RootElement.GetProperty(list).EnumerateArray().Select(e => e.GetProperty(field).GetString()!)];
        }

        return Strings("tracks.json", "tracks", "name")
            .Concat(Strings("events.json", "events", "name"))
            .Concat(Strings("campaign.json", "regions", "name"))
            .Concat(Strings("cars.json", "cars", "country"))
            .Concat(["Starter pack", "Classic pack", "Elite pack", "Event finished", "Not enough cars that may enter", "No tickets left"]);
    }

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
