namespace Paddock.Tests;

public class CarModelTests
{
    [Fact]
    public void EveryCar_HasAModelOfItsShape_StandingOnItsWheels()
    {
        var cars = Roster.FromJson(GameData.Read("cars.json"));

        foreach (var car in cars.All)
        {
            var model = CarModels.Build(car);
            var shape = BodyShape.Of(car.Body);

            model.Bounds.Min.Y.Should().BeApproximately(0, 0.02f, car.Id);
            model.Bounds.Size.X.Should().BeApproximately(shape.Length * CarModels.Pixel, 0.4f, car.Id);
            model.Bounds.Size.Z.Should().BeInRange(1.5f, 2.3f, car.Id);
            model.Parts.Count.Should().BeGreaterThanOrEqualTo(4, car.Id);
            model.Parts.Sum(p => p.Mesh.Triangles.Count).Should().BeLessThan(800, car.Id);
        }
    }
}
