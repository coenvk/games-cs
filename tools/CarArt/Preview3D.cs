using System.Numerics;
using Jentl.Assets;
using Jentl.Raster;
using Paddock;

namespace CarArt;

/// <summary>Every car's 3D model in a grid, seen from three-quarters in front, for checking the models by eye.</summary>
internal static class Preview3D
{
    private const int Cell = 200;
    private const int Columns = 10;

    public static PixelCanvas Draw(IReadOnlyList<CarModel> cars)
    {
        var rows = (cars.Count + Columns - 1) / Columns;
        var sheet = new PixelCanvas(Cell * Columns, Cell * rows);
        var camera = new Camera { Position = new Vector3(4.2f, 2.4f, 5.2f), Target = new Vector3(0, 0.6f, 0), FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 40 };
        var renderer = new Renderer(new FrameBuffer(Cell, Cell)) { ViewProjection = camera.ViewProjection(1) };
        for (var i = 0; i < cars.Count; i++)
        {
            renderer.Clear(Colors.Pack(new Vector4(0x1c / 255f, 0x21 / 255f, 0x30 / 255f, 1)));
            renderer.DrawModel(CarModels.Build(cars[i]), Matrix4x4.Identity);
            var (left, top) = (i % Columns * Cell, i / Columns * Cell);
            for (var y = 0; y < Cell; y++)
            {
                for (var x = 0; x < Cell; x++)
                {
                    var p = renderer.Target[x, y];
                    sheet[left + x, top + y] = new Rgba((byte)p, (byte)(p >> 8), (byte)(p >> 16));
                }
            }
        }

        return sheet;
    }
}
