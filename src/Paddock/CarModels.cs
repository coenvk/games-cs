using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Paddock;

/// <summary>
/// Paddock's cars in 3D, low-poly, from the same description the sprites are drawn from (<see cref="BodyShape"/>, era,
/// paint and trim): a body with wheel arches, a narrower cabin with glass and pillars (or an open cockpit), four
/// wheels with hubs, bumpers, lamps, and each kind's extras (stripes, a bed, roof rails, a wing). Laid out in the side
/// view's pixels, <see cref="Pixel"/> metres each; the front is +x, the car is centred on x and z and stands on y = 0.
/// </summary>
public static class CarModels
{
    /// <summary>Metres per side-view pixel: 48 pixels for about 4.6 m.</summary>
    public const float Pixel = 4.6f / 48;

    // The side view's ground line, as the sprites have it.
    private const float Ground = 23;

    private static readonly Mesh Cube = MeshBuilder.Box(Vector3.One);
    private static readonly Mesh Tyre = MeshBuilder.Cylinder(1, 1, 10).Transform(Matrix4x4.CreateRotationX(MathF.PI / 2));
    private static readonly Mesh Disc = MeshBuilder.Cylinder(1, 1, 8).Transform(Matrix4x4.CreateRotationX(MathF.PI / 2));

    public static MeshModel Build(CarModel car)
    {
        ArgumentNullException.ThrowIfNull(car);
        var shape = BodyShape.Of(car.Body);
        var era = Era.Of(car.Year);
        var paint = Parse(car.Paint);
        var trim = Parse(car.Trim);
        var wedge = car.Body is Body.Supercar or Body.Hypercar || era == Era.Wedge;

        // Edges in side-view pixels, as the painter lays them out.
        var left = (64 - shape.Length) / 2;
        var (back, front) = (left, left + shape.Length);
        float At(double share) => left + (float)Math.Round(share * (shape.Length - 1));
        var (cabinFrom, cabinTo) = (At(shape.CabinFrom), At(shape.CabinTo) + 1);
        var middle = (back + front) / 2f;
        Vector2 V(float x, float y) => new((x - middle) * Pixel, (Ground - y) * Pixel);
        float Y(float y) => (Ground - y) * Pixel;
        float X(float x) => (x - middle) * Pixel;

        var width = Math.Clamp(shape.Length * Pixel * shape.TopWidth / shape.TopLength * 0.8f, 1.6f, 2.0f);
        var cabinWidth = width * 0.84f;

        var materials = new Materials(paint, trim, era);
        var model = new MeshModelBuilder();
        void Box(Material material, float x0, float x1, float y0, float y1, float depth, float z = 0) =>
            model.Add(Cube, Matrix4x4.CreateScale(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0), depth) * Matrix4x4.CreateTranslation((x0 + x1) / 2, (y0 + y1) / 2, z), material);

        // The body: wheel arches cut from the bottom, rounded top corners, a dropping nose on wedges.
        var round = era == Era.Classic ? 3 : 2;
        var bottom = shape.Sill + 1f;
        var wheelY = Ground - shape.Wheel;
        var archRadius = shape.Wheel + 1.3f;
        float[] axles = [At(shape.RearAxle) + 0.5f, At(shape.FrontAxle) + 0.5f];

        // The top of the body along its length: the bonnet line (dropping on a wedge's nose), rising into a wing over a
        // wheel where it would otherwise cut through the tyre, as on low supercars.
        var wing = wheelY - (shape.Wheel + 0.5f) - 0.4f;
        float TopAt(float x)
        {
            var top = wedge && x > cabinTo ? shape.Hood + ((x - cabinTo) * 3 / (front - cabinTo)) : shape.Hood;
            foreach (var axle in axles)
            {
                var reach = archRadius + 4;
                var t = (x - axle) / reach;
                if (top > wing && MathF.Abs(t) < 1)
                {
                    top = MathF.Min(top, wing + ((top - wing) * t * t));
                }
            }

            return top;
        }

        List<Vector2> body = [V(back, bottom)];
        foreach (var axle in axles)
        {
            body.AddRange(Arch(axle, wheelY, archRadius, bottom).Select(p => V(p.X, MathF.Max(p.Y, TopAt(p.X) + 0.6f))));
        }

        body.AddRange([V(front, bottom), V(front, TopAt(front) + round)]);
        for (var x = front - round; x >= back + round; x--)
        {
            body.Add(V(x, TopAt(x)));
        }

        body.Add(V(back, shape.Hood + round));
        model.Add(MeshBuilder.Extrude(body, width), Matrix4x4.Identity, materials.Paint);

