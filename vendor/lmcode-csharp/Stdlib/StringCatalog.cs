using System.Globalization;

namespace LmCode.Stdlib;

/// <summary>Optional host functions; merge the matching fragment into the host schema.</summary>
public static class StringCatalog
{
    private const int MaxBytes = 64 * 1024 * 1024;
    public static long StrLen(VmString s) => s.Length;
    public static bool StrStartsWith(VmString s, VmString prefix) => s.Span.StartsWith(prefix.Span);
    public static bool StrEndsWith(VmString s, VmString suffix) => s.Span.EndsWith(suffix.Span);
    public static bool StrContains(VmString s, VmString needle) => s.Span.IndexOf(needle.Span) >= 0;
    public static VmString StrLower(VmString s) => MapAscii(s, false);
    public static VmString StrUpper(VmString s) => MapAscii(s, true);

    public static VmString StrSubstr(VmString s, long start, long count)
    {
        if (start < 0) start = 0;
        if (count <= 0 || start >= s.Length) return new VmString([]);
        int length = (int)Math.Min(count, s.Length - start);
        return new VmString(s.Span.Slice((int)start, length));
    }

    public static long StrFind(VmString s, VmString needle)
    {
        int index = s.Span.IndexOf(needle.Span);
        return index < 0 ? -1 : index;
    }

    public static VmString StrReplace(VmString s, VmString from, VmString to)
    {
        if (from.Length == 0)
        {
            if (s.Length > MaxBytes) throw Error.Host("str_replace: result must be at most 67108864 bytes");
            return s;
        }
        long resultSize = 0;
        int at = 0;
        while (true)
        {
            int found = s.Span[at..].IndexOf(from.Span);
            if (found < 0) { AddReplaceSize(ref resultSize, s.Length - at); break; }
            found += at;
            AddReplaceSize(ref resultSize, found - at);
            AddReplaceSize(ref resultSize, to.Length);
            at = found + from.Length;
        }
        var result = new byte[(int)resultSize];
        at = 0;
        int output = 0;
        while (true)
        {
            int found = s.Span[at..].IndexOf(from.Span);
            if (found < 0) { s.Span[at..].CopyTo(result.AsSpan(output)); break; }
            found += at;
            s.Span[at..found].CopyTo(result.AsSpan(output)); output += found - at;
            to.Span.CopyTo(result.AsSpan(output)); output += to.Length;
            at = found + from.Length;
        }
        return VmString.FromOwned(result);
    }

    public static VmString StrTrim(VmString s)
    {
        int start = 0, end = s.Length;
        while (start < end && IsTrimByte(s.Span[start])) ++start;
        while (end > start && IsTrimByte(s.Span[end - 1])) --end;
        return new VmString(s.Span[start..end]);
    }

    public static VmString StrRepeat(VmString s, long count)
    {
        if (s.Length == 0 && count >= 0) return new VmString([]);
        if (count < 0 || (s.Length > 0 && (ulong)count > (ulong)(MaxBytes / s.Length)))
            throw Error.Host("str_repeat: count must be nonnegative and result at most 67108864 bytes");
        int resultLength = checked((int)(s.Length * count));
        var result = new byte[resultLength];
        for (long i = 0; i < count; ++i) s.Span.CopyTo(result.AsSpan(checked((int)(i * s.Length))));
        return VmString.FromOwned(result);
    }

    public static bool StrIsInt(VmString s) => TryParseInt(s.Span, out _);
    public static long StrToInt(VmString s) => TryParseInt(s.Span, out long value) ? value : 0;
    public static bool StrIsFloat(VmString s) => TryParseFloat(s.Span, out _);
    public static double StrToFloat(VmString s) => TryParseFloat(s.Span, out double value) ? value : 0.0;

    public static bool StrMatch(VmString text, VmString pattern)
    {
        var work = new GlobWork();
        int t = 0, p = 0, starPattern = -1, starText = 0;
        while (t < text.Length)
        {
            work.Step();
            int next;
            if (p < pattern.Length && pattern.Span[p] == (byte)'*') { starPattern = p++; starText = t; }
            else if (p < pattern.Length && GlobToken(pattern.Span, p, text.Span[t], out next, ref work)) { p = next; ++t; }
            else if (starPattern >= 0) { p = starPattern + 1; t = ++starText; }
            else return false;
        }
        while (p < pattern.Length && pattern.Span[p] == (byte)'*') { work.Step(); ++p; }
        return p == pattern.Length;
    }

