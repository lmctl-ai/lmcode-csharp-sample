using System.Globalization;
using System.Runtime.CompilerServices;

namespace LmCode.Stdlib;

public sealed class StrList
{
    internal readonly List<VmString> Items;
    public StrList() : this([]) { }
    public StrList(IEnumerable<VmString> values) => Items = new(values ?? throw new ArgumentNullException(nameof(values)));
    public IReadOnlyList<VmString> Values => Items;
}
public sealed class IntList
{
    internal readonly List<long> Items;
    public IntList() : this([]) { }
    public IntList(IEnumerable<long> values) => Items = new(values ?? throw new ArgumentNullException(nameof(values)));
    public IReadOnlyList<long> Values => Items;
}
public sealed class FloatList
{
    internal readonly List<double> Items;
    public FloatList() : this([]) { }
    public FloatList(IEnumerable<double> values) => Items = new(values ?? throw new ArgumentNullException(nameof(values)));
    public IReadOnlyList<double> Values => Items;
}
public sealed class StrMap
{
    internal readonly List<(VmString Key, VmString Value)> Items = [];
    internal readonly Dictionary<VmString, int> Positions = [];
    internal readonly HashSet<int> Deleted = [];
    internal int Live;
    internal bool Dirty;
    internal int[] Fenwick = [0];
    public StrMap() { }
    public StrMap(IEnumerable<(VmString Key, VmString Value)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var (key, value) in entries)
        {
            if (Positions.TryGetValue(key, out int index)) Items[index] = (key, value);
            else { Positions.Add(key, Items.Count); Items.Add((key, value)); }
        }
        Live = Items.Count;
        Fenwick = new int[Items.Count + 1];
        FenwickCount = Fenwick.Length;
        for (int i = 1; i < Fenwick.Length; ++i) Fenwick[i] = i & -i;
    }
    public IReadOnlyList<(VmString Key, VmString Value)> Entries => new EntryView(this);
    private sealed class EntryView(StrMap map) : IReadOnlyList<(VmString Key, VmString Value)>
    {
        public int Count => map.Live;
        public (VmString Key, VmString Value) this[int index]
        {
            get { if ((uint)index >= (uint)map.Live) throw new ArgumentOutOfRangeException(nameof(index)); return map.Items[map.FenwickSelect(index + 1)]; }
        }
        public IEnumerator<(VmString Key, VmString Value)> GetEnumerator()
        { for (int i = 0; i < map.Items.Count; ++i) if (!map.Deleted.Contains(i)) yield return map.Items[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal void Compact()
    {
        if (!Dirty) return;
        var items = new List<(VmString Key, VmString Value)>(Live);
        var positions = new Dictionary<VmString, int>(Live);
        for (int i = 0; i < Items.Count; ++i) if (!Deleted.Contains(i)) { positions[Items[i].Key] = items.Count; items.Add(Items[i]); }
        Items.Clear(); Items.AddRange(items); Positions.Clear(); foreach (var pair in positions) Positions[pair.Key] = pair.Value; Deleted.Clear(); Dirty = false; Fenwick = new int[Items.Count + 1]; FenwickCount = Fenwick.Length; for (int i = 1; i < Fenwick.Length; ++i) Fenwick[i] = i & -i;
    }
    internal int FenwickCount = 1;
    internal int FenwickPrefix(int index) { int total = 0; for (; index > 0; index -= index & -index) total += Fenwick[index]; return total; }
    internal void FenwickAdd(int index, int delta) { for (; index < FenwickCount; index += index & -index) Fenwick[index] += delta; }
    internal int FenwickSelect(int target) { int index = 0, bit = 1; while ((bit << 1) < FenwickCount) bit <<= 1; for (; bit > 0; bit >>= 1) { int next = index + bit; if (next < FenwickCount && Fenwick[next] < target) { index = next; target -= Fenwick[next]; } } return index; }
}

/// <summary>Optional byte-oriented collection host functions.</summary>
public static class CollectionsCatalog
{
    private const int MaxObjects = 65536;
    private sealed class State { public int Count; public readonly List<ObjRef> References = []; public readonly Dictionary<object, ObjRef> Borrowed = new(ReferenceEqualityComparer.Instance); }
    private static readonly ConditionalWeakTable<Context, State> States = new();

    public static void RegisterCollections(Registry r, bool defaultOwned = false)
    {
        if (defaultOwned) r.MarkDefaultCollections();
        r.BeginCompatibilityRegistration();
        try
        {
        r.Method("StrList", "len", [], "int", static (c, self, _) => Value.Int(c.Get<StrList>(self).Items.Count));
        r.Method("StrList", "at", ["int"], "string", static (c, self, a) => Value.Str(c.Get<StrList>(self).Items[Index(a[0].AsInt(), c.Get<StrList>(self).Items.Count, "StrList.at")]));
        r.Method("StrList", "add", ["string"], "StrList", static (c, self, a) => { c.Get<StrList>(self).Items.Add(a[0].AsStr()); return Value.Object("StrList", self); });
        r.Method("StrList", "set", ["int", "string"], "StrList", static (c, self, a) => { var x = c.Get<StrList>(self); x.Items[Index(a[0].AsInt(), x.Items.Count, "StrList.set")] = a[1].AsStr(); return Value.Object("StrList", self); });
        r.Method("StrList", "contains", ["string"], "bool", static (c, self, a) => Value.Bool(c.Get<StrList>(self).Items.Contains(a[0].AsStr())));
        r.Method("StrList", "index_of", ["string"], "int", static (c, self, a) => Value.Int(c.Get<StrList>(self).Items.IndexOf(a[0].AsStr())));
        r.Method("StrList", "clear", [], "StrList", static (c, self, _) => { c.Get<StrList>(self).Items.Clear(); return Value.Object("StrList", self); });
        r.Method("StrList", "join", ["string"], "string", static (c, self, a) => Value.Str(Join(c.Get<StrList>(self).Items, a[0].AsStr())));

        RegisterList<IntList, long>(r, "IntList", "int", static x => x.Items, static v => v.AsInt(), Value.Int, static (a, b) => a == b);
        RegisterList<FloatList, double>(r, "FloatList", "float", static x => x.Items, static v => v.AsFloat(), Value.Float, static (a, b) => a == b);

        r.Method("StrMap", "len", [], "int", static (c, self, _) => Value.Int(c.Get<StrMap>(self).Live));
        r.Method("StrMap", "has", ["string"], "bool", static (c, self, a) => c.Get<StrMap>(self).Positions.ContainsKey(a[0].AsStr()) ? Value.Bool(true) : Value.Bool(false));
        r.Method("StrMap", "get", ["string"], "string", static (c, self, a) =>
        {
            var map = c.Get<StrMap>(self); int i = Find(map, a[0].AsStr());
            if (i < 0) throw Error.Host("StrMap.get: missing key");
            return Value.Str(map.Items[i].Value);
        });
        r.Method("StrMap", "get_or", ["string", "string"], "string", static (c, self, a) =>
        {
            var map = c.Get<StrMap>(self); int i = Find(map, a[0].AsStr());
            return Value.Str(i < 0 ? a[1].AsStr() : map.Items[i].Value);
        });
        r.Method("StrMap", "set", ["string", "string"], "StrMap", static (c, self, a) =>
        {
            var map = c.Get<StrMap>(self); if (map.Dirty && map.Items.Count > 2 * map.Live + 1024) map.Compact(); int i = Find(map, a[0].AsStr());
            if (i < 0) { map.Positions[a[0].AsStr()] = map.Items.Count; map.Items.Add((a[0].AsStr(), a[1].AsStr())); int index = map.Items.Count; if (index >= map.Fenwick.Length) Array.Resize(ref map.Fenwick, Math.Max(index + 1, map.Fenwick.Length * 2)); map.Fenwick[index] = map.FenwickPrefix(index - 1) - map.FenwickPrefix(index - (index & -index)) + 1; map.FenwickCount = index + 1; ++map.Live; } else map.Items[i] = (map.Items[i].Key, a[1].AsStr());
            return Value.Object("StrMap", self);
        });
        r.Method("StrMap", "key_at", ["int"], "string", static (c, self, a) =>
        {
            var map = c.Get<StrMap>(self); long wanted = a[0].AsInt(); if (wanted < 0 || wanted >= map.Live) throw Error.Host($"StrMap.key_at: index out of range: {wanted.ToString(CultureInfo.InvariantCulture)}");
            return Value.Str(map.Items[map.FenwickSelect((int)wanted + 1)].Key);
        });
        r.Method("StrMap", "remove", ["string"], "bool", static (c, self, a) =>
        {
            var map = c.Get<StrMap>(self); int i = Find(map, a[0].AsStr());
            if (i < 0) return Value.Bool(false); map.Positions.Remove(a[0].AsStr()); map.Deleted.Add(i); --map.Live; map.Dirty = true; map.FenwickAdd(i + 1, -1); return Value.Bool(true);
        });
        r.Method("StrMap", "clear", [], "StrMap", static (c, self, _) => { var map = c.Get<StrMap>(self); map.Items.Clear(); map.Positions.Clear(); map.Deleted.Clear(); map.Live = 0; map.Dirty = false; map.Fenwick = [0]; map.FenwickCount = 1; return Value.Object("StrMap", self); });

        r.Function("str_list", [], "StrList", static (c, _) => Value.Object("StrList", Allocate(c, "StrList", new StrList())));
        r.Function("int_list", [], "IntList", static (c, _) => Value.Object("IntList", Allocate(c, "IntList", new IntList())));
        r.Function("float_list", [], "FloatList", static (c, _) => Value.Object("FloatList", Allocate(c, "FloatList", new FloatList())));
        r.Function("str_map", [], "StrMap", static (c, _) => Value.Object("StrMap", Allocate(c, "StrMap", new StrMap())));
        r.Function("str_split", ["string", "string"], "StrList", static (c, a) =>
        {
            VmString text = a[0].AsStr(), separator = a[1].AsStr();
            if (separator.Length == 0) throw Error.Host("str_split: separator must not be empty");
            var list = new StrList(); int at = 0;
            while (true)
            {
                int found = text.Span[at..].IndexOf(separator.Span);
                if (found < 0) { list.Items.Add(new VmString(text.Span[at..])); break; }
                found += at; list.Items.Add(new VmString(text.Span[at..found])); at = found + separator.Length;
            }
            return Value.Object("StrList", Allocate(c, "StrList", list));
        });
        }
        finally { r.EndCompatibilityRegistration(); }
    }

    private static void RegisterList<TList, T>(Registry r, string type, string element, Func<TList, List<T>> items, Func<Value, T> get, Func<T, Value> put, Func<T, T, bool> equals)
        where TList : class
    {
        r.Method(type, "len", [], "int", (c, self, _) => Value.Int(items(c.Get<TList>(self)).Count));
        r.Method(type, "at", ["int"], element, (c, self, a) => { var x = items(c.Get<TList>(self)); return put(x[Index(a[0].AsInt(), x.Count, type + ".at")]); });
        r.Method(type, "add", [element], type, (c, self, a) => { items(c.Get<TList>(self)).Add(get(a[0])); return Value.Object(type, self); });
        r.Method(type, "set", ["int", element], type, (c, self, a) => { var x = items(c.Get<TList>(self)); x[Index(a[0].AsInt(), x.Count, type + ".set")] = get(a[1]); return Value.Object(type, self); });
        r.Method(type, "contains", [element], "bool", (c, self, a) => Value.Bool(Find(items(c.Get<TList>(self)), get(a[0]), equals) >= 0));
        r.Method(type, "index_of", [element], "int", (c, self, a) => Value.Int(Find(items(c.Get<TList>(self)), get(a[0]), equals)));
        r.Method(type, "clear", [], type, (c, self, _) => { items(c.Get<TList>(self)).Clear(); return Value.Object(type, self); });
    }
    private static int Find<T>(List<T> items, T value, Func<T, T, bool> equals)
    {
        for (int i = 0; i < items.Count; ++i) if (equals(items[i], value)) return i;
        return -1;
    }
    private static int Index(long index, int count, string method)
    {
        if (index < 0 || (ulong)index >= (uint)count) throw Error.Host($"{method}: index out of range: {index.ToString(CultureInfo.InvariantCulture)}");
        return (int)index;
    }
    private static int Find(StrMap map, VmString key)
    {
        return map.Positions.GetValueOrDefault(key, -1);
    }
    private static VmString Join(List<VmString> items, VmString separator)
    {
        int total = 0; for (int i = 0; i < items.Count; ++i) total = checked(total + items[i].Length + (i == 0 ? 0 : separator.Length));
        var bytes = new byte[total]; int at = 0;
        for (int i = 0; i < items.Count; ++i) { if (i != 0) { separator.Span.CopyTo(bytes.AsSpan(at)); at += separator.Length; } items[i].Span.CopyTo(bytes.AsSpan(at)); at += items[i].Length; }
        return VmString.FromOwned(bytes);
    }
    /// <summary>Copy host values into a standard collection owned by this run.</summary>
    public static Value CreateStrList(Context context, IEnumerable<VmString> values)
        => Value.Object("StrList", Allocate(context, "StrList", new StrList(values)));
    public static Value CreateIntList(Context context, IEnumerable<long> values)
        => Value.Object("IntList", Allocate(context, "IntList", new IntList(values)));
    public static Value CreateFloatList(Context context, IEnumerable<double> values)
        => Value.Object("FloatList", Allocate(context, "FloatList", new FloatList(values)));
    public static Value CreateStrMap(Context context, IEnumerable<(VmString Key, VmString Value)> values)
        => Value.Object("StrMap", Allocate(context, "StrMap", new StrMap(values)));
    internal static ObjRef Allocate<T>(Context context, string type, T value) where T : class
    {
        State state = States.GetOrCreateValue(context);
        if (state.Count >= MaxObjects) throw Error.Host($"{type} object limit exceeded (65536)");
        ++state.Count; ObjRef reference = context.InsertCollection(type, value); state.References.Add(reference); state.Borrowed[value] = reference; return reference;
    }
    // Immutable Json navigation borrows nodes without spending another root
    // allocation. Cache by reference so repeated navigation preserves identity.
    internal static ObjRef Borrow(Context context, string type, object value)
    {
        State state = States.GetOrCreateValue(context);
        if (state.Borrowed.TryGetValue(value, out ObjRef existing)) return existing;
        ObjRef reference = context.InsertCollection(type, value);
        state.References.Add(reference);
        state.Borrowed.Add(value, reference);
        return reference;
    }
    internal static void BeginRun(Context context)
    {
        if (States.TryGetValue(context, out State? state)) { foreach (ObjRef reference in state.References) context.ReleaseCollection(reference); state.References.Clear(); state.Borrowed.Clear(); state.Count = 0; }
    }
    internal static void EndRun(Context context) => BeginRun(context);
    internal static ObjRef AllocateStrList(Context context, StrList list) => Allocate(context, "StrList", list);
}
