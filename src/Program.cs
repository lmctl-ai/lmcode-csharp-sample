using System.Globalization;
using System.Reflection;
using System.Text;
using LmCode;
using Sample;
using VmProgram = LmCode.Program;

// Source compilation is lmscript's job. This executable only discovers its API
// and loads schema-bound bytecode; the SDK attaches source locations to VM errors.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = new UTF8Encoding(false);
Console.Out.NewLine = "\n"; // Portable protocol bytes, including Windows.
try
{
    using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("sample.schema")!;
    using var schemaStream = new MemoryStream();
    resource.CopyTo(schemaStream);
    byte[] schema = schemaStream.ToArray();
    var host = new Host();
    if (args is ["--version"]) { Console.WriteLine("lmcode-csharp-sample " + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion); return 0; }
    if (args is ["--lmcode-schema"] or ["--lmcode-schema", "--json"])
    {
        Console.OpenStandardOutput().Write(schema);
        return 0;
    }
    if (args is ["--lmcode-signatures"])
    {
        foreach (string signature in host.Registry.Signatures()) Console.WriteLine(signature);
        return 0;
    }
    if (args is not ["--lmcode", var path])
    {
        Console.WriteLine(Reports.ErrorJson("cli", new Error("CLI_USAGE", "usage: --version | --lmcode-schema [--json] | --lmcode-signatures | --lmcode FILE")));
        return 2;
    }

    // One result belongs to this logical invocation, outside per-call VM arenas.
    var resultSink = new ResultSink();
    host.Registry.SetResultSink(resultSink);

    VmProgram program;
    try { program = VmProgram.Load(File.ReadAllBytes(path), schema, host.Registry); }
    catch (Error error)
    {
        string message = error.Code switch
        {
            "LOAD_SCHEMA_MISMATCH" => "sample schema mismatch",
            "LOAD_UNBOUND" => "sample import mismatch",
            _ => "invalid sample bytecode"
        };
        return Reports.ReportError("load", new Error(error.Code, message));
    }
    try { program.Run(host.Context, host.Registry); }
    catch (Error error) { return Reports.ReportError("run", error); }
    int entriesCalled = 0;
    var entries = program.Entries;
    void CallEntry(int index, Value argument)
    {
        Value result = program.Call(index, host.Context, host.Registry, [argument]);
        entriesCalled++;
        if (result.Kind != ValueKind.None) Protocol.EntryResult(entries[index].Name, result);
    }
    try
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Annotation == "item")
                foreach (ObjRef item in host.Shop.Items.Values) CallEntry(i, Value.Object("Money", item));
            else if (entries[i].Annotation == "total") CallEntry(i, host.TotalHandle());
        }
    }
    catch (Error error) { return Reports.ReportError("run", error); }
    if (host.Ledger.Balance < 0) return Reports.ReportAppFailure("ledger: negative balance");
    int status = Protocol.Success(host.Ledger, entriesCalled);
    // Errors return above: dispatch, HALT diagnostics and app validation precede framing.
    ResultFrames.Write(Console.OpenStandardOutput(), resultSink);
    return status;
}
catch (Exception)
{
    return Reports.ReportAppFailure("cannot read sample input");
}