    public static void RegisterString(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
        r.Function("str_starts_with", ["string", "string"], "bool", static (_, a) => Value.Bool(StrStartsWith(a[0].AsStr(), a[1].AsStr())));
        r.Function("str_ends_with", ["string", "string"], "bool", static (_, a) => Value.Bool(StrEndsWith(a[0].AsStr(), a[1].AsStr())));
        r.Function("str_contains", ["string", "string"], "bool", static (_, a) => Value.Bool(StrContains(a[0].AsStr(), a[1].AsStr())));
        r.Function("str_lower", ["string"], "string", static (_, a) => Value.Str(StrLower(a[0].AsStr())));
        r.Function("str_upper", ["string"], "string", static (_, a) => Value.Str(StrUpper(a[0].AsStr())));
        r.Function("str_substr", ["string", "int", "int"], "string", static (_, a) => Value.Str(StrSubstr(a[0].AsStr(), a[1].AsInt(), a[2].AsInt())));
        r.Function("str_len", ["string"], "int", static (_, a) => Value.Int(StrLen(a[0].AsStr())));
        r.Function("str_match", ["string", "string"], "bool", static (_, a) => Value.Bool(StrMatch(a[0].AsStr(), a[1].AsStr())));
        r.Function("str_find", ["string", "string"], "int", static (_, a) => Value.Int(StrFind(a[0].AsStr(), a[1].AsStr())));
        r.Function("str_replace", ["string", "string", "string"], "string", static (_, a) => Value.Str(StrReplace(a[0].AsStr(), a[1].AsStr(), a[2].AsStr())));
        r.Function("str_trim", ["string"], "string", static (_, a) => Value.Str(StrTrim(a[0].AsStr())));
        r.Function("str_repeat", ["string", "int"], "string", static (_, a) => Value.Str(StrRepeat(a[0].AsStr(), a[1].AsInt())));
        r.Function("str_to_int", ["string"], "int", static (_, a) => Value.Int(StrToInt(a[0].AsStr())));
        r.Function("str_is_int", ["string"], "bool", static (_, a) => Value.Bool(StrIsInt(a[0].AsStr())));
        r.Function("str_to_float", ["string"], "float", static (_, a) => Value.Float(StrToFloat(a[0].AsStr())));
        r.Function("str_is_float", ["string"], "bool", static (_, a) => Value.Bool(StrIsFloat(a[0].AsStr())));
        }
        finally { r.EndCompatibilityRegistration(); }
    }

    private static VmString MapAscii(VmString s, bool upper)
    {
        var result = s.Span.ToArray();
        for (int i = 0; i < result.Length; ++i)
            if (upper ? result[i] is >= (byte)'a' and <= (byte)'z' : result[i] is >= (byte)'A' and <= (byte)'Z')
                result[i] = (byte)(result[i] + (upper ? 'A' - 'a' : 'a' - 'A'));
        return VmString.FromOwned(result);
    }

    private static bool IsTrimByte(byte value) => value == (byte)' ' || value is >= 9 and <= 13;

    private static void AddReplaceSize(ref long size, int amount)
    {
        size += amount;
        if (size > MaxBytes) throw Error.Host("str_replace: result must be at most 67108864 bytes");
    }

    private static bool TryParseInt(ReadOnlySpan<byte> bytes, out long value)
    {
        value = 0;
        if (bytes.Length == 0) return false;
        int start = bytes[0] == (byte)'+' ? 1 : 0;
        if (start == 1 && (bytes.Length == 1 || bytes[1] is (byte)'+' or (byte)'-')) return false;
        bool negative = start == 0 && bytes[0] == (byte)'-';
        if (negative) start = 1;
        if (start == bytes.Length) return false;
        ulong limit = negative ? 1UL << 63 : long.MaxValue;
        ulong magnitude = 0;
        for (int i = start; i < bytes.Length; ++i)
        {
            if (bytes[i] is < (byte)'0' or > (byte)'9') return false;
            uint digit = (uint)(bytes[i] - '0');
            if (magnitude > (limit - digit) / 10) return false;
            magnitude = magnitude * 10 + digit;
        }
        value = negative ? magnitude == 1UL << 63 ? long.MinValue : -(long)magnitude : (long)magnitude;
        return true;
    }

    private static bool TryParseFloat(ReadOnlySpan<byte> bytes, out double value)
    {
        value = 0;
        int i = 0;
        if (i < bytes.Length && bytes[i] is (byte)'+' or (byte)'-') ++i;
        bool digits = false, nonzero = false;
        while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') { digits = true; nonzero |= bytes[i] != (byte)'0'; ++i; }
        if (i < bytes.Length && bytes[i] == (byte)'.')
        {
            ++i;
            while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') { digits = true; nonzero |= bytes[i] != (byte)'0'; ++i; }
        }
        if (!digits) return false;
        if (i < bytes.Length && bytes[i] is (byte)'e' or (byte)'E')
        {
            ++i;
            if (i < bytes.Length && bytes[i] is (byte)'+' or (byte)'-') ++i;
            int exponent = i;
            while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') ++i;
            if (i == exponent) return false;
        }
        if (i != bytes.Length) return false;
        bool parsed = double.TryParse(bytes, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);
        return parsed && (value != 0.0 || !nonzero) && double.IsFinite(value);
    }

    private struct GlobWork
    {
        private long steps;
        public void Step()
        {
            if (steps == 1L << 27) throw Error.Host("str_match: pattern exceeds the 2^27 work limit");
            ++steps;
        }
    }

    private static bool GlobToken(ReadOnlySpan<byte> pattern, int i, byte c, out int next, ref GlobWork work)
    {
        if (pattern[i] == (byte)'\\' && i + 1 < pattern.Length) { next = i + 2; return pattern[i + 1] == c; }
        if (pattern[i] == (byte)'?') { next = i + 1; return true; }
        if (pattern[i] == (byte)'[')
        {
            int q = i + 1; bool negate = q < pattern.Length && pattern[q] == (byte)'!'; if (negate) ++q;
            int first = q; bool hit = false;
            while (q < pattern.Length && (pattern[q] != (byte)']' || q == first))
            {
                work.Step();
                byte lo = GlobByte(pattern, ref q);
                if (q + 1 < pattern.Length && pattern[q] == (byte)'-' && pattern[q + 1] != (byte)']')
                {
                    ++q; byte hi = GlobByte(pattern, ref q);
                    if (lo <= hi && c >= lo && c <= hi) hit = true;
                }
                else if (lo == c) hit = true;
            }
            if (q < pattern.Length) { next = q + 1; return hit != negate; }
        }
        next = i + 1; return pattern[i] == c;
    }

    private static byte GlobByte(ReadOnlySpan<byte> pattern, ref int index)
    {
        if (pattern[index] == (byte)'\\' && index + 1 < pattern.Length) ++index;
        return pattern[index++];
    }
}
