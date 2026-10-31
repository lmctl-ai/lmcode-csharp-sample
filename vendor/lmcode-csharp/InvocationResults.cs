using System.Text;
namespace LmCode;

public sealed record InvocationRegistration(string File,int Index);

/// <summary>One invocation, independently owned per-program sinks. Serialize host bookkeeping.</summary>
public sealed class InvocationResults
{
    private sealed class Slot {public ResultSink? Sink;public string? Result;}
    private sealed record Failure(string Phase,Error Error,int? Part);
    private readonly Slot[] slots;
    private readonly bool builder;
    private Failure? failure;
    private bool finalized;
    public IReadOnlyList<InvocationRegistration> Registrations {get;}
    private InvocationResults(bool builder,IEnumerable<InvocationRegistration> registrations,int count) {
        this.builder=builder;Registrations=Array.AsReadOnly(registrations.ToArray());slots=Enumerable.Range(0,count).Select(_=>new Slot()).ToArray();
    }
    private static Error Invalid(string text)=>new("CLI_USAGE","lmcode invocation: "+text);
    public static InvocationResults FromMetadata(string raw,IReadOnlyList<string> parsedFiles) => FromMetadata(new UTF8Encoding(false,true).GetBytes(raw),parsedFiles);
    private static InvocationResults FromMetadata(byte[] raw,IReadOnlyList<string> parsedFiles) {
        try {
            using var doc=InvocationJson.Parse(raw);var root=doc.RootElement;InvocationJson.Keys(root,"version","mode","parts");
            if(!root.TryGetProperty("version",out var version)||!InvocationJson.Integer(version,1))throw new Exception("unsupported version");
            string? mode=root.TryGetProperty("mode",out var modeNode)&&modeNode.ValueKind==System.Text.Json.JsonValueKind.String?modeNode.GetString():null;if(mode is not ("single" or "builder"))throw new Exception("invalid mode");
            root.TryGetProperty("parts",out var parts);if(parts.ValueKind!=System.Text.Json.JsonValueKind.Array||parts.GetArrayLength()==0||(mode=="single"&&parts.GetArrayLength()!=1))throw new Exception("invalid parts");
            var files=new List<string>();
            foreach(var part in parts.EnumerateArray()) {
                InvocationJson.Keys(part,"index","file");if(!part.TryGetProperty("index",out var index)||!InvocationJson.Integer(index,files.Count))throw new Exception("noncontiguous index");
                string file=part.TryGetProperty("file",out var fileNode)&&fileNode.ValueKind==System.Text.Json.JsonValueKind.String?fileNode.GetString()!:"";
                if(file.Contains('\0')||!Path.IsPathFullyQualified(file))throw new Exception("file must be absolute");
                if(files.Contains(file,StringComparer.Ordinal))throw new Exception("duplicate file");files.Add(file);
            }
            if(parsedFiles.Count!=files.Count)throw new Exception("registration count mismatch");
            var registrations=new List<InvocationRegistration>();var seen=new HashSet<int>();
            foreach(string file in parsedFiles){int index=files.IndexOf(file);if(index<0)throw new Exception("extra registration");if(!seen.Add(index))throw new Exception("duplicate registration");registrations.Add(new(file,index));}
            return new(mode=="builder",registrations,files.Count);
        }catch(Exception e) when(e is not OutOfMemoryException){throw Invalid(e.Message);}
    }
    public static InvocationResults FromCommand(IReadOnlyList<string> parsedFiles)=>FromCommand(parsedFiles,Environment.GetEnvironmentVariable("LMCODE_INVOCATION_FILE"));
    // Null explicitly selects standalone semantics; present-empty metadata is invalid.
    public static InvocationResults FromCommand(IReadOnlyList<string> parsedFiles,string? manifest) {
        if(manifest is not null) {
            try {_=ResultJson.Quote(manifest);if(manifest.Length==0||manifest.Contains('\0')||!Path.IsPathFullyQualified(manifest))throw new Exception("manifest path must be absolute");
                return FromMetadata(System.IO.File.ReadAllBytes(manifest),parsedFiles);
            }catch(Error){throw;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw Invalid("cannot read manifest");}catch(Exception e)when(e is not OutOfMemoryException){throw Invalid(e.Message);}
        }
        if(parsedFiles.Count==0)throw Invalid("empty parts");
        foreach(string file in parsedFiles){if(string.IsNullOrEmpty(file)||file.Contains('\0'))throw Invalid("invalid command file");try{_=ResultJson.Quote(file);}catch(Error){throw Invalid("file must be UTF-8");}}
        return new(parsedFiles.Count>1,parsedFiles.Select((file,index)=>new InvocationRegistration(file,index)),parsedFiles.Count);
    }
    private Slot Get(int index)=>index>=0&&index<slots.Length?slots[index]:throw Error.Host("lmcode invocation: invalid slot index");
    private void Active(){if(finalized)throw Error.Host("lmcode invocation: finalized");}
    public ResultSink Select(int index,Registry registry,ResultContract? contract=null) {
        Active();var slot=Get(index);contract??=new();
        if(slot.Sink is null){if(failure is not null)throw Error.Host("lmcode invocation: cannot start work after failure");slot.Sink=new ResultSink(contract);}
        slot.Sink.Writable();if(slot.Sink.Contract!=contract)throw Error.Host("lmcode invocation: result contract changed");
        registry.SetResultSink(slot.Sink);return slot.Sink;
    }
    public void Complete(int index) {
        Active();var slot=Get(index);if(slot.Sink is null)throw Error.Host("lmcode invocation: slot was not selected");
        try{slot.Result=slot.Sink.Seal();}catch(Error e){Fail(index,"run",e);throw;}
    }
    public void Fail(int? index,string phase,Error error) {
        Active();if(failure is not null)return;if(index.HasValue)_=Get(index.Value);
        _=ResultFrames.FailureJson(phase,error);failure=new(phase,error.Snapshot(),builder?index:null);
    }
    public int WriteFrame(Stream output) {
        if(finalized)return 4;
        try {
            if(failure is null)for(int i=0;i<slots.Length;i++)if(slots[i].Result is null){Fail(i,"run",Error.Host("lmcode invocation: slot not completed"));break;}
            string json;int code=0;
            if(failure is not null){json=ResultFrames.FailureJson(failure.Phase,failure.Error,failure.Part);code=ResultFrames.ExitCode(failure.Phase);}
            else json="{\"ok\":true,\"result\":"+(builder?"["+string.Join(',',slots.Select(s=>s.Result))+"]":slots[0].Result)+(builder?",\"parts\":"+slots.Length:"")+"}";
            finalized=true;return ResultFrames.Emit(output,json,code);
        }catch(Exception){finalized=true;ResultFrames.OutputError();return 4;}
    }
}