        // The cabin, narrower than the body, with side windows set into it, a pillar between them, and glass front and back.
        if (shape.Roof > 0)
        {
            var rise = shape.Hood - shape.Roof;
            var room = Math.Max(1, ((int)(cabinTo - cabinFrom) - 5) / 2);
            var rearSlope = car.Body is Body.Hatchback or Body.Estate or Body.Suv or Body.Van or Body.Pickup ? 1 : Math.Min(room, (rise / 2) + 1);
            var frontSlope = Math.Min(room, wedge ? rise + 2 : (rise / 2) + 2);
            Vector2[] cabin = [V(cabinFrom, shape.Hood), V(cabinTo, shape.Hood), V(cabinTo - frontSlope, shape.Roof), V(cabinFrom + rearSlope, shape.Roof)];
            model.Add(MeshBuilder.Extrude(cabin, cabinWidth), Matrix4x4.Identity, materials.Paint);
            model.Add(MeshBuilder.Extrude(Polygon2.Inset(cabin, Pixel), cabinWidth + 0.03f), Matrix4x4.Identity, materials.Glass);
            Box(materials.Paint, X(((cabinFrom + cabinTo) / 2) - 0.6f), X(((cabinFrom + cabinTo) / 2) + 0.6f), Y(shape.Hood), Y(shape.Roof + 0.5f), cabinWidth + 0.05f);
            model.Add(Pane(cabin[1], cabin[2], outward: 1), Matrix4x4.Identity, materials.Glass);
            model.Add(Pane(cabin[3], cabin[0], outward: 1), Matrix4x4.Identity, materials.Glass);
            Mesh Pane(Vector2 from, Vector2 to, float outward)
            {
                // A thin sheet along the cabin's slope, a little proud of it and in from its ends and sides.
                var along = Vector2.Normalize(to - from);
                var normal = new Vector2(along.Y, -along.X) * outward;
                var inset = along * Pixel * 0.8f;
                Vector2[] sheet = [from + inset + (normal * 0.01f), to - inset + (normal * 0.01f), to - inset + (normal * 0.04f), from + inset + (normal * 0.04f)];
                return MeshBuilder.Extrude(sheet, cabinWidth * 0.86f);
            }
        }
        else
        {
            // An open car: seats in the cockpit and a raked windscreen.
            Box(materials.Interior, X(cabinFrom + 2), X(cabinTo - 2), Y(shape.Hood), Y(shape.Hood - 2), width * 0.8f);
            Vector2[] screen = [V(cabinTo, shape.Hood), V(cabinTo + 0.6f, shape.Hood), V(cabinTo - 1.4f, shape.Hood - 3.5f), V(cabinTo - 2, shape.Hood - 3.5f)];
            model.Add(MeshBuilder.Extrude(screen, width * 0.78f), Matrix4x4.Identity, materials.Glass);
        }

        // Wheels in their arches: tyre, and a hub standing just proud of it.
        var radius = (shape.Wheel + 0.5f) * Pixel;
        foreach (var axle in new[] { shape.RearAxle, shape.FrontAxle })
        {
            foreach (var side in new[] { -1, 1 })
            {
                // Ten-sided, so a flat side rests on the ground.
                var centre = new Vector3(X(At(axle) + 0.5f), radius * MathF.Cos(MathF.PI / 10), side * ((width / 2) - 0.11f));
                model.Add(Tyre, Matrix4x4.CreateScale(radius, radius, 0.26f) * Matrix4x4.CreateTranslation(centre), materials.Tyre);
                model.Add(Disc, Matrix4x4.CreateScale(radius * 0.55f, radius * 0.55f, 0.28f) * Matrix4x4.CreateTranslation(centre), materials.Hub);
            }
        }

        // Bumpers, lamps front and back.
        var (frontX, backX) = (X(front), X(back));
        Box(materials.Bumper, frontX - 0.06f, frontX + 0.05f, Y(bottom), Y(bottom - 2), width + 0.04f);
        Box(materials.Bumper, backX - 0.05f, backX + 0.06f, Y(bottom), Y(bottom - 2), width + 0.04f);
        var lampY = wedge ? shape.Hood + 3.5f : shape.Hood + 1.5f;
        var lampHeight = (era == Era.Classic ? 2 : 1.2f) * Pixel;
        var lampWidth = era == Era.Modern ? 0.42f : 0.28f;
        foreach (var side in new[] { -1, 1 })
        {
            var z = side * ((width / 2) - 0.3f);
            Box(materials.Lamp, frontX - 0.04f, frontX + 0.02f, Y(lampY) - (lampHeight / 2), Y(lampY) + (lampHeight / 2), lampWidth, z);
            Box(materials.TailLamp, backX - 0.02f, backX + 0.04f, Y(shape.Hood + 3), Y(shape.Hood + 1), 0.3f, z);
        }

