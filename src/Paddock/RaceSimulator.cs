namespace Paddock;

/// <summary>One car's run down a track: how long it took, and when it passed every metre.</summary>
/// <param name="Time">Seconds from the start to the finish.</param>
/// <param name="TimeAtMetre">The time at metre 0, 1, 2 ... up to the finish.</param>
public sealed record RaceRun(double Time, IReadOnlyList<double> TimeAtMetre)
{
    /// <summary>The distance run, in metres.</summary>
    public double Length => TimeAtMetre.Count - 1;

    /// <summary>How far the car had come at <paramref name="time"/>: 0 before the start, the full length after the finish.</summary>
    public double DistanceAt(double time)
    {
        if (time <= 0)
        {
            return 0;
        }

        if (time >= Time)
        {
            return Length;
        }

        // The last metre whose time is at or before `time`, then linear within the metre.
        int low = 0, high = TimeAtMetre.Count - 1;
        while (high - low > 1)
        {
            var mid = (low + high) / 2;
            if (TimeAtMetre[mid] <= time)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var span = TimeAtMetre[high] - TimeAtMetre[low];
        return low + (span > 0 ? (time - TimeAtMetre[low]) / span : 0);
    }
}

/// <summary>
/// Works out how a car runs a track, metre by metre, from its stats and the condition. Deterministic: only
/// arithmetic, <see cref="Math.Sqrt"/> and <see cref="Math.Log"/>, so every platform gets the same bits.
/// </summary>
public static class RaceSimulator
{
    /// <summary>Runs <paramref name="car"/> down <paramref name="track"/> from a standing start.</summary>
    public static RaceRun Run(Stats car, TrackDef track, Condition condition)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(track);
        var speeds = Speeds(car, track, condition);
        var times = new double[speeds.Length];
        for (var i = 1; i < speeds.Length; i++)
        {
            times[i] = times[i - 1] + (2 / (speeds[i - 1] + speeds[i]));
        }

        return new RaceRun(times[^1], times);
    }

    /// <summary>When the car first reaches <paramref name="kmh"/> on <paramref name="track"/> (for checking acceleration).</summary>
    public static double TimeTo(Stats car, TrackDef track, Condition condition, double kmh)
    {
        var speeds = Speeds(car, track, condition);
        var target = kmh / 3.6;
        var time = 0.0;
        for (var i = 1; i < speeds.Length; i++)
        {
            var step = 2 / (speeds[i - 1] + speeds[i]);
            if (speeds[i] >= target)
            {
                return time + (step * (target - speeds[i - 1]) / (speeds[i] - speeds[i - 1]));
            }

            time += step;
        }

        return double.PositiveInfinity;
    }

    // The speed (m/s) at every metre: as fast as the engine allows, never above the limits, braking in time for them.
    private static double[] Speeds(Stats car, TrackDef track, Condition condition)
    {
        var metres = Math.Max(1, (int)Math.Round(track.Length));
        var limit = new double[metres + 1];
        var gradient = new double[metres + 1];
        var rough = Balance.IsRough(condition) ? Balance.RoughGrip(car.Clearance) : 1;
        var grip = Balance.CorneringGrip(car.Handling, car.Tyres, condition) * rough;
        var engineTop = car.TopSpeedKmh / 3.6;
        var top = Balance.IsRough(condition) ? engineTop * Balance.RoughSpeed(car.Clearance) : engineTop;

        // Each metre takes the limit of the piece it lies in.
        var pieceEnd = 0.0;
        var piece = -1;
        for (var m = 0; m <= metres; m++)
        {
            while (m >= pieceEnd && piece < track.Pieces.Count - 1)
            {
                piece++;
                pieceEnd += track.Pieces[piece].Metres;
            }

            var p = track.Pieces[piece];
            limit[m] = p.Radius > 0 ? Math.Min(top, Math.Sqrt(grip * Balance.G * p.Radius)) : top;
            gradient[m] = p.Gradient;
        }

        // Engine pull falls off towards top speed: a(v) = A (1 - v / top), with A set so 0-100 km/h takes the stat's time.
        // A car that cannot reach 100 km/h takes the stat's time to reach 90% of its top speed instead.
        var pull = -(engineTop / car.ZeroTo100) * Math.Log(1 - (Math.Min(27.78, 0.9 * engineTop) / engineTop));
        var traction = Balance.TyreGrip(car.Tyres, condition) * rough * Balance.G * Balance.DriveTraction(car.Drive);
        var speeds = new double[metres + 1];
        for (var m = 0; m < metres; m++)
        {
            var v = speeds[m];
            var accel = Math.Min(pull * (1 - (v / engineTop)), traction) - (Balance.G * gradient[m]);
            var next = Math.Sqrt(Math.Max(0, (v * v) + (2 * accel)));
            speeds[m + 1] = Math.Max(0.5, Math.Min(next, limit[m + 1]));
        }

        // Braking: never arrive at a metre faster than the car can slow down for it.
        var braking = grip * Balance.G * Balance.Braking * Math.Sqrt(Math.Sqrt(Balance.ReferenceWeight / car.WeightKg));
        for (var m = metres - 1; m > 0; m--)
        {
            speeds[m] = Math.Min(speeds[m], Math.Sqrt((speeds[m + 1] * speeds[m + 1]) + (2 * braking)));
        }

        return speeds;
    }
}