/// <summary>Terminal invocation envelopes; never called implicitly by the VM.</summary>
public static class ResultFrames
{
    internal static int ExitCode(string phase)=>phase switch {"cli"=>2,"load" or "run"=>3,"app"=>4,_=>throw Error.Host("lmcode invocation: invalid failure phase")};
    internal static string FailureJson(string phase,Error e,int? part=null) {
        _=ExitCode(phase);
        return "{\"ok\":false,\"phase\":"+ResultJson.Quote(phase)+",\"code\":"+ResultJson.Quote(e.Code)+",\"message\":"+ResultJson.Quote(e.Message)+(e.Line.HasValue?",\"line\":"+e.Line+(e.Column.HasValue?",\"column\":"+e.Column:""):"")+(part.HasValue?",\"part\":"+part:"")+"}";
    }
    internal static void OutputError(){try{Console.Error.WriteLine("lmcode invocation: cannot write envelope");}catch(Exception){}}
    internal static int Emit(Stream output,string json,int code) {
        try{byte[] bytes=new UTF8Encoding(false,true).GetBytes("LMCODE_RETURN_BEGIN\n"+json+"\nLMCODE_RETURN_END\n");output.Write(bytes);output.Flush();return code;}
        catch(Exception){OutputError();return 4;}
    }
    public static int FailureFrame(Stream output,string phase,Error error)=>Emit(output,FailureJson(phase,error),ExitCode(phase));
}
