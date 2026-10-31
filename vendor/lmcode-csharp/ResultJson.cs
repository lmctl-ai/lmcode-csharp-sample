using System.Globalization;
using System.Text;

namespace LmCode;

/// Helpers for the lmcode return sink and its compact JSON wire value.
public static class ResultJson
{
    private const long Safe = 9_007_199_254_740_991;
    private static Error Invalid(string path, string reason) =>
        Error.Host($"lmcode return: invalid JSON value at {path}: {reason}");

    public static string FormatNumber(double value, string path = "")
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw Invalid(path, "nonfinite");
        if (value == 0) return "0";
        if (value == Math.Truncate(value) && Math.Abs(value) > Safe) throw Invalid(path, "unsafe_integer");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string ValidateNumberToken(string token, string path = "")
    {
        if (string.IsNullOrEmpty(token)) throw Invalid(path, "handle");
        int p = 0; if (token[p] == '-') p++;
        int intStart = p; if (p >= token.Length || token[p] < '0' || token[p] > '9') throw Invalid(path, "handle");
        if (token[p] == '0') { p++; if (p < token.Length && token[p] >= '0' && token[p] <= '9') throw Invalid(path, "handle"); }
        else { while (p < token.Length && token[p] >= '0' && token[p] <= '9') p++; }
        int intEnd = p, fracStart = -1, fracEnd = p;
        if (p < token.Length && token[p] == '.')
        {
            fracStart = ++p; while (p < token.Length && token[p] >= '0' && token[p] <= '9') p++;
            if (p == fracStart) throw Invalid(path, "handle");
            fracEnd = p;
        }
        long exponent = 0;
        if (p < token.Length && token[p] is 'e' or 'E')
        {
            p++; bool negative = false; if (p < token.Length && token[p] is '+' or '-') { negative = token[p++] == '-'; }
            int eStart = p; long e = 0; while (p < token.Length && token[p] >= '0' && token[p] <= '9') { e = Math.Min((long)token.Length + 17, e * 10 + token[p++] - '0'); }
            if (p == eStart) throw Invalid(path, "handle");
            exponent = negative ? -e : e;
        }
        if (p != token.Length) throw Invalid(path, "handle");
        string digits = (token[intStart..intEnd] + (fracStart < 0 ? "" : token[fracStart..fracEnd])).TrimStart('0');
        if (digits.Length == 0) return "0";
        int fractionalDigits = fracStart < 0 ? 0 : fracEnd - fracStart;
        long decimalPlaces = (long)fractionalDigits - exponent;
        bool integral = decimalPlaces <= 0 || (decimalPlaces < digits.Length && digits[(int)(digits.Length - decimalPlaces)..].All(c => c == '0'));
        if (integral)
        {
            string significant = digits;
            if (decimalPlaces > 0) significant = significant[..(significant.Length - (int)decimalPlaces)];
            else if (decimalPlaces < 0)
            {
                long zeros = -decimalPlaces;
                if (zeros > 16 || significant.Length + zeros > 16) throw Invalid(path, "unsafe_integer");
                significant += new string('0', (int)zeros);
            }
            significant = significant.TrimStart('0');
            if (significant.Length > 16 || (significant.Length == 16 && string.CompareOrdinal(significant, "9007199254740991") > 0)) throw Invalid(path, "unsafe_integer");
        }
        double parsed;
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) throw Invalid(path, "nonfinite");
        if (double.IsInfinity(parsed)) throw Invalid(path, integral ? "unsafe_integer" : "nonfinite");
        if (parsed == 0) throw Invalid(path, "underflow");
        if (parsed == Math.Truncate(parsed) && Math.Abs(parsed) > Safe) throw Invalid(path, "unsafe_integer");
        return FormatNumber(parsed, path);
    }

    public static string Quote(VmString value, string path = "")
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(value.Span); }
        catch (DecoderFallbackException) { throw Invalid(path, "utf8"); }
        return Quote(text, path);
    }

    public static string Quote(string value, string path = "")
    {
        var b = new StringBuilder("\"");
        try { _ = new UTF8Encoding(false, true).GetBytes(value); }
        catch (EncoderFallbackException) { throw Invalid(path, "utf8"); }
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c)) { b.Append(c); if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw Invalid(path, "utf8"); b.Append(value[i]); continue; }
            if (char.IsLowSurrogate(c)) throw Invalid(path, "utf8");
            b.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture), _ => c.ToString() });
        }
        return b.Append('"').ToString();
    }
}
