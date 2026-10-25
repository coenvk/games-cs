using Jentl.Assets;
using Paddock;

namespace CarArt;

/// <summary>Draws one car, side and top, from its body, era and colours. The same car always comes out the same.</summary>
internal static class Painter
{
    public const int SideWidth = 64;
    public const int SideHeight = 24;
    public const int TopWidth = 18;
    public const int TopHeight = 10;

    private const int Ground = 23;

    private static readonly Rgba Tyre = Rgba.Hex(0x1a1a1e);
    private static readonly Rgba Glass = Rgba.Hex(0x9fd3ff);
    private static readonly Rgba GlassDark = Rgba.Hex(0x5d8fb8);
    private static readonly Rgba Lamp = Rgba.Hex(0xfff6a0);
    private static readonly Rgba TailLamp = Rgba.Hex(0xd82020);
    private static readonly Rgba Interior = Rgba.Hex(0x2a2226);
    private static readonly Rgba White = Rgba.Hex(0xf2eee4);

    public static PixelCanvas Side(CarModel car)
    {
        var shape = BodyShape.Of(car.Body);
        var paint = Parse(car.Paint);
        var trim = Parse(car.Trim);
        var era = Era.Of(car.Year);
        var canvas = new PixelCanvas(SideWidth, SideHeight);
        var left = (SideWidth - shape.Length) / 2;
        var right = left + shape.Length - 1;
        int At(double share) => left + (int)Math.Round(share * (shape.Length - 1));
        var (cabinFrom, cabinTo) = (At(shape.CabinFrom), At(shape.CabinTo));
        var wedge = car.Body is Body.Supercar or Body.Hypercar || era == Era.Wedge;

        // The silhouette, column by column: bonnet and boot at hood height, the cabin up to the roof with sloped glass.
        for (var x = left; x <= right; x++)
        {
            var top = shape.Hood;
            if (wedge && x > cabinTo)
            {
                top += (x - cabinTo) * 3 / Math.Max(1, right - cabinTo); // the nose drops away
            }

            if (shape.Roof > 0 && x >= cabinFrom && x <= cabinTo)
            {
                var rise = shape.Hood - shape.Roof;
                var fromBack = x - cabinFrom;
                var fromFront = cabinTo - x;
                // Slopes leave a flat roof of at least four pixels; boxy cars have an upright back.
                var room = Math.Max(1, (cabinTo - cabinFrom - 4) / 2);
                var rearSlope = car.Body is Body.Hatchback or Body.Estate or Body.Suv or Body.Van or Body.Pickup ? 1 : Math.Min(room, (rise / 2) + 1);
                var frontSlope = Math.Min(room, wedge ? rise + 2 : (rise / 2) + 2);
                top = Math.Max(shape.Roof, Math.Max(shape.Hood - (fromBack * rise / rearSlope), shape.Hood - (fromFront * rise / frontSlope)));
            }

            canvas.Rect(x, top, 1, shape.Sill - top + 1, paint);
        }

        // Rounded corners, rounder on old cars.
        var round = era == Era.Classic ? 3 : 2;
        for (var i = 0; i < round; i++)
        {
            for (var j = 0; j < round - i; j++)
            {
                canvas[left + i, shape.Hood + j] = Rgba.Clear;
                canvas[right - i, shape.Hood + j + (wedge ? 2 : 0)] = Rgba.Clear;
            }
        }

        // Shading: a light line along the top, the lower body darker.
        for (var x = left; x <= right; x++)
        {
            for (var y = 0; y < SideHeight; y++)
            {
                if (!canvas[x, y].IsClear)
                {
                    canvas[x, y] = paint.Lighter(0.35);
                    break;
                }
            }

            canvas.Rect(x, shape.Sill - 1, 1, 2, paint.Darker(0.3));
        }

        // Windows: inside the cabin, a pillar in the middle; an open car shows its seats and a windscreen.
        if (shape.Roof > 0)
        {
            for (var x = cabinFrom + 2; x <= cabinTo - 2; x++)
            {
                var top = Enumerable.Range(0, SideHeight).First(y => !canvas[x, y].IsClear) + 1;
                for (var y = top; y < shape.Hood; y++)
                {
                    canvas[x, y] = x == (cabinFrom + cabinTo) / 2 ? paint.Darker(0.2) : y == top ? Glass.Lighter(0.4) : Glass;
                }
            }
        }
        else
        {
            canvas.Rect(cabinFrom + 2, shape.Hood - 2, cabinTo - cabinFrom - 4, 2, Interior);
            canvas.Line(cabinTo, shape.Hood - 1, cabinTo - 2, shape.Hood - 4, GlassDark);
        }

        Extras(canvas, car, shape, left, right, cabinFrom, cabinTo, paint, trim);

        // Bumpers and lamps.
        var bumper = era == Era.Classic ? trim : era == Era.Wedge ? Rgba.Hex(0x222226) : paint.Darker(0.15);
        canvas.Rect(right - 1, shape.Sill - 2, 2, 2, bumper);
        canvas.Rect(left, shape.Sill - 2, 2, 2, bumper);
        var lampY = (wedge ? shape.Hood + 3 : shape.Hood + 1);
        if (era == Era.Classic)
        {
            canvas.Rect(right - 1, lampY, 2, 2, Lamp);
        }
        else
        {
            canvas.Rect(right - (era == Era.Modern ? 3 : 1), lampY, era == Era.Modern ? 4 : 2, 1, Lamp);
        }

        canvas.Rect(left, shape.Hood + 1, 1, 2, TailLamp);

        // Wheels in cut-out arches.
        foreach (var axle in new[] { shape.RearAxle, shape.FrontAxle })
        {
            var cx = At(axle);
            var cy = Ground - shape.Wheel;
            canvas.Ellipse(cx, cy, shape.Wheel + 1, shape.Wheel + 1, Rgba.Clear);
            canvas.Ellipse(cx, cy, shape.Wheel, shape.Wheel, Tyre);
            var hub = Math.Max(1, shape.Wheel / 2);
            canvas.Ellipse(cx, cy, hub, hub, era == Era.Modern ? trim.Lighter(0.5) : trim);
            canvas[cx, cy] = Tyre.Lighter(0.2);
        }

        // Very dark cars get a grey outline, or they vanish on dark backgrounds.
        canvas.Outline((paint.R + paint.G + paint.B) / 3 < 60 ? Rgba.Hex(0x6a6e7a) : paint.Darker(0.7));
        return canvas;
    }

