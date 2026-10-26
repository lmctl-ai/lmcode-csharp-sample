namespace LmCode.Stdlib;

/// <summary>Strict byte encoders matching the v3 standard catalog.</summary>
public static class EncodingCatalog
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private const string Hex = "0123456789abcdef";

    public static VmString Base64Encode(VmString input)
    {
        int groups = CheckedBase64Groups(input.Length);
        byte[] output = new byte[checked(groups * 4)];
        int at = 0;
        for (int i = 0; i < input.Length; i += 3)
        {
            int remaining = input.Length - i;
            int a = input.Span[i];
            int b = remaining > 1 ? input.Span[i + 1] : 0;
            int c = remaining > 2 ? input.Span[i + 2] : 0;
            output[at++] = (byte)Alphabet[a >> 2];
            output[at++] = (byte)Alphabet[((a & 3) << 4) | (b >> 4)];
            output[at++] = remaining > 1 ? (byte)Alphabet[((b & 15) << 2) | (c >> 6)] : (byte)'=';
            output[at++] = remaining > 2 ? (byte)Alphabet[c & 63] : (byte)'=';
        }
        return VmString.FromOwned(output);
    }

    public static VmString Base64Decode(VmString input)
    {
        if ((input.Length & 3) != 0) throw Error.Host("str_base64_decode: invalid encoding");
        byte[] output = new byte[checked((input.Length / 4) * 3)];
        int at = 0;
        for (int i = 0; i < input.Length; i += 4)
        {
            bool last = i + 4 == input.Length;
            int a = Base64Value(input.Span[i]), b = Base64Value(input.Span[i + 1]);
            if (a < 0 || b < 0) InvalidBase64();
            byte c2 = input.Span[i + 2], c3 = input.Span[i + 3];
            if (c2 == (byte)'=')
            {
                if (!last || c3 != (byte)'=' || (b & 15) != 0) InvalidBase64();
                output[at++] = (byte)((a << 2) | (b >> 4));
                continue;
            }
            int c = Base64Value(c2);
            if (c < 0) InvalidBase64();
            output[at++] = (byte)((a << 2) | (b >> 4));
            output[at++] = (byte)((b << 4) | (c >> 2));
            if (c3 == (byte)'=')
            {
                if (!last || (c & 3) != 0) InvalidBase64();
                continue;
            }
            int d = Base64Value(c3);
            if (d < 0) InvalidBase64();
            output[at++] = (byte)((c << 6) | d);
        }
        return VmString.FromOwned(output.AsSpan(0, at).ToArray());
    }

    public static VmString HexEncode(VmString input)
    {
        CheckedHexSize(input.Length);
        byte[] output = new byte[input.Length * 2];
        int at = 0;
        foreach (byte value in input.Span)
        {
            output[at++] = (byte)Hex[value >> 4];
            output[at++] = (byte)Hex[value & 15];
        }
        return VmString.FromOwned(output);
    }

    public static VmString HexDecode(VmString input)
    {
        if ((input.Length & 1) != 0) InvalidHex();
        byte[] output = new byte[input.Length / 2];
        for (int i = 0; i < input.Length; i += 2)
        {
            int high = HexValue(input.Span[i]), low = HexValue(input.Span[i + 1]);
            if (high < 0 || low < 0) InvalidHex();
            output[i / 2] = (byte)((high << 4) | low);
        }
        return new VmString(output);
    }

    public static void RegisterEncoding(Registry registry)
    {
        registry.BeginCompatibilityRegistration();
        try
        {
            registry.Function("str_base64_encode", ["string"], "string", (_, a) => Value.Str(Base64Encode(a[0].AsStr())));
            registry.Function("str_base64_decode", ["string"], "string", (_, a) => Value.Str(Base64Decode(a[0].AsStr())));
            registry.Function("str_hex_encode", ["string"], "string", (_, a) => Value.Str(HexEncode(a[0].AsStr())));
            registry.Function("str_hex_decode", ["string"], "string", (_, a) => Value.Str(HexDecode(a[0].AsStr())));
        }
        finally { registry.EndCompatibilityRegistration(); }
    }

    private static int Base64Value(byte value)
    {
        if (value is >= (byte)'A' and <= (byte)'Z') return value - 'A';
        if (value is >= (byte)'a' and <= (byte)'z') return value - 'a' + 26;
        if (value is >= (byte)'0' and <= (byte)'9') return value - '0' + 52;
        return value switch { (byte)'+' => 62, (byte)'/' => 63, _ => -1 };
    }

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1
    };

    private static void InvalidBase64() => throw Error.Host("str_base64_decode: invalid encoding");
    private static void InvalidHex() => throw Error.Host("str_hex_decode: invalid encoding");

    internal static int CheckedBase64Groups(int inputLength)
    {
        long groups = inputLength / 3L + (inputLength % 3 == 0 ? 0 : 1);
        if (groups > Array.MaxLength / 4L) throw Error.Host("str_base64_encode: size overflow");
        return (int)groups;
    }

    internal static void CheckedHexSize(int inputLength)
    {
        if (inputLength > Array.MaxLength / 2) throw Error.Host("str_hex_encode: size overflow");
    }
}
