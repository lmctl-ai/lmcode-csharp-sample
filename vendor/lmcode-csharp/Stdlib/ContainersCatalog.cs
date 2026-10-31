using System.Globalization;

namespace LmCode.Stdlib;

/// <summary>V2 value containers. All objects live in the active std arena.</summary>
public static class ContainersCatalog
{
    private sealed class Map<T>
    {
        public readonly List<(VmString Key, T Value)> Items = [];
        public readonly Dictionary<VmString, int> Positions = [];
        public int Find(VmString key) => Positions.GetValueOrDefault(key, -1);
        public void Rebuild() { Positions.Clear(); for (int i = 0; i < Items.Count; i++) Positions[Items[i].Key] = i; }
    }
    private sealed class Stack<T> { public readonly List<T> Items = []; }
    private sealed class Queue<T> { public readonly List<T> Items = []; public int Head; }
    internal sealed class Pq<T>
    {
        public readonly PriorityQueue<T, (double Priority, long Sequence)> Items = new(PriorityComparer.Instance);
        public long Next;
        private sealed class PriorityComparer : IComparer<(double Priority, long Sequence)>
        {
            public static readonly PriorityComparer Instance = new();
            public int Compare((double Priority, long Sequence) a, (double Priority, long Sequence) b)
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : a.Sequence.CompareTo(b.Sequence);
            }
        }
    }

    public static void RegisterContainers(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
            RegisterMap<long>(r, "IntMap", "int", Value.Int, static v => v.AsInt(), CollectionsCatalog.CreateIntList);
            RegisterMap<double>(r, "FloatMap", "float", Value.Float, static v => v.AsFloat(), CollectionsCatalog.CreateFloatList);
            RegisterStack<long>(r, "IntStack", "int", Value.Int, static v => v.AsInt());
            RegisterStack<double>(r, "FloatStack", "float", Value.Float, static v => v.AsFloat());
            RegisterStack<VmString>(r, "StrStack", "string", Value.Str, static v => v.AsStr());
            RegisterQueue<long>(r, "IntQueue", "int", Value.Int, static v => v.AsInt());
            RegisterQueue<double>(r, "FloatQueue", "float", Value.Float, static v => v.AsFloat());
            RegisterQueue<VmString>(r, "StrQueue", "string", Value.Str, static v => v.AsStr());
            RegisterPq<long>(r, "IntPQ", "int", Value.Int, static v => v.AsInt());
            RegisterPq<double>(r, "FloatPQ", "float", Value.Float, static v => v.AsFloat());
            RegisterPq<VmString>(r, "StrPQ", "string", Value.Str, static v => v.AsStr());
        }
        finally { r.EndCompatibilityRegistration(); }
    }

    internal static IReadOnlyList<(VmString Key,Value Value)> ResultEntries(Context context,ObjRef self,string type) => type switch
    {
        "IntMap" => context.Get<Map<long>>(self).Items.Select(item=>(item.Key,Value.Int(item.Value))).ToArray(),
        "FloatMap" => context.Get<Map<double>>(self).Items.Select(item=>(item.Key,Value.Float(item.Value))).ToArray(),
        _ => throw Error.Host("lmcode return: unsupported map handle"),
    };

    private static ObjRef Allocate<T>(Context c, string type, T value) where T : class
        => CollectionsCatalog.Allocate(c, type, value);

    private static void RegisterMap<T>(Registry r, string type, string element, Func<T, Value> put, Func<Value, T> get, Func<Context, IEnumerable<T>, Value> snapshot)
    {
        r.Function(type[..^3].ToLowerInvariant() + "_map", [], type, (c, _) => Value.Object(type, Allocate(c, type, new Map<T>())));
        r.Method(type, "keys", [], "StrList", (c, self, _) =>
            CollectionsCatalog.CreateStrList(c, c.Get<Map<T>>(self).Items.Select(entry => entry.Key)));
        r.Method(type, "values", [], element == "int" ? "IntList" : "FloatList", (c, self, _) =>
            snapshot(c, c.Get<Map<T>>(self).Items.Select(entry => entry.Value)));
        r.Method(type, "len", [], "int", (c, self, _) => Value.Int(c.Get<Map<T>>(self).Items.Count));
        r.Method(type, "has", ["string"], "bool", (c, self, a) => Value.Bool(c.Get<Map<T>>(self).Find(a[0].AsStr()) >= 0));
        r.Method(type, "get", ["string"], element, (c, self, a) =>
        {
            var m = c.Get<Map<T>>(self); int i = m.Find(a[0].AsStr());
            if (i < 0) throw Error.Host(type + ".get: missing key");
            return put(m.Items[i].Value);
        });
        r.Method(type, "get_or", ["string", element], element, (c, self, a) =>
        {
            var m = c.Get<Map<T>>(self); int i = m.Find(a[0].AsStr());
            return i < 0 ? a[1] : put(m.Items[i].Value);
        });
        r.Method(type, "set", ["string", element], type, (c, self, a) =>
        {
            var m = c.Get<Map<T>>(self); int i = m.Find(a[0].AsStr());
            if (i < 0) { m.Positions[a[0].AsStr()] = m.Items.Count; m.Items.Add((a[0].AsStr(), get(a[1]))); } else m.Items[i] = (m.Items[i].Key, get(a[1]));
            return Value.Object(type, self);
        });
        r.Method(type, "key_at", ["int"], "string", (c, self, a) =>
        {
            var m = c.Get<Map<T>>(self); int i = Index(a[0].AsInt(), m.Items.Count, type + ".key_at");
            return Value.Str(m.Items[i].Key);
        });
        r.Method(type, "remove", ["string"], "bool", (c, self, a) =>
        {
            var m = c.Get<Map<T>>(self); int i = m.Find(a[0].AsStr());
            if (i < 0) return Value.Bool(false); m.Items.RemoveAt(i); m.Rebuild(); return Value.Bool(true);
        });
        r.Method(type, "clear", [], type, (c, self, _) => { var m = c.Get<Map<T>>(self); m.Items.Clear(); m.Positions.Clear(); return Value.Object(type, self); });
    }

    private static void RegisterStack<T>(Registry r, string type, string element, Func<T, Value> put, Func<Value, T> get)
    {
        r.Function(type[..^5].ToLowerInvariant() + "_stack", [], type, (c, _) => Value.Object(type, Allocate(c, type, new Stack<T>())));
        r.Method(type, "push", [element], type, (c, self, a) => { c.Get<Stack<T>>(self).Items.Add(get(a[0])); return Value.Object(type, self); });
        r.Method(type, "pop", [], element, (c, self, _) => { var s = c.Get<Stack<T>>(self); if (s.Items.Count == 0) throw Error.Host(type + ".pop: empty"); int i = s.Items.Count - 1; T v = s.Items[i]; s.Items.RemoveAt(i); return put(v); });
        r.Method(type, "peek", [], element, (c, self, _) => { var s = c.Get<Stack<T>>(self); if (s.Items.Count == 0) throw Error.Host(type + ".peek: empty"); return put(s.Items[^1]); });
        r.Method(type, "len", [], "int", (c, self, _) => Value.Int(c.Get<Stack<T>>(self).Items.Count));
        r.Method(type, "empty", [], "bool", (c, self, _) => Value.Bool(c.Get<Stack<T>>(self).Items.Count == 0));
        r.Method(type, "clear", [], type, (c, self, _) => { c.Get<Stack<T>>(self).Items.Clear(); return Value.Object(type, self); });
        r.Method(type, "at", ["int"], element, (c, self, a) => { var s = c.Get<Stack<T>>(self); int i = Index(a[0].AsInt(), s.Items.Count, type + ".at"); return put(s.Items[s.Items.Count - 1 - i]); });
    }

    private static void RegisterQueue<T>(Registry r, string type, string element, Func<T, Value> put, Func<Value, T> get)
    {
        r.Function(type[..^5].ToLowerInvariant() + "_queue", [], type, (c, _) => Value.Object(type, Allocate(c, type, new Queue<T>())));
        r.Method(type, "push", [element], type, (c, self, a) => { c.Get<Queue<T>>(self).Items.Add(get(a[0])); return Value.Object(type, self); });
        r.Method(type, "pop", [], element, (c, self, _) => { var q = c.Get<Queue<T>>(self); if (q.Head == q.Items.Count) throw Error.Host(type + ".pop: empty"); T v = q.Items[q.Head++]; Trim(q); return put(v); });
        r.Method(type, "peek", [], element, (c, self, _) => { var q = c.Get<Queue<T>>(self); if (q.Head == q.Items.Count) throw Error.Host(type + ".peek: empty"); return put(q.Items[q.Head]); });
        r.Method(type, "len", [], "int", (c, self, _) => { var q = c.Get<Queue<T>>(self); return Value.Int(q.Items.Count - q.Head); });
        r.Method(type, "empty", [], "bool", (c, self, _) => { var q = c.Get<Queue<T>>(self); return Value.Bool(q.Head == q.Items.Count); });
        r.Method(type, "clear", [], type, (c, self, _) => { var q = c.Get<Queue<T>>(self); q.Items.Clear(); q.Head = 0; return Value.Object(type, self); });
        r.Method(type, "at", ["int"], element, (c, self, a) => { var q = c.Get<Queue<T>>(self); int i = Index(a[0].AsInt(), q.Items.Count - q.Head, type + ".at"); return put(q.Items[q.Head + i]); });
    }

    private static void RegisterPq<T>(Registry r, string type, string element, Func<T, Value> put, Func<Value, T> get)
    {
        r.Function(type[..^2].ToLowerInvariant() + "_pq", [], type, (c, _) => Value.Object(type, Allocate(c, type, new Pq<T>())));
        r.Method(type, "push", [element, "float"], type, (c, self, a) => { var q = c.Get<Pq<T>>(self); double p = a[1].AsFloat(); if (!double.IsFinite(p)) throw Error.Host(type + ".push: priority must be finite"); if (q.Next == long.MaxValue) throw Error.Host(type + ".push: insertion sequence exhausted"); q.Items.Enqueue(get(a[0]), (p, q.Next++)); return Value.Object(type, self); });
        r.Method(type, "pop", [], element, (c, self, _) => { var q = c.Get<Pq<T>>(self); if (!q.Items.TryDequeue(out T? v, out (double Priority, long Sequence) _)) throw Error.Host(type + ".pop: empty"); return put(v!); });
        r.Method(type, "peek", [], element, (c, self, _) => { var q = c.Get<Pq<T>>(self); if (!q.Items.TryPeek(out T? v, out (double Priority, long Sequence) _)) throw Error.Host(type + ".peek: empty"); return put(v!); });
        r.Method(type, "len", [], "int", (c, self, _) => Value.Int(c.Get<Pq<T>>(self).Items.Count));
        r.Method(type, "empty", [], "bool", (c, self, _) => Value.Bool(c.Get<Pq<T>>(self).Items.Count == 0));
        r.Method(type, "clear", [], type, (c, self, _) => { var q = c.Get<Pq<T>>(self); q.Items.Clear(); q.Next = 0; return Value.Object(type, self); });
    }
    private static int Index(long value, int count, string method)
    {
        if (value < 0 || value >= count) throw Error.Host(method + ": index out of range: " + value.ToString(CultureInfo.InvariantCulture));
        return (int)value;
    }
    private static void Trim<T>(Queue<T> q) { if (q.Head > 64 && q.Head > q.Items.Count / 2) { q.Items.RemoveRange(0, q.Head); q.Head = 0; } }
}
