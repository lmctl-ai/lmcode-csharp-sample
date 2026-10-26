namespace LmCode.Stdlib;

/// <summary>The built-in lmcode standard catalog (v3).</summary>
public static class StandardCatalog
{
    private static readonly Lazy<HashSet<string>> Canonical = new(() =>
    {
        var registry = new Registry(false);
        StringCatalog.RegisterString(registry);
        EncodingCatalog.RegisterEncoding(registry);
        MathCatalog.RegisterMath(registry);
        CollectionsCatalog.RegisterCollections(registry);
        FileCatalog.RegisterFile(registry);
        ContainersCatalog.RegisterContainers(registry);
        JsonCatalog.RegisterJson(registry);
        return registry.Signatures().ToHashSet(StringComparer.Ordinal);
    });
    public static IReadOnlyCollection<string> Signatures => Canonical.Value;

    public static void RegisterStd(Registry registry)
    {
        registry.BeginBuiltinRegistration();
        try
        {
            StringCatalog.RegisterString(registry);
            EncodingCatalog.RegisterEncoding(registry);
            MathCatalog.RegisterMath(registry);
            CollectionsCatalog.RegisterCollections(registry, defaultOwned: true);
            FileCatalog.RegisterFile(registry);
            ContainersCatalog.RegisterContainers(registry);
            JsonCatalog.RegisterJson(registry);
        }
        finally { registry.EndBuiltinRegistration(); }
    }

    internal static bool IsStandard(string signature) => Canonical.Value.Contains(signature);
}

/// <summary>Short compatibility alias for the built-in standard binder.</summary>
public static class Std
{
    public static void RegisterStd(Registry registry) => StandardCatalog.RegisterStd(registry);
    public static IReadOnlyCollection<string> Signatures => StandardCatalog.Signatures;
}
