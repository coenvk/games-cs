using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;

namespace Games.Race;

/// <summary>A drag race from the side: two lanes, distance markers, the finish line; the view keeps the leader in sight.</summary>
internal sealed class DragView(double length, Sprite player, Sprite opponent, Condition condition)
{
    private const float VisibleMetres = 70;

    public void Draw(SpriteBatch batch, Fonts fonts, Rectangle viewport, double playerMetres, double opponentMetres)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(fonts);
        var perMetre = viewport.Width / VisibleMetres;
        var lead = Math.Max(playerMetres, opponentMetres);
        var camera = Math.Clamp(lead - (VisibleMetres * 0.65), 0, Math.Max(0, length - (VisibleMetres * 0.8)));
        float X(double metres) => viewport.X + (float)((metres - camera) * perMetre);

        var horizon = viewport.Y + (int)(viewport.Height * 0.45f);
        var sky = condition == Condition.Wet ? new Color(112, 128, 150) : condition == Condition.Sand ? new Color(150, 190, 230) : new Color(126, 166, 216);
        batch.FillRectangle(new Rectangle(viewport.X, viewport.Y, viewport.Width, horizon - viewport.Y), sky);

        // Stands and buildings, drifting slower than the road.
        for (var i = 0; i < 60; i++)
        {
            var x = viewport.X + (float)((i * 38) - (camera * perMetre * 0.4));
            if (x < viewport.X - 60 || x > viewport.Right)
            {
                continue;
            }

            var h = 30 + ((i * 37) % 50);
            var block = new Rectangle((int)x, horizon - (int)(h * viewport.Height / 400f), (int)(32 * viewport.Width / 1280f) + 6, (int)(h * viewport.Height / 400f));
            batch.FillRectangle(block, condition == Condition.Sand ? new Color(214, 190, 140) : new Color(138, 143, 152));
            batch.FillRectangle(new Rectangle(block.X + 4, block.Y + 4, Math.Max(2, block.Width / 4), Math.Max(2, block.Height / 6)), new Color(109, 114, 123));
        }

        var road = new Rectangle(viewport.X, horizon, viewport.Width, viewport.Bottom - horizon);
        var surface = condition == Condition.Sand ? new Color(200, 172, 116) : condition == Condition.Wet ? new Color(58, 60, 70) : new Color(75, 76, 85);
        batch.FillRectangle(new Rectangle(road.X, road.Y, road.Width, 4), new Color(204, 51, 51));
        batch.FillRectangle(new Rectangle(road.X, road.Y + 4, road.Width, road.Height - 4), surface);
        var laneY = road.Y + (road.Height * 2 / 5);
        batch.FillRectangle(new Rectangle(road.X, laneY, road.Width, 2), new Color(221, 221, 221));

        // Markers every 100 m, and the finish line.
        var small = fonts.Small;
        for (var m = 0; m <= length; m += 100)
        {
            var x = X(m);
            if (x < viewport.X - 2 || x > viewport.Right)
            {
                continue;
            }

            batch.FillRectangle(new Rectangle((int)x, road.Y + 4, 2, road.Height - 4), new Color(255, 255, 255, 120));
            batch.DrawText(small, string.Create(CultureInfo.InvariantCulture, $"{m} m"), new Vector2(x + 4, road.Y + 6), Color.White, shadow: false);
        }

        var finish = X(length);
        var square = Math.Max(4, road.Height / 16);
        for (var y = road.Y + 4; y < road.Bottom; y += square)
        {
            for (var k = 0; k < 2; k++)
            {
                var white = (((y - road.Y) / square) + k) % 2 == 0;
                batch.FillRectangle(new Rectangle((int)finish + (k * square), y, square, square), white ? Color.White : Color.Black);
            }
        }

        // The cars: the opponent in the far lane, the player in the near one, fronts at their distance.
        var scale = Math.Max(1, (int)MathF.Round(perMetre * 4.6f / 48));
        DrawCar(batch, opponent, X(opponentMetres), laneY - (road.Height / 10), scale);
        DrawCar(batch, player, X(playerMetres), laneY + (road.Height * 3 / 10), scale);
    }

    private static void DrawCar(SpriteBatch batch, Sprite sprite, float front, int bottom, int scale)
    {
        // The sprite's car is centred in it with a few clear pixels around; its front sits about 6 pixels in from the right.
        var size = new Point(sprite.Source.Width * scale, sprite.Source.Height * scale);
        var bounds = new Rectangle((int)front - size.X + (6 * scale), bottom - size.Y, size.X, size.Y);
        batch.Draw(sprite, bounds, Color.White);
    }
}
