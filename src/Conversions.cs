using System.Globalization;
using LmCode;
using LmCode.Stdlib;

namespace Sample;

static class Conversions
{
    public static void Register(Registry r, Host host)
    {
        r.Function("host_int", ["int"], "int", (_, a) => Value.Int(a[0].AsInt()));
        r.Function("host_float", ["float"], "float", (_, a) => Value.Float(a[0].AsFloat()));
        r.Function("host_bool", ["bool"], "bool", (_, a) => Value.Bool(a[0].AsBool()));
        r.Function("host_bytes", ["string"], "string", (_, a) => Value.Str(a[0].AsStr()));
        r.Function("host_hex", ["string"], "string", (_, a) => Value.Str(Hex(a[0].AsStr())));
        r.Function("host_float_case", ["string"], "float", (_, a) => a[0].AsStr().ToString() switch
        {
            "-0" => Value.Float(-0.0), "nan" => Value.Float(double.NaN), "inf" => Value.Float(double.PositiveInfinity),
            "-inf" => Value.Float(double.NegativeInfinity), "large" => Value.Float(1e21), "subnormal" => Value.Float(double.Epsilon),
            _ => throw Error.Host("host_float_case: unknown case")
        });
        r.Function("host_echo_money", ["Money"], "Money", (_, a) => a[0]);
        r.Function("host_same", ["Money", "Money"], "bool", (_, a) => Value.Bool(a[0].AsObj().Equals(a[1].AsObj())));
        r.Function("host_null", [], "Money", (_, _) => Value.Object("Money", new ObjRef(-1)));
        r.Function("host_is_null", ["Money"], "bool", (_, a) => Value.Bool(a[0].AsObj().Index < 0));
        r.Function("host_wrong", [], "Money", (_, _) => Value.Object("Items", new ObjRef(-1)));
        r.Function("host_void", [], "void", (_, _) => Value.None);
        r.Function("host_sum", Enumerable.Repeat("int", 255).ToArray(), "int", (_, a) =>
        {
            long sum = 0; foreach (Value value in a) sum = unchecked(sum + value.AsInt()); return Value.Int(sum);
        });
        r.Function("host_long_" + new string('x', 128), ["int"], "int", (_, a) => Value.Int(a[0].AsInt()));

        r.Function("host_str_list", ["StrList"], "StrList", (c, a) =>
        {
            var values = new List<VmString>(c.Get<StrList>(a[0].AsObj()).Values) { VmString.FromString("host") };
            return Value.Object("StrList", c.Insert("StrList", new StrList(values)));
        });
        r.Function("host_int_list", ["IntList"], "IntList", (c, a) =>
        {
            var values = new List<long>(c.Get<IntList>(a[0].AsObj()).Values) { 9 };
            return Value.Object("IntList", c.Insert("IntList", new IntList(values)));
        });
        r.Function("host_float_list", ["FloatList"], "FloatList", (c, a) =>
        {
            var values = new List<double>(c.Get<FloatList>(a[0].AsObj()).Values) { 0.5 };
            return Value.Object("FloatList", c.Insert("FloatList", new FloatList(values)));
        });
        r.Function("host_str_map", ["StrMap"], "StrMap", (c, a) =>
        {
            var source = c.Get<StrMap>(a[0].AsObj());
            return Value.Object("StrMap", c.Insert("StrMap", new StrMap(source.Entries.Append((VmString.FromString("host"), VmString.FromString("yes"))))));
        });
    }

    private static VmString Hex(VmString value)
    {
        const string digits = "0123456789abcdef"; byte[] output = new byte[value.Length * 2]; int i = 0;
        foreach (byte b in value.Span) { output[i++] = (byte)digits[b >> 4]; output[i++] = (byte)digits[b & 15]; }
        return new VmString(output);
    }
}
