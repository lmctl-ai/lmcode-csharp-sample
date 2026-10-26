namespace LmCode.Stdlib;

/// <summary>Optional host testing helpers for byte output and numeric assertions.</summary>
public static class TestingCatalog
{
    public static void RegisterTesting(Registry r, Stream? sink = null)
    {
        r.BeginCompatibilityRegistration();
        try
        {
        Stream output = sink ?? Console.OpenStandardOutput();
        r.Function("print", ["string"], "void", (_, a) =>
        {
            output.Write(a[0].AsStr().Span);
            output.WriteByte((byte)'\n');
            return Value.None;
        });
        r.Function("assert_close", ["float", "float", "float"], "void", (_, a) =>
        {
            double left = a[0].AsFloat(), right = a[1].AsFloat(), tolerance = a[2].AsFloat();
            if (Math.Abs(left - right) <= tolerance) return Value.None;
            throw Error.Host($"assert_close failed: {Formatting.FormatG15(left)} vs {Formatting.FormatG15(right)} (tol {Formatting.FormatG15(tolerance)})");
        });
        }
        finally { r.EndCompatibilityRegistration(); }
    }
}
