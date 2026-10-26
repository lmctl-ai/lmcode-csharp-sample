# lmcode C# shop sample

**Agent entry point:** this is a small .NET 8 host for the lmcode VM. It uses
the checked-in SDK in [vendor/lmcode-csharp](vendor/lmcode-csharp) and the
portable corpus in [sample-suite](sample-suite); no sibling checkout is needed
to build it.

```sh
python3 --version
npm install --global @lmctl-ai/lmctl@0.2.420   # Node 24+
dotnet build src/LmCode.Sample.csproj -c Release
./test.sh
dotnet publish src/LmCode.Sample.csproj -c Release -r linux-x64 --self-contained true -p:PublishAot=true
```

## Write a host in C# in ten steps

1. Read the [portable contract](sample-suite/CONTRACT.md) and choose domain
   objects whose lifetimes are owned by one host process.
2. Start with [host.schema.json](sample-suite/host.schema.json); it declares
   contexts, object types, overloads, operators and the `Currency` enum.
3. Add a .NET 8 executable project referencing `vendor/lmcode-csharp/LmCode.csproj`.
4. Read [the domain model](src/Shop.cs) and [the CLI](src/Program.cs), then
   register contexts and callbacks with `Registry.Context`, `Function` and
   `Method`; signatures must match the schema exactly.
5. Keep handles in `Context`: call `SetObject` for owned objects and use
   `Context.Get<T>` inside callbacks. `Context` owns the slots; the registry
   only owns callback declarations.
6. Map values explicitly: `Value.Int`, `Value.Float`, `Value.Bool`,
   `Value.Str(VmString.FromString(...))`, `Value.Object`, or `Value.None`.
7. Treat `VmString` as bytes. Use `Span` for byte-preserving operations and
   `ToString()` only when the host intentionally decodes UTF-8.
8. Load bytecode with the exact UTF-8 schema bytes using `Program.Load`, then
   run it with a fresh `Context`; normalize only the documented loader messages.
9. Emit one JSON result line and the specified exit code: 0 success, 2 CLI,
   3 VM/load, and 4 application/setup failure. Keep stderr empty.
10. Run [test.sh](test.sh), which locks the SDK/corpus and executes every case
    with byte-identical stdout and exit-code checks.

## Feature coverage

The host covers the same 50 corpus cases as the C++ reference: primitive
control flow (001–008), Money/Shop/Ledger objects and overloads (009–014,
034–045), every standard collection/math/string/file operation (015–024,
037–038, 048), errors and application failures (025–033), and heredoc/splice
forms (046–047). Cases 090–091 verify malformed bytecode and schema binding.

## Conversions

| lmcode value | C# representation | Rule |
|---|---|---|
| `string` | `VmString` | Preserve raw bytes; decode UTF-8 deliberately. |
| `int` | `long` | Signed 64-bit two's-complement arithmetic wraps. |
| `float` | `double` | Preserve signed zero and non-finite values; format with invariant `G15`. |
| `bool` | `bool` | Convert only through `Value.AsBool()`. |
| object handle | `ObjRef` + `Context.Get<T>` | `Context` owns the type-safe object slot. |
| list/map | SDK collection handles | Fresh per run; callbacks return `Value.Object`. |
| void | `Value.None` | Never substitute a null object. |
| host/VM error | `LmCode.Error` | Preserve code and source location at the boundary. |

The canonical corpus and expected outputs are vendored snapshots. Update them
only after a reviewed change in `lmctl-ai/lmcode-cpp`; never edit an expected
output in this repository to make a failing host appear green.

The Money float overload is deliberately narrower than a general lmcode float:
it accepts finite, in-range units and rounds half away from zero to signed
int64 cents. NativeAOT uses the same source and can be checked with the publish
command above; CI proves Linux, macOS and Windows artifacts.
