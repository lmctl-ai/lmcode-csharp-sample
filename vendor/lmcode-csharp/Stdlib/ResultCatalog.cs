namespace LmCode.Stdlib;

/// <summary>Write-only result imports bound to the registry's invocation sink.</summary>
public static class ResultCatalog
{
    public static void RegisterResults(Registry registry)
    {
        registry.BeginCompatibilityRegistration();
        try
        {
            foreach (string type in new[] {"int","float","bool","string","Json","StrList","IntList","FloatList","StrMap","IntMap","FloatMap"})
            {
                registry.Function("lmcode_return_set",[type],"void",(context,args)=>{registry.RequiredResultSink().Set(context,args[0]);return Value.None;});
                registry.Function("lmcode_return_put",["string",type],"void",(context,args)=>{registry.RequiredResultSink().Put(context,args[0].AsStr(),args[1]);return Value.None;});
            }
            registry.Function("lmcode_return_null",[],"void",(_,_)=>{registry.RequiredResultSink().SetNull();return Value.None;});
            registry.Function("lmcode_return_put_null",["string"],"void",(_,args)=>{registry.RequiredResultSink().PutNull(args[0].AsStr());return Value.None;});
        }
        finally {registry.EndCompatibilityRegistration();}
    }
}
