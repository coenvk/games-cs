using System.Text.Json;

namespace Paddock.Tests;

public class ArtTests
{
    [Fact]
    public void Atlas_HasSideAndTopViews_ForEveryCar()
    {
        var cars = Roster.FromJson(GameData.Read("cars.json"));
        using var atlas = JsonDocument.Parse(GameData.Read("cars.atlas.json"));
        var sprites = atlas.RootElement.GetProperty("sprites").EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var car in cars.All)
        {
            sprites.Should().Contain($"side/{car.Id}").And.Contain($"top/{car.Id}");
        }

        File.Exists(Path.Combine(AppContext.BaseDirectory, "paddock", atlas.RootElement.GetProperty("image").GetString()!)).Should().BeTrue();
    }
}
