using System.Text;
namespace LmCode;

/// <summary>Immutable VM bytes. Text conversion is an explicit host operation.</summary>
public sealed class VmString : IEquatable<VmString>
{
    private readonly byte[] bytes;
    public VmString(ReadOnlySpan<byte> bytes) { this.bytes = bytes.ToArray(); }
    private VmString(byte[] bytes, bool owned) { this.bytes = bytes; }
    internal static VmString FromOwned(byte[] bytes) => new(bytes, true);
    public ReadOnlySpan<byte> Span => bytes;
    public int Length => bytes.Length;
    public static VmString FromString(string text) => FromOwned(Encoding.UTF8.GetBytes(text));
    public override string ToString() => Encoding.UTF8.GetString(bytes);
    public static VmString Concat(VmString a, VmString b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.Span.CopyTo(result); b.Span.CopyTo(result.AsSpan(a.Length));
        return FromOwned(result);
    }
    public bool Equals(VmString? other) => other is not null && Span.SequenceEqual(other.Span);
    public override bool Equals(object? obj) => obj is VmString value && Equals(value);
    public override int GetHashCode() { var h = new HashCode(); foreach (byte b in bytes) h.Add(b); return h.ToHashCode(); }
}
public enum ValueKind { None, Int, Float, Str, Bool, Obj }
public readonly record struct ObjRef(int Index);
public readonly struct Value
{
    public ValueKind Kind { get; }
    private readonly long integer;
    private readonly double number;
    private readonly VmString? text;
    private readonly ObjRef obj;
    public string TypeName { get; }
    private Value(ValueKind kind, long integer = 0, double number = 0, VmString? text = null, ObjRef obj = default, string typeName = "")
    { Kind = kind; this.integer = integer; this.number = number; this.text = text; this.obj = obj; TypeName = typeName; }
    public static Value None => default;
    public static Value Int(long n) => new(ValueKind.Int, integer: n);
    public static Value Float(double n) => new(ValueKind.Float, number: n);
    public static Value Str(VmString s) => new(ValueKind.Str, text: s);
    public static Value Bool(bool b) => new(ValueKind.Bool, integer: b ? 1 : 0);
    public static Value Object(string type, ObjRef obj) => new(ValueKind.Obj, obj: obj, typeName: type);
    public string KindName => Kind switch { ValueKind.Int => "int", ValueKind.Float => "float", ValueKind.Str => "string", ValueKind.Bool => "bool", ValueKind.Obj => "object", _ => "void" };
    private Error Expected(string kind) => new("RUN_ERROR", $"expected {kind}, got {KindName}");
    public long AsInt() => Kind == ValueKind.Int ? integer : throw Expected("int");
    public double AsFloat() => Kind == ValueKind.Float ? number : throw Expected("float");
    public VmString AsStr() => Kind == ValueKind.Str ? text! : throw Expected("string");
    public bool AsBool() => Kind == ValueKind.Bool ? integer != 0 : throw Expected("bool");
    public ObjRef AsObj() => Kind == ValueKind.Obj ? obj : throw Expected("object");
}
public sealed class Context
{
    private readonly List<object?> objects = [];
    private readonly List<string> types = [];
    private readonly Dictionary<string, ObjRef> names = new(StringComparer.Ordinal);
    private readonly Stack<int> freeCollectionSlots = new();
    public ObjRef Insert(string type, object value) { var r = new ObjRef(objects.Count); objects.Add(value); types.Add(type); return r; }
    public void Set(string name, ObjRef obj) => names[name] = obj;
    public ObjRef SetObject(string name, string type, object value) { var r = Insert(type, value); Set(name, r); return r; }
    public ObjRef? Lookup(string name) => names.TryGetValue(name, out var r) ? r : null;
    public string TypeName(ObjRef obj) => types[obj.Index];
    public T Get<T>(ObjRef obj) => obj.Index >= 0 && obj.Index < objects.Count && objects[obj.Index] is T value ? value : throw Error.Host($"context object #{obj.Index} has the wrong host type or was taken");
    public T Take<T>(ObjRef obj) { T value = Get<T>(obj); objects[obj.Index] = null; return value; }
    public int Count => objects.Count;
    internal ObjRef InsertCollection(string type, object value)
    {
        if (!freeCollectionSlots.TryPop(out int index)) return Insert(type, value);
        objects[index] = value; types[index] = type; return new ObjRef(index);
    }
    internal void ReleaseCollection(ObjRef reference)
    {
        objects[reference.Index] = null;
        freeCollectionSlots.Push(reference.Index);
    }
}
