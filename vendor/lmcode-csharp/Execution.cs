using System.Globalization;

namespace LmCode;

public static class Formatting
{
    public static string FormatG15(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        if (x == 0) return double.IsNegative(x) ? "-0" : "0";
        string sci = x.ToString("0.00000000000000e+00", CultureInfo.InvariantCulture);
        int split = sci.IndexOf('e'); string mant = sci[..split]; int exp = int.Parse(sci[(split + 1)..], CultureInfo.InvariantCulture);
        string Trim(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
        if (exp < -4 || exp >= 15) return $"{Trim(mant)}e{(exp < 0 ? "-" : "+")}{Math.Abs(exp):00}";
        int decimals = Math.Max(0, 14 - exp);
        return Trim(x.ToString($"F{decimals}", CultureInfo.InvariantCulture));
    }
}

public sealed partial class Program
{
    private static Value Pop(List<Value> s) { if (s.Count == 0) throw new Error("RUN_ERROR", "stack underflow"); Value v = s[^1]; s.RemoveAt(s.Count - 1); return v; }
    private static void BinI(List<Value> s, Func<long, long, long> f) { var (a, b) = IntPair(s); s.Add(Value.Int(f(a, b))); }
    private static void BinF(List<Value> s, Func<double, double, double> f) { var (a, b) = FloatPair(s); s.Add(Value.Float(f(a, b))); }
    private static long Div(long a, long b) { if (b == 0) throw new Error("RUN_ERROR", "int division by zero"); if (a == long.MinValue && b == -1) return long.MinValue; return a / b; }
    private static long Mod(long a, long b) { if (b == 0) throw new Error("RUN_ERROR", "int modulo by zero"); return a == long.MinValue && b == -1 ? 0 : a % b; }
    private static double Min(double a, double b) => double.IsNaN(a) ? a : double.IsNaN(b) ? b : a < b ? a : b < a ? b : a;
    private static double Max(double a, double b) => double.IsNaN(a) ? a : double.IsNaN(b) ? b : a > b ? a : b > a ? b : a;
    private static bool Equal(Value a, Value b) { if (a.Kind != b.Kind) throw new Error("RUN_ERROR", $"cannot compare {a.KindName} with {b.KindName}"); return a.Kind switch { ValueKind.None => true, ValueKind.Int => a.AsInt() == b.AsInt(), ValueKind.Float => a.AsFloat() == b.AsFloat(), ValueKind.Str => a.AsStr().Equals(b.AsStr()), ValueKind.Bool => a.AsBool() == b.AsBool(), ValueKind.Obj => a.AsObj().Equals(b.AsObj()), _ => false }; }

