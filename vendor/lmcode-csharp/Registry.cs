namespace LmCode;
internal sealed record Binding(string Name, string? TypeName, string[] Parameters, string Returns,
    Func<Context, Value[], Value>? Function, Func<Context, ObjRef, Value[], Value>? Method);
public sealed class Registry
{
    private readonly Dictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> contexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> builtins = new(StringComparer.Ordinal);
    private bool registeringBuiltins;
    private bool compatibilityRegistration;
    private int activeRuns;
    private bool defaultCollections;
    private ResultSink? resultSink;
    /// <summary>Bind once per logical invocation; retain across body and entry calls.</summary>
    public void SetResultSink(ResultSink? sink)
    {
        if (!defaultCollections) throw Error.Host("std: registry has standard library disabled");
        resultSink=sink;
    }
    internal ResultSink RequiredResultSink() => resultSink ?? throw Error.Host("lmcode return: no result sink");
    public Registry(bool includeStd = true) { if (includeStd) Stdlib.StandardCatalog.RegisterStd(this); }
    private Registry Add(string name, string? type, string[] parameters, string returns,
        Func<Context, Value[], Value>? function, Func<Context, ObjRef, Value[], Value>? method)
    {
        string label = type is null ? "function" : "method";
        string fullName = type is null ? name : type + "." + name;
        string sig = $"({string.Join(", ", parameters)}) -> {returns}";
        string canonical = $"{(type is null ? "fn" : "method")} {fullName}{sig}";
        if (bindings.ContainsKey(canonical) && registeringBuiltins && builtins.Contains(canonical)) return this;
        foreach (var entry in bindings.Values)
            if (entry.Name == name && entry.TypeName == type && entry.Parameters.AsSpan().SequenceEqual(parameters))
            {
                if (!builtins.Contains(canonical) || !compatibilityRegistration) throw new Error("LOAD_UNBOUND", $"registry: {label} '{fullName}{sig}' is registered twice");
                bindings[canonical] = new(name, type, (string[])parameters.Clone(), returns, function, method); return this;
            }
        bindings.Add(canonical, new(name, type, (string[])parameters.Clone(), returns, function, method));
        if (registeringBuiltins) builtins.Add(canonical);
        return this;
    }
    public Registry Function(string name, string[] parameters, string returns, Func<Context, Value[], Value> function) => Add(name, null, parameters, returns, function, null);
    public Registry Method(string type, string name, string[] parameters, string returns, Func<Context, ObjRef, Value[], Value> method) => Add(name, type, parameters, returns, null, method);
    public Registry Context(string name, string type)
    {
        if (!contexts.TryAdd(name, type)) throw new Error("LOAD_UNBOUND", $"registry: context '{name}' is declared twice");
        return this;
    }
    public string? ContextType(string name) => contexts.GetValueOrDefault(name);
    public string[] Signatures()
    {
        var result = new List<string>(bindings.Keys);
        foreach (var (name, type) in contexts) result.Add($"context {name}: {type}");
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
    public string[] HostSignatures()
    {
        var result = bindings.Keys.Where(x => !builtins.Contains(x)).ToList();
        result.AddRange(contexts.Select(x => $"context {x.Key}: {x.Value}"));
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
    public string[] StdSignatures() => builtins.Order(StringComparer.Ordinal).ToArray();
    public bool MatchesSchemaSignatures(IEnumerable<string> schemaSignatures)
    {
        try { CheckSchemaSignatures(schemaSignatures); return true; }
        catch (Error) { return false; }
    }
    public void CheckSchemaSignatures(IEnumerable<string> schemaSignatures)
    {
        var expected = schemaSignatures.ToHashSet(StringComparer.Ordinal);
        foreach (string builtin in builtins) expected.Add(builtin);
        var actual = Signatures().ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected)) throw new Error("LOAD_UNBOUND", $"registry schema signatures differ: missing [{string.Join(", ", expected.Except(actual).Order())}], extra [{string.Join(", ", actual.Except(expected).Order())}]");
    }
    internal void BeginBuiltinRegistration() => registeringBuiltins = true;
    internal void EndBuiltinRegistration() => registeringBuiltins = false;
    internal void BeginCompatibilityRegistration() => compatibilityRegistration = true;
    internal void EndCompatibilityRegistration() => compatibilityRegistration = false;
    internal void MarkDefaultCollections() => defaultCollections = true;
    internal void EnterRun(Context context)
    {
        if (!defaultCollections) return;
        if (Interlocked.CompareExchange(ref activeRuns, 1, 0) != 0) throw Error.Host("registry run is already active");
        if (defaultCollections) Stdlib.CollectionsCatalog.BeginRun(context);
    }
    internal void ExitRun(Context context)
    {
        if (!defaultCollections) return;
        if (defaultCollections) Stdlib.CollectionsCatalog.EndRun(context);
        Volatile.Write(ref activeRuns, 0);
    }
    internal Binding? Resolve(string canonical) => bindings.GetValueOrDefault(canonical);
    internal string DeclaredFor(string canonical)
    {
        int par = canonical.IndexOf('('); if (par < 0) return "";
        string prefix = canonical[..par] + "(";
        var matches = new List<string>();
        foreach (string key in bindings.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) matches.Add(key);
        matches.Sort(StringComparer.Ordinal); return string.Join(", ", matches);
    }
}
