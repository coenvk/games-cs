using Jentl.Assets;
using Jentl.Raster;

namespace Paddock.Tests;

public class ChaseModelTests
{
    // As the 3D race view makes them: side sprites at 4.6 m per 48 pixels, 1.75 m wide, sides read past the border.
    [Fact]
    public void EveryCarsSideSprite_MakesACarSizedModel()
    {
        var cars = Roster.FromJson(GameData.Read("cars.json"));
        var atlas = AtlasFile.FromJson(GameData.Read("cars.atlas.json"));
        var image = Png.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "paddock", atlas.Image)));

        foreach (var car in cars.All)
        {
            var r = atlas.Sprites[$"side/{car.Id}"];
            var rgba = new byte[r.Width * r.Height * 4];
            for (var y = 0; y < r.Height; y++)
            {
                Array.Copy(image.Pixels, (((r.Y + y) * image.Width) + r.X) * 4, rgba, y * r.Width * 4, r.Width * 4);
            }

            var model = Models.FromPicture(Texture.FromRgba(r.Width, r.Height, rgba), 4.6f / 48, 1.75f, sideInset: 1.5f);

            model.Triangles.Should().NotBeEmpty(car.Id);
            model.Bounds.Min.Y.Should().Be(0, car.Id);
            model.Bounds.Size.X.Should().BeInRange(3, 7, car.Id);
            model.Bounds.Size.Y.Should().BeInRange(1, 3, car.Id);
        }
    }
}
