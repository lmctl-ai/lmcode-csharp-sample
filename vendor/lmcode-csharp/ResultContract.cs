using System.Text;
using System.Text.Json;
namespace LmCode;

// Host schema metadata, never supplied by the invocation descriptor.
public sealed record ResultContract
{
    public string Type { get; private init; } = "";
    public bool Nullable { get; private init; }
    public static ResultContract FromSchema(string raw)
    {
        try {
            using var doc=InvocationJson.Parse(raw,false);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("dsl.schema",out var v) || !InvocationJson.Integer(v,2))throw new Exception("requires schema 2");
            if(!root.TryGetProperty("result",out var r))return new();
            InvocationJson.Keys(r,"type","nullable");
            if(!r.TryGetProperty("type",out var t)||t.ValueKind!=JsonValueKind.String)throw new Exception("type must be a string");
            string type=t.GetString()!;
            if(!new[]{"null","bool","int","float","string","Json","StrList","IntList","FloatList","StrMap","IntMap","FloatMap"}.Contains(type))throw new Exception("unknown type "+type);
            bool nullable=false;
            if(r.TryGetProperty("nullable",out var n)) {
                if(type=="null")throw new Exception("type null forbids nullable");
                if(n.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new Exception("nullable must be bool");nullable=n.GetBoolean();
            }
            return new(){Type=type,Nullable=nullable};
        }catch(Exception e) when(e is not OutOfMemoryException){throw new Error("LOAD_BAD_SCHEMA","lmcode result contract: "+e.Message);}
    }
    private static void Mismatch(string path,string expected,string got)=>throw Error.Host($"lmcode return: result type mismatch at {path}: expected {expected}; got {got}");
    internal static string Source(Value v)=>v.Kind switch {ValueKind.Int=>"int",ValueKind.Float=>"float",ValueKind.Bool=>"bool",ValueKind.Str=>"string",ValueKind.None=>"null",ValueKind.Obj=>v.TypeName,_=>"handle"};
    private static bool Assignable(string actual,string expected)=>actual==expected||(actual=="int"&&expected=="float")||(actual=="IntList"&&expected=="FloatList")||(actual=="IntMap"&&expected=="FloatMap");
    internal void ValidateSource(Value v,string? field=null) {
        string actual=Source(v);if(Type is "" or "Json" || actual=="Json")return;
        string expected=Type,path="";
        if(field is not null) {
            expected=Type switch {"StrMap"=>"string","IntMap"=>"int","FloatMap"=>"float",_=>""};
            if(expected=="")return;path=ResultSink.Pointer("",field);
        } else if(actual=="null")return;
        if(!Assignable(actual,expected))Mismatch(path,expected,actual);
    }
    internal void Validate(string json) {using var doc=InvocationJson.Parse(json);Validate(doc.RootElement,Type,"");}
    private void Validate(JsonElement n,string type,string path) {
        if(type=="")return;
        string kind=n.ValueKind switch {JsonValueKind.Null=>"null",JsonValueKind.True or JsonValueKind.False=>"bool",JsonValueKind.String=>"string",JsonValueKind.Array=>"array",JsonValueKind.Object=>"object",_=> n.GetDouble()==Math.Truncate(n.GetDouble())?"int":"float"};
        if(kind=="null") {if(Nullable||type=="null")return;Mismatch(path,type,kind);}
        if(type=="Json")return;
        if(Primitive(n,type))return;
        bool list=type is "StrList" or "IntList" or "FloatList",map=type is "StrMap" or "IntMap" or "FloatMap";
        if((list&&kind=="array")||(map&&kind=="object")) {
            string element=type.StartsWith("Str",StringComparison.Ordinal)?"string":type.StartsWith("Int",StringComparison.Ordinal)?"int":"float";
            if(list){int i=0;foreach(var child in n.EnumerateArray()){if(!Primitive(child,element))Mismatch(path+"/"+i,element,Kind(child));i++;}}
            else foreach(var p in n.EnumerateObject())if(!Primitive(p.Value,element))Mismatch(ResultSink.Pointer(path,p.Name),element,Kind(p.Value));
            return;
        }
        Mismatch(path,type,kind);
    }
    private static string Kind(JsonElement n)=>n.ValueKind switch {JsonValueKind.Null=>"null",JsonValueKind.True or JsonValueKind.False=>"bool",JsonValueKind.String=>"string",JsonValueKind.Array=>"array",JsonValueKind.Object=>"object",_=>n.GetDouble()==Math.Truncate(n.GetDouble())?"int":"float"};
    private static bool Primitive(JsonElement n,string t)=>t switch {"int"=>n.ValueKind==JsonValueKind.Number&&n.GetDouble()==Math.Truncate(n.GetDouble()),"float"=>n.ValueKind==JsonValueKind.Number,"bool"=>n.ValueKind is JsonValueKind.True or JsonValueKind.False,"string"=>n.ValueKind==JsonValueKind.String,_=>false};
}

internal static class InvocationJson
{
    internal static JsonDocument Parse(string raw,bool numbers=true) => Parse(new UTF8Encoding(false,true).GetBytes(raw),numbers);
    internal static JsonDocument Parse(byte[] raw,bool numbers=true) {
        var node=Stdlib.JsonCatalog.ParseInvocation(raw);
        if(numbers)ResultSink.ValidateNode(node);
        return JsonDocument.Parse(raw,new JsonDocumentOptions{MaxDepth=int.MaxValue});
    }
    internal static bool Integer(JsonElement n,int value)=>n.ValueKind==JsonValueKind.Number&&n.GetDouble()==value;
    internal static void Keys(JsonElement n,params string[] allowed) {if(n.ValueKind!=JsonValueKind.Object)throw new Exception("object required");foreach(var p in n.EnumerateObject())if(!allowed.Contains(p.Name))throw new Exception("unknown key "+p.Name);}
}
