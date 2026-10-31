using System.Globalization;
using System.Text;
using LmCode.Stdlib;

namespace LmCode;

/// <summary>Invocation-owned JSON result, independent of VM collection regions.</summary>
public sealed class ResultSink
{
    private const long SafeInteger = 9_007_199_254_740_991;
    private JsonCatalog.Node root = new();
    internal static Error Invalid(string path, string reason) => Error.Host($"lmcode return: invalid JSON value at {path}: {reason}");
    internal static string Pointer(string parent, string key) => parent + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string Key(VmString key, string path)
    {
        if (key is null) throw Invalid(path,"handle");
        try { return new UTF8Encoding(false,true).GetString(key.Span); }
        catch (DecoderFallbackException) { throw Invalid(path,"utf8"); }
    }
    public void Reset() => root = new();
    public void SetNull() => Reset();
    public void Set(Context context, Value value) => root = CopyValue(context,value,"");
    public void Put(Context context, VmString key, Value value)
    {
        string name = FieldKey(key);
        var owned = CopyValue(context,value,Pointer("",name));
        PutOwned(key,owned);
    }
    public void PutNull(VmString key) { _ = FieldKey(key); PutOwned(key,new()); }
    private string FieldKey(VmString key)
    {
        if (root.Kind is not ("null" or "object")) throw Error.Host("lmcode return: field assignment requires object or null");
        return Key(key,"");
    }
    private void PutOwned(VmString key, JsonCatalog.Node owned)
    {
        if (root.Kind == "null") {
            var next = new JsonCatalog.Node {Kind="object",Raw=""};
            next.Object.Add((new VmString(key.Span),"",owned));
            root=next;return;
        }
        for (int i=0;i<root.Object.Count;i++)
            if (root.Object[i].Key.Equals(key)) { root.Object[i]=(root.Object[i].Key,"",owned); return; }
        root.Object.Add((new VmString(key.Span),"",owned));
    }
    public string Json() => Encode(root);
    private static JsonCatalog.Node Integer(long value,string path)
    {
        if (value < -SafeInteger || value > SafeInteger) throw Invalid(path,"unsafe_integer");
        return new() {Kind="int",Integer=value,Raw=""};
    }
    private static JsonCatalog.Node Floating(double value,string path)
    {
        _ = ResultJson.FormatNumber(value,path);
        return new() {Kind="float",Float=value == 0 ? 0 : value,Raw=""};
    }
    private static JsonCatalog.Node String(VmString value,string path)
    {
        if (value is null) throw Invalid(path,"handle");
        _ = ResultJson.Quote(value,path);
        return new() {Kind="string",Text=new VmString(value.Span),Raw=""};
    }
    private static T Host<T>(Context context,ObjRef reference,string path) where T:class
    {
        try { return context.Get<T>(reference); }
        catch (Error) { throw Invalid(path,"handle"); }
    }
    private static JsonCatalog.Node CopyValue(Context context,Value value,string path)
    {
        switch (value.Kind)
        {
            case ValueKind.None: return new();
            case ValueKind.Int: return Integer(value.AsInt(),path);
            case ValueKind.Float: return Floating(value.AsFloat(),path);
            case ValueKind.Bool: return new() {Kind="bool",Bool=value.AsBool(),Raw=""};
            case ValueKind.Str: return String(value.AsStr(),path);
            case ValueKind.Obj: break;
            default: throw Invalid(path,"handle");
        }
        ObjRef self=value.AsObj();
        try { if (context.TypeName(self)!=value.TypeName) throw Invalid(path,"handle"); }
        catch (ArgumentOutOfRangeException) { throw Invalid(path,"handle"); }
        var result=new JsonCatalog.Node {Raw=""};
        switch (value.TypeName)
        {
            case "Json":
                JsonCatalog.Node source;
                try { source=JsonCatalog.ResultNode(context,self); }
                catch (Error) { throw Invalid(path,"handle"); }
                return CopyNode(source,path,new(ReferenceEqualityComparer.Instance));
            case "StrList":
                result.Kind="array";
                foreach (var item in Host<StrList>(context,self,path).Values) result.Array.Add(String(item,path+"/"+result.Array.Count.ToString(CultureInfo.InvariantCulture)));
                return result;
            case "IntList":
                result.Kind="array";
                foreach (long item in Host<IntList>(context,self,path).Values) result.Array.Add(Integer(item,path+"/"+result.Array.Count.ToString(CultureInfo.InvariantCulture)));
                return result;
            case "FloatList":
                result.Kind="array";
                foreach (double item in Host<FloatList>(context,self,path).Values) result.Array.Add(Floating(item,path+"/"+result.Array.Count.ToString(CultureInfo.InvariantCulture)));
                return result;
            case "StrMap":
                result.Kind="object";
                foreach (var (key,item) in Host<StrMap>(context,self,path).Entries) Add(result,key,String(item,Pointer(path,Key(key,path))),path);
                return result;
            case "IntMap": case "FloatMap":
                result.Kind="object";
                IReadOnlyList<(VmString Key,Value Value)> entries;
                try { entries=ContainersCatalog.ResultEntries(context,self,value.TypeName); }
                catch (Error) { throw Invalid(path,"handle"); }
                foreach (var (key,item) in entries) Add(result,key,CopyValue(context,item,Pointer(path,Key(key,path))),path);
                return result;
            default: throw Invalid(path,"handle");
        }
    }
    private static void Add(JsonCatalog.Node node,VmString key,JsonCatalog.Node value,string path)
    {
        string name=Key(key,path);
        if (node.Object.Any(member=>member.Key.Equals(key))) throw Invalid(Pointer(path,name),"handle");
        node.Object.Add((new VmString(key.Span),"",value));
    }
    private static bool NonzeroMantissa(string raw)
    {
        foreach (char c in raw) { if (c is 'e' or 'E') break; if (c is >= '1' and <= '9') return true; }
        return false;
    }
    private static JsonCatalog.Node CopyNode(JsonCatalog.Node? node,string path,HashSet<JsonCatalog.Node> active)
    {
        if (node is null) throw Invalid(path,"handle");
        if (!active.Add(node)) throw Invalid(path,"cycle");
        JsonCatalog.Node result;
        switch (node.Kind)
        {
            case "null": result=new();break;
            case "bool": result=new() {Kind="bool",Bool=node.Bool,Raw=""};break;
            case "int":
                if (node.Raw.Length!=0) _=ResultJson.ValidateNumberToken(node.Raw,path);
                result=Integer(node.Integer,path);break;
            case "float":
                result=Floating(node.Float,path);
                if (node.Raw.Length!=0) {
                    if (node.Float==0 && NonzeroMantissa(node.Raw)) throw Invalid(path,"underflow");
                    _=ResultJson.ValidateNumberToken(node.Raw,path);
                }
                break;
            case "string": result=String(node.Text,path);break;
            case "array":
                result=new() {Kind="array",Raw=""};
                foreach (var child in node.Array) result.Array.Add(CopyNode(child,path+"/"+result.Array.Count.ToString(CultureInfo.InvariantCulture),active));
                break;
            case "object":
                result=new() {Kind="object",Raw=""};
                foreach (var member in node.Object) {
                    string name=Key(member.Key,path);
                    if (result.Object.Any(x=>x.Key.Equals(member.Key))) throw Invalid(Pointer(path,name),"handle");
                    Add(result,member.Key,CopyNode(member.Value,Pointer(path,name),active),path);
                }
                break;
            default: throw Invalid(path,"handle");
        }
        active.Remove(node);
        return result;
    }
    private static string Encode(JsonCatalog.Node node) => node.Kind switch
    {
        "null"=>"null", "bool"=>node.Bool?"true":"false",
        "int"=>node.Integer.ToString(CultureInfo.InvariantCulture), "float"=>ResultJson.FormatNumber(node.Float),
        "string"=>ResultJson.Quote(node.Text),
        "array"=>"["+string.Join(',',node.Array.Select(Encode))+"]",
        "object"=>"{"+string.Join(',',node.Object.Select(member=>ResultJson.Quote(member.Key)+":"+Encode(member.Value)))+"}",
        _=>throw Invalid("","handle"),
    };
}

/// <summary>Explicit successful-host framing; never called implicitly by the VM.</summary>
public static class ResultFrames
{
    public static byte[] Encode(ResultSink sink) => new UTF8Encoding(false,true).GetBytes("LMCODE_RETURN_BEGIN\n"+sink.Json()+"\nLMCODE_RETURN_END\n");
    /// <summary>Call only after the body, selected entries and app validation succeed.</summary>
    public static void Write(Stream output,ResultSink sink)
    {
        byte[] frame=Encode(sink);
        try { output.Write(frame); output.Flush(); }
        catch(IOException) { throw Error.Host("lmcode return: cannot write result frame"); }
    }
}
