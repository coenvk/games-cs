namespace Paddock;

/// <summary>
/// Maths the race needs, made from IEEE arithmetic only, so every platform computes the same bits.
/// <see cref="Math.Sqrt"/> is exactly rounded by IEEE 754 and safe everywhere; <see cref="Math.Log"/> comes from each
/// platform's maths library, and those may differ in the last bit.
/// </summary>
public static class Arith
{
    private const double Ln2 = 0.6931471805599453;
    private const double Sqrt2 = 1.4142135623730951;

    /// <summary>The natural logarithm: the exponent times ln 2, plus ln of the mantissa by the atanh series.</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0)
        {
            return double.NaN;
        }

        if (x == 0)
        {
            return double.NegativeInfinity;
        }

        if (double.IsPositiveInfinity(x))
        {
            return x;
        }

        // Subnormal numbers have no implicit leading bit; scale them into the normal range first.
        var shift = 0;
        if (x < 2.2250738585072014e-308)
        {
            x *= 18014398509481984.0; // 2^54
            shift = -54;
        }

        var bits = BitConverter.DoubleToInt64Bits(x);
        var exponent = (int)((bits >> 52) & 0x7FF) - 1023 + shift;
        var mantissa = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
        if (mantissa > Sqrt2)
        {
            mantissa /= 2;
            exponent++;
        }

        // ln m = 2 atanh(s) = 2 (s + s³/3 + s⁵/5 + ...), with |s| ≤ 0.172 the terms vanish quickly.
        var s = (mantissa - 1) / (mantissa + 1);
        var s2 = s * s;
        var term = s;
        var sum = 0.0;
        for (var k = 1; k <= 31; k += 2)
        {
            sum += term / k;
            term *= s2;
        }

        return (exponent * Ln2) + (2 * sum);
    }
}
