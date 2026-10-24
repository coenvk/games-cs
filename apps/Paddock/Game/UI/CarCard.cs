using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using static Games.UI.Tr;

namespace Games.UI;

/// <summary>
/// The full car card (the mockup's style A): a frame and header in the rarity colour with the class and RQ, the car's
/// side view on a sky and road, name, maker and year, top speed, 0-100, handling and weight (with upgrades), tags for
/// drive, tyres and clearance, and the stage and tune. Laid out for any size, at 5:7.
/// </summary>
internal static class CarCard
{
    /// <summary>Width over height.</summary>
    public const float Aspect = 5f / 7f;

    private static readonly Color Ink = new(28, 33, 48);
    private static readonly Color Sky = new(126, 166, 216);
    private static readonly Color SkyLow = new(185, 211, 238);
    private static readonly Color Road = new(75, 76, 85);
    private static readonly Color Line = new(41, 48, 74);

    /// <summary>The colour of a rarity class.</summary>
    public static Color ColorOf(Rarity rarity) => rarity switch
    {
        Rarity.F => new Color(138, 143, 152),
        Rarity.E => new Color(111, 174, 90),
        Rarity.D => new Color(63, 143, 216),
        Rarity.C => new Color(154, 90, 216),
        Rarity.B => new Color(216, 112, 63),
        Rarity.A => new Color(216, 63, 90),
        _ => new Color(232, 192, 64),
    };

    /// <summary>The card's size for a width.</summary>
    public static Point Size(int width) => new(width, (int)(width / Aspect));

    /// <summary>Draws the card for <paramref name="car"/> filling <paramref name="bounds"/> (keep it at <see cref="Aspect"/>).</summary>
    /// <param name="highlight">Draws a bright outline, for the selected card.</param>
    public static void Draw(SpriteBatch batch, Fonts fonts, PaddockData data, CarInstance car, Rectangle bounds, bool highlight = false, float alpha = 1)
    {
        var model = data.Roster.Find(car.ModelId);
        if (model is null)
        {
            return;
        }

        var rq = data.Roster.Rq(model.Id);
        var rarity = Rating.ClassOf(rq);
        var frame = ColorOf(rarity) * alpha;
        var u = bounds.Width / 300f;
        int U(float v) => (int)MathF.Round(v * u);
        var big = fonts.Sized(26 * u);
        var small = fonts.Sized(19 * u);

        batch.FillRectangle(bounds, frame);
        if (highlight)
        {
            batch.StrokeRectangle(new Rectangle(bounds.X - U(4), bounds.Y - U(4), bounds.Width + U(8), bounds.Height + U(8)), Palette.Accent * alpha, Math.Max(2, U(3)));
        }

        var inner = new Rectangle(bounds.X + U(7), bounds.Y + U(7), bounds.Width - U(14), bounds.Height - U(14));
        batch.FillRectangle(inner, Ink * alpha);

        // Header: class letter and RQ.
        var header = new Rectangle(inner.X, inner.Y, inner.Width, U(34));
        batch.FillRectangle(header, frame);
        batch.DrawText(big, rarity.ToString(), new Vector2(header.X + U(10), header.Y + U(2)), Ink * alpha, shadow: false);
        var rqText = string.Create(CultureInfo.InvariantCulture, $"RQ {rq}");
        batch.DrawText(big, rqText, new Vector2(header.Right - U(10), header.Y + U(2)), Ink * alpha, Align.Right, shadow: false);

        // The car on a sky and a strip of road, at a whole-number scale so its pixels stay square.
        var picture = new Rectangle(inner.X, header.Bottom, inner.Width, U(118));
        batch.FillRectangle(new Rectangle(picture.X, picture.Y, picture.Width, picture.Height * 3 / 5), Sky * alpha);
        batch.FillRectangle(new Rectangle(picture.X, picture.Y + (picture.Height * 3 / 5), picture.Width, picture.Height / 5), SkyLow * alpha);
        batch.FillRectangle(new Rectangle(picture.X, picture.Y + (picture.Height * 4 / 5), picture.Width, picture.Height - (picture.Height * 4 / 5)), Road * alpha);
        if (data.Cars.TryGet($"side/{model.Id}", out var side))
        {
            var scale = Math.Max(1, (picture.Width - U(16)) / side.Source.Width);
            var size = new Point(side.Source.Width * scale, side.Source.Height * scale);
            batch.Draw(side, new Rectangle(picture.Center.X - (size.X / 2), picture.Bottom - size.Y - U(2), size.X, size.Y), Color.White * alpha);
        }

        // Name, maker and year.
        var y = picture.Bottom + U(6);
        batch.DrawText(big, Fit(big, model.Name, inner.Width - U(16)), new Vector2(inner.X + U(8), y), Palette.Text * alpha, shadow: false);
        y += (int)big.LineHeight;
        batch.DrawText(small, string.Create(CultureInfo.InvariantCulture, $"{model.Maker}, {model.Year}"), new Vector2(inner.X + U(8), y), Palette.Muted * alpha, shadow: false);
        y += (int)small.LineHeight + U(4);

        // The four stats, with upgrades.
        var stats = Matches.StatsOf(car, data.Roster);
        (string Label, string Value)[] rows =
        [
            (T("Top speed"), string.Create(CultureInfo.InvariantCulture, $"{stats.TopSpeedKmh:0} km/h")),
            (T("0-100"), stats.TopSpeedKmh < 100 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{stats.ZeroTo100:0.0} s")),
            (T("Handling"), string.Create(CultureInfo.InvariantCulture, $"{stats.Handling:0}")),
            (T("Weight"), string.Create(CultureInfo.InvariantCulture, $"{stats.WeightKg:#,0} kg")),
        ];
        foreach (var (label, value) in rows)
        {
            batch.FillRectangle(new Rectangle(inner.X, y, inner.Width, Math.Max(1, U(1))), Line * alpha);
            batch.DrawText(small, label, new Vector2(inner.X + U(8), y + U(3)), Palette.Text * alpha, shadow: false);
            batch.DrawText(small, value, new Vector2(inner.Right - U(8), y + U(3)), Palette.Text * alpha, Align.Right, shadow: false);
            y += (int)small.LineHeight + U(4);
        }

        // Tags: drive, tyres, clearance; then stage and tune when upgraded.
        y += U(4);
        var x = inner.X + U(8);
        foreach (var tag in new[] { Words.Of(stats.Drive), Words.Of(stats.Tyres), Words.Of(stats.Clearance) })
        {
            var width = (int)small.MeasureString(tag).X + U(10);
            batch.FillRectangle(new Rectangle(x, y, width, (int)small.LineHeight + U(2)), Line * alpha);
            batch.DrawText(small, tag, new Vector2(x + U(5), y + U(1)), Palette.Text * alpha, shadow: false);
            x += width + U(5);
        }

        if (car.Stage > 1 || car.Tune.Total > 0)
        {
            var tune = T("Stage {0} · {1}", car.Stage, car.Tune);
            batch.DrawText(small, tune, new Vector2(inner.Right - U(8), inner.Bottom - small.LineHeight - U(6)), Palette.Accent * alpha, Align.Right, shadow: false);
        }
    }

    private static string Fit(FontStashSharp.SpriteFontBase font, string text, float width)
    {
        if (font.MeasureString(text).X <= width)
        {
            return text;
        }

        while (text.Length > 1 && font.MeasureString(text + "…").X > width)
        {
            text = text[..^1];
        }

        return text + "…";
    }
}
