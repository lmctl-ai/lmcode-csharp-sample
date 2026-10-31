using System.Globalization;
using System.Reflection;
using System.Text;
using LmCode;
using Sample;
using VmProgram = LmCode.Program;

// Source compilation is lmscript's job. This executable discovers its API and
// loads schema-bound bytecode; the SDK attaches source locations to VM errors.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = new UTF8Encoding(false);
Console.Out.NewLine = "\n";

static int Failure(string phase, Error error) => ResultFrames.FailureFrame(Console.OpenStandardOutput(), phase, error);
static int Usage() => Failure("cli", new Error("CLI_USAGE", "usage: --version | --lmcode-schema [--json] | --lmcode-signatures | [--first|--lmcode|--last] FILE ..."));

try
{
    using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("sample.schema")!;
    using var schemaStream = new MemoryStream();
    resource.CopyTo(schemaStream);
    byte[] schema = schemaStream.ToArray();
    string schemaText = Encoding.UTF8.GetString(schema);
    var host = new Host();

    if (args is ["--version"])
    {
        Console.WriteLine("lmcode-csharp-sample " + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        return 0;
    }
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

    var parsedFiles = new List<string>();
    var priorities = new List<int>();
    for (int i = 0; i < args.Length; i += 2)
    {
        string flag = args[i];
        if (i + 1 >= args.Length || flag is not ("--lmcode" or "--first" or "--last")) return Usage();
        parsedFiles.Add(args[i + 1]);
        priorities.Add(flag == "--first" ? 0 : flag == "--last" ? 2 : 1);
    }
    if (parsedFiles.Count == 0) return Usage();

    InvocationResults results;
    try { results = InvocationResults.FromCommand(parsedFiles); }
    catch (Error error) { return Failure("cli", error); }
    var contract = ResultContract.FromSchema(schemaText);
    var executionOrder = Enumerable.Range(0, parsedFiles.Count).OrderBy(index => priorities[index]);
    int entriesCalled = 0;
    bool failed = false;

    foreach (int position in executionOrder)
    {
        InvocationRegistration part = results.Registrations[position];
        string phase = "load";
        try
        {
            VmProgram program;
            try { program = VmProgram.Load(File.ReadAllBytes(part.File), schema, host.Registry); }
            catch (Error error)
            {
                string message = error.Code switch
                {
                    "LOAD_SCHEMA_MISMATCH" => "sample schema mismatch",
                    "LOAD_UNBOUND" => "sample import mismatch",
                    _ => "invalid sample bytecode"
                };
                throw new Error(error.Code, message);
            }

            results.Select(part.Index, host.Registry, contract);
            phase = "run";
            program.Run(host.Context, host.Registry);
            var entries = program.Entries;
            void CallEntry(int index, Value argument)
            {
                Value value = program.Call(index, host.Context, host.Registry, [argument]);
                entriesCalled++;
                if (value.Kind != ValueKind.None) Protocol.EntryResult(entries[index].Name, value);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Annotation == "item")
                    foreach (ObjRef item in host.Shop.Items.Values) CallEntry(i, Value.Object("Money", item));
                else if (entries[i].Annotation == "total") CallEntry(i, host.TotalHandle());
            }
            results.Complete(part.Index);
        }
        catch (Error error)
        {
            results.Fail(part.Index, phase, error);
            failed = true;
            break;
        }
        catch (Exception)
        {
            results.Fail(part.Index, "app", new Error("APP_FAIL", "cannot read sample input"));
            failed = true;
            break;
        }
    }

    if (!failed && host.Ledger.Balance < 0)
    {
        results.Fail(null, "app", new Error("APP_FAIL", "ledger: negative balance"));
        failed = true;
    }
    if (!failed) Protocol.HostSummary(host.Ledger, entriesCalled);
    return results.WriteFrame(Console.OpenStandardOutput());
}
catch (Error error) { return Failure("cli", error); }
catch (Exception) { return Failure("app", new Error("APP_FAIL", "cannot read sample input")); }
