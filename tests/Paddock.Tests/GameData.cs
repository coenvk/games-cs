namespace Paddock.Tests;

/// <summary>The game's own data files, as the app ships them (linked into the test output).</summary>
internal static class GameData
{
    public static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "paddock", file));

    public static IReadOnlyList<TrackDef> Tracks => TrackDef.ListFromJson(Read("tracks.json"));
}
