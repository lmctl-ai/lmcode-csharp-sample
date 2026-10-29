using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace LmCode;
internal enum Op : byte
{
    PUSH_INT = 1, PUSH_STR, PUSH_BOOL, LOAD_LOCAL, STORE_LOCAL, LOAD_CTX, CALL_FN, CALL_METHOD, EQ, NE, NOT, JUMP, JUMP_IF_FALSE, JUMP_IF_TRUE, DUP, POP, HALT,
    PUSH_FLOAT, ADD_I, SUB_I, MUL_I, DIV_I, ADD_F, SUB_F, MUL_F, DIV_F, NEG_I, NEG_F, LT_I, LE_I, GT_I, GE_I, LT_F, LE_F, GT_F, GE_F, I2F, CONCAT, I2S, F2S, B2S,
    FOR_RANGE = 0x2f, FOR_NEXT = 0x31, MATH = 0x32, MOD_I = 0x33, WHILE_BEGIN = 0x34, WHILE_TEST = 0x35, LOOP_BACK = 0x36
}
internal sealed class Reader(byte[] bytes)
{
    public int Pos { get; set; }
    public bool Done => Pos == bytes.Length;
    private void Need(long count)
    {
        if (Pos < 0 || count < 0 || count > bytes.Length - (long)Pos)
            throw Error.BadBytecode($"bytecode is truncated: wanted {count} byte(s) at offset {Pos}, file has {bytes.Length}");
    }
    public byte U8() { Need(1); return bytes[Pos++]; }
    public ushort U16() { Need(2); var v = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(Pos)); Pos += 2; return v; }
    public uint U32() { Need(4); var v = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(Pos)); Pos += 4; return v; }
    public int I32() => unchecked((int)U32());
    public long I64() { Need(8); var v = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(Pos)); Pos += 8; return v; }
    public double F64() => BitConverter.Int64BitsToDouble(I64());
    public byte[] Bytes(uint count) { Need(count); var v = bytes.AsSpan(Pos, (int)count).ToArray(); Pos += (int)count; return v; }
}
public sealed record Local(string TypeName, bool Hidden);
public sealed record Line(int Pc, uint Number, ushort Column);
public sealed record Entry(string Name, string Annotation, string Iface, IReadOnlyList<string> Params, string Returns);
public sealed partial class Program
{
    public const ushort Version = 6;
    private List<VmString> consts = [];
    private List<Binding> imports = [];
    private List<string> contexts = [];
    private readonly List<Local> locals = [];
    private readonly List<Line> lines = [];
    private readonly Dictionary<int, (int Counter, int End)> loops = [];
    private byte[] code = [];
    private ushort maxStack;
    private Registry? boundRegistry;
    private readonly List<(Entry Metadata, Program Region)> entries = [];
    public IReadOnlyList<Entry> Entries => Array.AsReadOnly(entries.Select(e => e.Metadata).ToArray());
    private Program() { }
    public Value Call(int entry, Context context, Registry registry, IReadOnlyList<Value> args)
    {
        if (entry < 0 || entry >= entries.Count) throw Error.Host($"call entry index {entry} out of range ({entries.Count} entries)");
        var rule = entries[entry];
        if (args.Count != rule.Metadata.Params.Count) throw Error.Host($"call entry '{rule.Metadata.Name}' expects {rule.Metadata.Params.Count} argument(s); found {args.Count}");
        return rule.Region.Execute(context, registry, args, rule.Metadata.Returns != "void");
    }
    public int ConstantCount => consts.Count;
    public int ImportCount => imports.Count;
    public int LocalCount => locals.Count;
    public int CodeSize => code.Length;
    public ushort MaxStack => maxStack;
    public IReadOnlyList<Local> Locals => locals.AsReadOnly();
    public IReadOnlyList<Line> Lines => lines.AsReadOnly();
    public Line? LineOf(int pc) { Line? best = null; foreach (var line in lines) { if (line.Pc > pc) break; best = line; } return best; }
    public static Program Load(byte[] bytecode, byte[] schemaBytes, Registry registry)
    {
        if (bytecode.Length < 4 || !bytecode.AsSpan(0, 4).SequenceEqual("DSLB"u8))
            throw Error.BadBytecode("not a DSL bytecode file: bad magic (expected 'DSLB')");
        var p = new Program(); var r = new Reader(bytecode); r.Bytes(4);
        ushort version = r.U16();
        if (version != Version) throw Error.BadBytecode($"unsupported bytecode version {version} (this runtime reads version {Version})");
        byte[] hash = r.Bytes(32), expected = SHA256.HashData(schemaBytes);
        if (!hash.AsSpan().SequenceEqual(expected)) throw new Error("LOAD_SCHEMA_MISMATCH", $"schema mismatch: bytecode was compiled against schema sha256 {Convert.ToHexString(hash).ToLowerInvariant()}, the given schema has sha256 {Convert.ToHexString(expected).ToLowerInvariant()}");
        uint count = r.U32();
        for (uint i = 0; i < count; i++)
        {
            byte tag = r.U8(); if (tag != 1) throw Error.BadBytecode($"constant {i} has unknown tag {tag} (1 = string)");
            p.consts.Add(new VmString(r.Bytes(r.U32())));
        }
        string Constant(uint index, string what)
        {
            if (index >= p.consts.Count) throw Error.BadBytecode($"{what}: constant index {index} out of range ({p.consts.Count} constants)");
            // Only schema identifiers become text; value constants always remain raw bytes.
            var bytes = p.consts[(int)index].Span;
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new Error("LOAD_UNBOUND", $"{what} is not a valid UTF-8 schema identifier"); }
        }
        count = r.U16();
        for (uint i = 0; i < count; i++)
        {
            string canonical = Constant(r.U32(), "import"); var binding = registry.Resolve(canonical);
            if (binding is null)
            {
                string have = registry.DeclaredFor(canonical);
                throw new Error("LOAD_UNBOUND", $"import '{canonical}' is not provided by the host" + (have.Length == 0 ? "" : $" (the host declares {have})"));
            }
            p.imports.Add(binding);
        }
        count = r.U16();
        for (uint i = 0; i < count; i++)
        {
            string name = Constant(r.U32(), "context name"), type = Constant(r.U32(), "context type");
            string? declared = registry.ContextType(name);
            if (declared is null) throw new Error("LOAD_UNBOUND", $"context variable '{name}' is not declared by the host");
            if (declared != type) throw new Error("LOAD_UNBOUND", $"context variable '{name}' is declared as {type} by the bytecode, the host provides {declared}");
            p.contexts.Add(name);
        }
        void ReadRegion(Program region)
        {
            uint count = r.U16();
            for (uint i = 0; i < count; i++)
            {
                string type = Constant(r.U32(), "local type"); byte hidden = r.U8();
                if (hidden > 1) throw Error.BadBytecode($"local {i} hidden flag must be 0 or 1");
                region.locals.Add(new(type, hidden == 1));
            }
            region.maxStack = r.U16(); region.code = r.Bytes(r.U32()); count = r.U32();
            for (uint i = 0; i < count; i++)
            {
                uint pc = r.U32(), line = r.U32(); ushort column = r.U16();
                if (pc > int.MaxValue) throw Error.BadBytecode($"line table entry points at pc {pc}, beyond the code");
                if (region.lines.Count > 0 && pc < region.lines[^1].Pc) throw Error.BadBytecode($"line table is not sorted by pc at entry {i}");
                region.lines.Add(new((int)pc, line, column));
            }
        }
        ReadRegion(p); p.boundRegistry = registry; p.Verify();
        int entryCount = r.U16();
        for (int i = 0; i < entryCount; i++)
        {
            string name = "<" + i + ">";
            try
            {
                name = Constant(r.U32(), "entry name");
                if (p.entries.Any(e => e.Metadata.Name == name)) throw Error.BadBytecode($"duplicate entry name '{name}'");
                string annotation = Constant(r.U32(), "entry annotation"), iface = Constant(r.U32(), "entry interface");
                int nparams = r.U16();
                var region = new Program { consts = p.consts, imports = p.imports, contexts = p.contexts, boundRegistry = registry };
                ReadRegion(region);
                if (nparams > region.locals.Count) throw Error.BadBytecode($"nparams {nparams} exceeds nlocals {region.locals.Count}");
                var match = System.Text.RegularExpressions.Regex.Match(iface, @"\Ainterface ([A-Za-z_][A-Za-z0-9_]*)\(([^()]*)\) -> ([A-Za-z_][A-Za-z0-9_]*)\z");
                if (!match.Success) throw Error.BadBytecode($"interface '{iface}' is not an interface signature");
                string[] parameters = match.Groups[2].Value.Length == 0 ? [] : match.Groups[2].Value.Split(", ", StringSplitOptions.None);
                if (parameters.Any(t => !System.Text.RegularExpressions.Regex.IsMatch(t, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))) throw Error.BadBytecode($"interface '{iface}' is not an interface signature");
                if (nparams != parameters.Length) throw Error.BadBytecode($"entry '{name}' declares {nparams} parameter(s); {iface} has {parameters.Length}");
                for (int j = 0; j < nparams; j++)
                {
                    var local = region.locals[j];
                    if (local.Hidden) throw Error.BadBytecode($"entry '{name}' parameter {j + 1} is hidden");
                    if (local.TypeName != parameters[j]) throw Error.BadBytecode($"entry '{name}' parameter {j + 1} has type {local.TypeName}; the interface expects {parameters[j]}");
                }
                string returns = match.Groups[3].Value;
                region.Verify(returns == "void" ? 0 : 1, returns);
                p.entries.Add((new Entry(name, annotation, iface, Array.AsReadOnly(parameters), returns), region));
            }
            catch (Error e)
            {
                string prefix = $"entry '{name}'", detail = e.Message;
                if (detail.StartsWith("control flow target", StringComparison.Ordinal)) detail = "jump " + detail;
                throw Error.BadBytecode(detail.StartsWith(prefix, StringComparison.Ordinal) ? detail : prefix + ": " + detail);
            }
        }
        if (!r.Done) throw Error.BadBytecode($"bytecode has {bytecode.Length - r.Pos} trailing byte(s) after the entries");
        return p;
    }
}