        Extras(car.Body, shape, materials, Box, X, Y, back, front, cabinFrom, cabinTo, width, cabinWidth, bottom);
        return model.Build();
    }

    // The top of a wheel arch, left to right, from where its circle meets the body's bottom edge.
    private static IEnumerable<Vector2> Arch(float cx, float cy, float radius, float bottom)
    {
        var start = MathF.Asin(Math.Clamp((cy - bottom) / radius, -1, 1));
        const int Steps = 8;
        for (var i = 0; i <= Steps; i++)
        {
            var angle = MathF.PI - start - ((MathF.PI - (2 * start)) * i / Steps);
            yield return new Vector2(cx + (radius * MathF.Cos(angle)), cy - (radius * MathF.Sin(angle)));
        }
    }

    // What makes a kind of car itself, as on the sprites: stripes, a scoop, a bed, roof rails, intakes, a wing.
    private static void Extras(
        Body body, BodyShape shape, Materials m, Action<Material, float, float, float, float, float, float> box,
        Func<float, float> x, Func<float, float> y, float back, float front, float cabinFrom, float cabinTo, float width, float cabinWidth, float bottom)
    {
        var side = (width / 2) + 0.005f;
        switch (body)
        {
            case Body.Muscle:
                box(m.White, x(back + 2), x(front - 2), y(shape.Hood + 2.5f), y(shape.Hood + 1.5f), width + 0.01f, 0); // side stripe
                box(m.Black, x(cabinTo + 1), x(cabinTo + 5), y(shape.Hood), y(shape.Hood - 1), width * 0.3f, 0); // bonnet scoop
                break;
            case Body.Rally:
                box(m.White, x(back + 1), x(front - 1), y(bottom - 2), y(bottom - 3), width + 0.01f, 0); // sill stripe
                box(m.White, x(((cabinFrom + cabinTo) / 2) - 2), x(((cabinFrom + cabinTo) / 2) + 2), y(shape.Hood + 5), y(shape.Hood + 1), width + 0.01f, 0); // door roundel
                break;
            case Body.Suv or Body.Van:
                foreach (var z in new[] { -1f, 1f })
                {
                    box(m.Trim, x(cabinFrom + 2), x(cabinTo - 2), y(shape.Roof), y(shape.Roof - 1), 0.08f, z * ((cabinWidth / 2) - 0.1f));
                }

                break;
            case Body.Pickup:
                box(m.Interior, x(back + 1), x(cabinFrom - 1), y(shape.Hood), y(shape.Hood) + 0.01f, width - 0.2f, 0); // the bed
                break;
            case Body.Supercar or Body.Hypercar:
                foreach (var z in new[] { -1f, 1f })
                {
                    box(m.Black, x(cabinFrom - 3), x(cabinFrom + 0.5f), y(shape.Hood + 4), y(shape.Hood + 2), 0.02f, z * side); // side intake
                }

                if (body == Body.Hypercar)
                {
                    box(m.PaintDark, x(back), x(back + 8), y(shape.Hood - 4), y(shape.Hood - 5), width, 0); // rear wing
                    foreach (var z in new[] { -1f, 1f })
                    {
                        box(m.Black, x(back + 2), x(back + 3), y(shape.Hood), y(shape.Hood - 4), 0.06f, z * width * 0.3f);
                    }
                }

                break;
        }
    }

    private static Vector4 Parse(string hex)
    {
        var rgb = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1);
    }

    private static Vector4 Darker(Vector4 c, float by) => new(c.X * (1 - by), c.Y * (1 - by), c.Z * (1 - by), c.W);

    private static Vector4 Lighter(Vector4 c, float by) => Vector4.Lerp(c, Vector4.One, by) with { W = c.W };

    // The car's materials, its paint and trim and the parts every car has, as the sprites colour them.
    private sealed class Materials(Vector4 paint, Vector4 trim, Era era)
    {
        private static readonly Vector4 Dark = new(0x22 / 255f, 0x22 / 255f, 0x26 / 255f, 1);

        public Material Paint { get; } = new(paint, Shading.Flat);

        public Material PaintDark { get; } = new(Darker(paint, 0.4f), Shading.Flat);

        public Material Trim { get; } = new(trim, Shading.Flat);

        public Material Glass { get; } = new(new Vector4(0x9f / 255f, 0xd3 / 255f, 1, 1), Shading.Flat);

        public Material Tyre { get; } = new(new Vector4(0x1a / 255f, 0x1a / 255f, 0x1e / 255f, 1), Shading.Flat);

        public Material Hub { get; } = new(era == Era.Modern ? Lighter(trim, 0.5f) : trim, Shading.Flat);

        public Material Bumper { get; } = new(era == Era.Classic ? trim : era == Era.Wedge ? Dark : Darker(paint, 0.15f), Shading.Flat);

        public Material Lamp { get; } = new(new Vector4(1, 0xf6 / 255f, 0xa0 / 255f, 1), Shading.Unlit);

        public Material TailLamp { get; } = new(new Vector4(0xd8 / 255f, 0x20 / 255f, 0x20 / 255f, 1), Shading.Unlit);

        public Material Interior { get; } = new(new Vector4(0x2a / 255f, 0x22 / 255f, 0x26 / 255f, 1), Shading.Flat);

        public Material White { get; } = new(new Vector4(0xf2 / 255f, 0xee / 255f, 0xe4 / 255f, 1), Shading.Flat);

        public Material Black { get; } = new(Dark, Shading.Flat);
    }
}