    public void Run(Context context, Registry registry)
    {
        Registry owner = boundRegistry ?? registry;
        owner.EnterRun(context);
        try
        {
        var stack = new List<Value>(); var values = new Value[locals.Count]; var reader = new Reader(code);
        while (!reader.Done)
        {
            int faultPc = reader.Pos;
            try
            {
                Op op = (Op)reader.U8();
                switch (op)
                {
                    case Op.PUSH_INT: stack.Add(Value.Int(reader.I64())); break;
                    case Op.PUSH_FLOAT: stack.Add(Value.Float(reader.F64())); break;
                    case Op.PUSH_STR: stack.Add(Value.Str(consts[checked((int)reader.U32())])); break;
                    case Op.PUSH_BOOL: stack.Add(Value.Bool(reader.U8() != 0)); break;
                    case Op.LOAD_LOCAL: { int slot = reader.U16(); Value v = values[slot]; if (v.Kind == ValueKind.None) throw new Error("RUN_ERROR", $"local slot {slot} read before assignment"); stack.Add(v); break; }
                    case Op.STORE_LOCAL: values[reader.U16()] = Pop(stack); break;
                    case Op.LOAD_CTX: { string name = contexts[reader.U16()]; ObjRef obj = context.Lookup(name) ?? throw new Error("RUN_ERROR", $"context variable '{name}' was not provided by the host for this run"); stack.Add(Value.Object(registry.ContextType(name)!, obj)); break; }
                    case Op.CALL_FN: { Binding b = imports[reader.U16()]; var a = Args(stack, reader.U8()); Value v = b.Function!(context, a); if (v.Kind != ValueKind.None) stack.Add(v); break; }
                    case Op.CALL_METHOD: { Binding b = imports[reader.U16()]; var a = Args(stack, reader.U8()); Value self = Pop(stack); if (self.Kind != ValueKind.Obj || self.TypeName != b.TypeName) throw new Error("RUN_ERROR", $"method {b.TypeName}.{b.Name} called on a {(self.Kind == ValueKind.Obj ? self.TypeName : self.KindName)}"); ObjRef obj = self.AsObj(); Value v = b.Method!(context, obj, a); if (v.Kind != ValueKind.None) stack.Add(v); break; }
                    case Op.EQ: { Value b = Pop(stack), a = Pop(stack); stack.Add(Value.Bool(Equal(a, b))); break; }
                    case Op.NE: { Value b = Pop(stack), a = Pop(stack); stack.Add(Value.Bool(!Equal(a, b))); break; }
                    case Op.NOT: stack.Add(Value.Bool(!Unary(stack, ValueKind.Bool, "! applied to a").AsBool())); break;
                    case Op.JUMP: { int delta = reader.I32(); reader.Pos = checked(reader.Pos + delta); break; }
                    case Op.JUMP_IF_FALSE: { int d = reader.I32(); if (!Condition(stack)) reader.Pos = checked(reader.Pos + d); break; }
                    case Op.JUMP_IF_TRUE: { int d = reader.I32(); if (Condition(stack)) reader.Pos = checked(reader.Pos + d); break; }
                    case Op.DUP: if (stack.Count == 0) throw new Error("RUN_ERROR", "stack underflow"); stack.Add(stack[^1]); break;
                    case Op.POP: _ = Pop(stack); break;
                    case Op.HALT: return;
                    case Op.ADD_I: BinI(stack, (a, b) => unchecked(a + b)); break;
                    case Op.SUB_I: BinI(stack, (a, b) => unchecked(a - b)); break;
                    case Op.MUL_I: BinI(stack, (a, b) => unchecked(a * b)); break;
                    case Op.DIV_I: BinI(stack, Div); break;
                    case Op.MOD_I: BinI(stack, Mod); break;
                    case Op.ADD_F: BinF(stack, (a, b) => a + b); break;
                    case Op.SUB_F: BinF(stack, (a, b) => a - b); break;
                    case Op.MUL_F: BinF(stack, (a, b) => a * b); break;
                    case Op.DIV_F: BinF(stack, (a, b) => a / b); break;
                    case Op.NEG_I: stack.Add(Value.Int(unchecked(-Unary(stack, ValueKind.Int, "int negation applied to").AsInt()))); break;
                    case Op.NEG_F: stack.Add(Value.Float(-Unary(stack, ValueKind.Float, "float negation applied to").AsFloat())); break;
                    case Op.I2F: stack.Add(Value.Float(Unary(stack, ValueKind.Int, "float() applied to").AsInt())); break;
                    case Op.LT_I: CmpI(stack, (a, b) => a < b); break;
                    case Op.LE_I: CmpI(stack, (a, b) => a <= b); break;
                    case Op.GT_I: CmpI(stack, (a, b) => a > b); break;
                    case Op.GE_I: CmpI(stack, (a, b) => a >= b); break;
                    case Op.LT_F: CmpF(stack, (a, b) => a < b); break;
                    case Op.LE_F: CmpF(stack, (a, b) => a <= b); break;
                    case Op.GT_F: CmpF(stack, (a, b) => a > b); break;
                    case Op.GE_F: CmpF(stack, (a, b) => a >= b); break;
                    case Op.CONCAT: { Value bv = Pop(stack), av = Pop(stack); if (av.Kind != ValueKind.Str || bv.Kind != ValueKind.Str) throw new Error("RUN_ERROR", $"string + applied to {av.KindName} and {bv.KindName}"); VmString b = bv.AsStr(), a = av.AsStr(); stack.Add(Value.Str(VmString.Concat(a, b))); break; }
                    case Op.I2S: stack.Add(Value.Str(VmString.FromString(Unary(stack, ValueKind.Int, "str(int) applied to").AsInt().ToString(CultureInfo.InvariantCulture)))); break;
                    case Op.F2S: stack.Add(Value.Str(VmString.FromString(Formatting.FormatG15(Unary(stack, ValueKind.Float, "str(float) applied to").AsFloat())))); break;
                    case Op.B2S: stack.Add(Value.Str(VmString.FromString(Unary(stack, ValueKind.Bool, "str(bool) applied to").AsBool() ? "true" : "false"))); break;
                    case Op.MATH: MathOp(stack, reader.U8()); break;
                    case Op.FOR_RANGE: { int c = reader.U16(), e = reader.U16(), d = reader.I32(); long end = Pop(stack).AsInt(), start = Pop(stack).AsInt(); values[c] = Value.Int(start); values[e] = Value.Int(end); if (start >= end) reader.Pos = checked(reader.Pos + d); break; }
                    case Op.FOR_NEXT: { int end = reader.U16(), d = reader.I32(); var loop = loops[faultPc]; long i = unchecked(values[loop.Counter].AsInt() + 1); values[loop.Counter] = Value.Int(i); if (i < values[loop.End].AsInt()) reader.Pos = checked(reader.Pos + d); _ = end; break; }
                    case Op.WHILE_BEGIN: break;
                    case Op.WHILE_TEST: { int d = reader.I32(); if (!Pop(stack).AsBool()) reader.Pos = checked(reader.Pos + d); break; }
                    case Op.LOOP_BACK: { int delta = reader.I32(); reader.Pos = checked(reader.Pos + delta); break; }
                    default: throw new Error("RUN_ERROR", $"unknown opcode {(byte)op}");
                }
            }
            catch (Error e) { var line = LineOf(faultPc); if (line is not null) e.Locate(line.Number, line.Column); throw; }
        }
        }
        finally { owner.ExitRun(context); }
    }
    private static Value Unary(List<Value> stack, ValueKind kind, string operation)
    {
        Value value = Pop(stack);
        if (value.Kind != kind) throw new Error("RUN_ERROR", $"{operation} {value.KindName}");
        return value;
    }
    private static bool Condition(List<Value> stack)
    {
        Value value = Pop(stack);
        if (value.Kind != ValueKind.Bool) throw new Error("RUN_ERROR", $"condition is a {value.KindName}, not a bool");
        return value.AsBool();
    }
    private static (long A, long B) IntPair(List<Value> stack)
    {
        Value b = Pop(stack), a = Pop(stack);
        if (a.Kind != ValueKind.Int || b.Kind != ValueKind.Int) throw new Error("RUN_ERROR", $"int operator applied to {a.KindName} and {b.KindName}");
        return (a.AsInt(), b.AsInt());
    }
    private static (double A, double B) FloatPair(List<Value> stack)
    {
        Value b = Pop(stack), a = Pop(stack);
        if (a.Kind != ValueKind.Float || b.Kind != ValueKind.Float) throw new Error("RUN_ERROR", $"float operator applied to {a.KindName} and {b.KindName}");
        return (a.AsFloat(), b.AsFloat());
    }
    private static Value[] Args(List<Value> stack, int count) { var a = new Value[count]; for (int i = count - 1; i >= 0; i--) a[i] = Pop(stack); return a; }
    private static void CmpI(List<Value> s, Func<long, long, bool> f) { var (a, b) = IntPair(s); s.Add(Value.Bool(f(a, b))); }
    private static void CmpF(List<Value> s, Func<double, double, bool> f) { var (a, b) = FloatPair(s); s.Add(Value.Bool(f(a, b))); }
    private static void MathOp(List<Value> stack, byte operation)
    {
        if (operation >= 14)
        {
            long right = Pop(stack).AsInt();
            if (operation == 14) { stack.Add(Value.Int(right < 0 ? unchecked(-right) : right)); return; }
            long left = Pop(stack).AsInt();
            if (operation == 15) { stack.Add(Value.Int(Math.Min(left, right))); return; }
            if (operation == 16) { stack.Add(Value.Int(Math.Max(left, right))); return; }
            long value = Pop(stack).AsInt();
            stack.Add(Value.Int(Math.Min(Math.Max(value, left), right)));
            return;
        }
        double x = Pop(stack).AsFloat();
        switch (operation)
        {
            case 0: stack.Add(Value.Float(Math.Sin(x))); break;
            case 1: stack.Add(Value.Float(Math.Cos(x))); break;
            case 2: stack.Add(Value.Float(Math.Sqrt(x))); break;
            case 3: stack.Add(Value.Float(Math.Abs(x))); break;
            case 4: stack.Add(Value.Float(Math.Floor(x))); break;
            case 5: stack.Add(Value.Float(Math.Pow(Pop(stack).AsFloat(), x))); break;
            case 6: stack.Add(Value.Float(Min(Pop(stack).AsFloat(), x))); break;
            case 7: stack.Add(Value.Float(Max(Pop(stack).AsFloat(), x))); break;
            case 8:
                double lo = Pop(stack).AsFloat(), value = Pop(stack).AsFloat();
                stack.Add(Value.Float(Min(Max(value, lo), x))); break;
            case 9:
                double b = Pop(stack).AsFloat(), a = Pop(stack).AsFloat();
                double product = (b - a) * x;
                stack.Add(Value.Float(a + product)); break;
            case 10:
                x = Min(Max(x, 0), 1);
                double square = x * x, twice = 2 * x;
                stack.Add(Value.Float(square * (3 - twice))); break;
            case 11:
                x = Min(Max(x, 0), 1);
                stack.Add(Value.Float(x * x)); break;
            case 12:
                x = Min(Max(x, 0), 1);
                double delta = 1 - x;
                stack.Add(Value.Float(1 - delta * delta)); break;
            case 13:
                const double two63 = 9223372036854775808d;
                stack.Add(Value.Int(double.IsNaN(x) ? 0 : x >= two63 ? long.MaxValue : x <= -two63 ? long.MinValue : unchecked((long)x)));
                break;
            default: throw new Error("RUN_ERROR", "unsupported math function");
        }
    }
}