    public static PixelCanvas Top(CarModel car)
    {
        var shape = BodyShape.Of(car.Body);
        var paint = Parse(car.Paint);
        var canvas = new PixelCanvas(TopWidth, TopHeight);
        var (length, width) = (shape.TopLength, shape.TopWidth);
        var left = (TopWidth - length) / 2;
        var top = (TopHeight - width) / 2;
        var right = left + length - 1;
        var bottom = top + width - 1;

        // Wheels poke out at the corners, the body over them.
        var rear = left + 2;
        var front = right - 3;
        foreach (var x in new[] { rear, front })
        {
            canvas.Rect(x, top - 1, 3, 1, Tyre);
            canvas.Rect(x, bottom + 1, 3, 1, Tyre);
        }

        canvas.Rect(left, top, length, width, paint);
        foreach (var (x, y) in new[] { (left, top), (left, bottom), (right, top), (right, bottom) })
        {
            canvas[x, y] = Rgba.Clear;
        }

        // Cabin seen from above: windscreen, roof (or seats), rear window.
        int At(double share) => left + (int)Math.Round(share * (length - 1));
        var (cabinFrom, cabinTo) = (At(shape.CabinFrom), At(shape.CabinTo));
        if (shape.Roof > 0)
        {
            canvas.Rect(cabinTo - 1, top + 1, 2, width - 2, Glass);
            canvas.Rect(cabinFrom, top + 1, 1, width - 2, GlassDark);
            canvas.Rect(cabinFrom + 1, top + 1, Math.Max(1, cabinTo - cabinFrom - 2), width - 2, paint.Lighter(0.15));
        }
        else
        {
            canvas.Rect(cabinFrom, top + 1, Math.Max(2, cabinTo - cabinFrom), width - 2, Interior);
            canvas.Rect(cabinTo, top + 1, 1, width - 2, Glass);
        }

        if (car.Body == Body.Pickup)
        {
            canvas.Rect(left + 1, top + 1, cabinFrom - left - 1, width - 2, Interior.Lighter(0.15));
        }

        if (car.Body is Body.Muscle or Body.Rally)
        {
            canvas.Rect(left, (TopHeight / 2) - 1, length, 1, White);
            canvas.Rect(left, TopHeight / 2, length, 1, White);
        }

        canvas[right, top + 1] = Lamp;
        canvas[right, bottom - 1] = Lamp;
        canvas[left, top + 1] = TailLamp;
        canvas[left, bottom - 1] = TailLamp;
        return canvas;
    }

    // What makes a kind of car itself: stripes, decals, a wing, a bed, roof rails.
    private static void Extras(PixelCanvas canvas, CarModel car, BodyShape shape, int left, int right, int cabinFrom, int cabinTo, Rgba paint, Rgba trim)
    {
        switch (car.Body)
        {
            case Body.Muscle:
                canvas.Rect(left + 2, shape.Hood + 2, right - left - 4, 1, White);
                canvas.Rect(cabinTo + 1, shape.Hood - 1, 4, 1, Rgba.Hex(0x222226)); // bonnet scoop
                break;
            case Body.Rally:
                var door = (cabinFrom + cabinTo) / 2;
                canvas.Ellipse(door, shape.Hood + 3, 2, 2, White);
                canvas[door, shape.Hood + 3] = Rgba.Hex(0x222226);
                canvas.Rect(left + 1, shape.Sill - 3, right - left - 2, 1, White);
                canvas.Rect(left, shape.Sill + 1, 1, 2, Rgba.Hex(0x222226)); // mud flap
                break;
            case Body.Suv or Body.Van:
                canvas.Rect(cabinFrom + 2, shape.Roof - 1, cabinTo - cabinFrom - 4, 1, trim);
                break;
            case Body.Pickup:
                canvas.Rect(left + 1, shape.Hood, cabinFrom - left - 2, 2, Interior.Lighter(0.1));
                break;
            case Body.Supercar:
                canvas.Rect(cabinFrom - 3, shape.Hood + 2, 3, 2, Rgba.Hex(0x222226)); // side intake
                break;
            case Body.Hypercar:
                canvas.Rect(left + 2, shape.Hood - 4, 1, 4, Rgba.Hex(0x222226));
                canvas.Rect(left, shape.Hood - 5, 8, 1, paint.Darker(0.4)); // rear wing
                canvas.Rect(cabinFrom - 3, shape.Hood + 2, 4, 2, Rgba.Hex(0x222226));
                break;
        }
    }

    private static Rgba Parse(string hex) => Rgba.Hex(Convert.ToUInt32(hex.TrimStart('#'), 16));
}
