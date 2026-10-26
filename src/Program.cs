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
    if (args is ["--version"]) { Console.WriteLine("lmcode-csharp-sample 0.1.0"); return 0; }
    if (args is ["--dsl-schema"] or ["--dsl-schema", "--json"])
    {
        Console.OpenStandardOutput().Write(schema);
        return 0;
    }
    if (args is ["--dsl-signatures"])
    {
        foreach (string signature in host.Registry.Signatures()) Console.WriteLine(signature);
        return 0;
    }
    if (args is not ["--dsl", var path])
    {
        Console.WriteLine(Reports.ErrorJson("cli", new Error("CLI_USAGE", "usage: --version | --dsl-schema [--json] | --dsl-signatures | --dsl FILE")));
        return 2;
    }

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
    if (host.Ledger.Balance < 0) return Reports.ReportAppFailure("ledger: negative balance");
    return Protocol.Success(host.Ledger);
}
catch (Exception)
{
    return Reports.ReportAppFailure("cannot read sample input");
}
