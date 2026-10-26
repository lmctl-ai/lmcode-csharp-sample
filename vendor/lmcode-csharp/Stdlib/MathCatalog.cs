namespace LmCode.Stdlib;

/// <summary>Optional ordinary numeric host functions.</summary>
public static class MathCatalog
{
    public static long MathAbs(long value) => value < 0 ? unchecked(-value) : value;
    public static double MathAbs(double value) => Math.Abs(value);
    public static long MathMin(long a, long b) => Math.Min(a, b);
    public static long MathMax(long a, long b) => Math.Max(a, b);
    public static double MathMin(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : a <= b ? a : b;
    public static double MathMax(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : a >= b ? a : b;
    public static long MathFloor(long value) => value;
    public static long MathCeil(long value) => value;
    public static long MathRound(long value) => value;
    public static double MathFloor(double value) => Math.Floor(value);
    public static double MathCeil(double value) => Math.Ceiling(value);
    public static double MathRound(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    public static long MathPow(long baseValue, long exponent)
    {
        if (exponent < 0) throw Error.Host("math_pow: integer exponent must be nonnegative");
        ulong result = 1, factor = unchecked((ulong)baseValue), remaining = unchecked((ulong)exponent);
        while (remaining != 0)
        {
            if ((remaining & 1) != 0) result = unchecked(result * factor);
            remaining >>= 1;
            if (remaining != 0) factor = unchecked(factor * factor);
        }
        return unchecked((long)result);
    }
    public static double MathPow(double baseValue, double exponent)
    {
        if (exponent == 0.0 || baseValue == 1.0 || (baseValue == -1.0 && double.IsInfinity(exponent))) return 1.0;
        if (double.IsNaN(baseValue) || double.IsNaN(exponent)) return double.NaN;
        return Math.Pow(baseValue, exponent);
    }

    public static long MathSqrt(long value)
    {
        if (value < 0) throw Error.Host("math_sqrt: integer argument must be nonnegative");
        if (value < 2) return value;
        long low = 1, high = Math.Min(value, 3_037_000_499L), result = 1;
        while (low <= high)
        {
            long middle = low + (high - low) / 2;
            if (middle <= value / middle) { result = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return result;
    }
    public static double MathSqrt(double value) => Math.Sqrt(value);

    public static void RegisterMath(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
        r.Function("math_abs", ["int"], "int", static (_, a) => Value.Int(MathAbs(a[0].AsInt())));
        r.Function("math_abs", ["float"], "float", static (_, a) => Value.Float(MathAbs(a[0].AsFloat())));
        r.Function("math_min", ["int", "int"], "int", static (_, a) => Value.Int(MathMin(a[0].AsInt(), a[1].AsInt())));
        r.Function("math_min", ["float", "float"], "float", static (_, a) => Value.Float(MathMin(a[0].AsFloat(), a[1].AsFloat())));
        r.Function("math_max", ["int", "int"], "int", static (_, a) => Value.Int(MathMax(a[0].AsInt(), a[1].AsInt())));
        r.Function("math_max", ["float", "float"], "float", static (_, a) => Value.Float(MathMax(a[0].AsFloat(), a[1].AsFloat())));
        r.Function("math_floor", ["int"], "int", static (_, a) => Value.Int(MathFloor(a[0].AsInt())));
        r.Function("math_floor", ["float"], "float", static (_, a) => Value.Float(MathFloor(a[0].AsFloat())));
        r.Function("math_ceil", ["int"], "int", static (_, a) => Value.Int(MathCeil(a[0].AsInt())));
        r.Function("math_ceil", ["float"], "float", static (_, a) => Value.Float(MathCeil(a[0].AsFloat())));
        r.Function("math_round", ["int"], "int", static (_, a) => Value.Int(MathRound(a[0].AsInt())));
        r.Function("math_round", ["float"], "float", static (_, a) => Value.Float(MathRound(a[0].AsFloat())));
        r.Function("math_pow", ["int", "int"], "int", static (_, a) => Value.Int(MathPow(a[0].AsInt(), a[1].AsInt())));
        r.Function("math_pow", ["float", "float"], "float", static (_, a) => Value.Float(MathPow(a[0].AsFloat(), a[1].AsFloat())));
        r.Function("math_sqrt", ["int"], "int", static (_, a) => Value.Int(MathSqrt(a[0].AsInt())));
        r.Function("math_sqrt", ["float"], "float", static (_, a) => Value.Float(MathSqrt(a[0].AsFloat())));
        }
        finally { r.EndCompatibilityRegistration(); }
    }
}
