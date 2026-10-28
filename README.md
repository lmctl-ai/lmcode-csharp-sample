# lmcode C# shop sample

**Agent entry point:** this is a small .NET 8 host for the lmcode VM. It uses
the checked-in SDK in [vendor/lmcode-csharp](vendor/lmcode-csharp) and the
portable corpus in [sample-suite](sample-suite); no sibling checkout is needed
to build it.

```sh
python3 --version
npm install --global @lmctl-ai/lmctl@0.2.426   # Node 24+
dotnet build src/LmCode.Sample.csproj -c Release
./test.sh
dotnet publish src/LmCode.Sample.csproj -c Release -r linux-x64 --self-contained true -p:PublishAot=true
```

Case 067 uses R47/R48 rule methods; reproducing its pinned bytecode needs a rule-methods-capable compiler,
so set `LMSCRIPT_BIN` to a current lmctl-mono `lmscript` build until a containing release is published
(see [sample-suite/README.md](sample-suite/README.md)). Runtime execution of the pinned bytecode works on
any compiler version, since annotations and interface bindings are compile-time metadata only.

## Write a host in C# in ten steps

1. Read the [portable contract](sample-suite/CONTRACT.md) and choose domain
   objects whose lifetimes are owned by one host process.
2. Start with [host.schema.json](sample-suite/host.schema.json); it declares
   contexts, object types, overloads, operators, the `Currency` enum and one
   SAM interface (`Adjuster`, `(Money) -> void`) under `interfaces`.
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

| Feature | Corpus programs |
|---|---|
| Literals, arithmetic, conditions, loops, functions, switches | 001–008, 036, 039, 041–045 |
| Overloads, objects, enums, operators, conversions, fluent and void methods | 009–014, 034–035, 040 |
| Every standard collection, math, string and file signature | 015–024, 037–038, 048 |
| Located VM/host errors, numeric assertions, application failure | 025–033 |
| Raw/dedented heredocs and all outer splice types | 046–047 |
| Native byte/int64/float/handle/collection boundaries and VM limits | 049–066 |
| `@NAME` rule method annotation and `implements` SAM interface | 067 |
| Typed maps, Stack/Queue/PQ and immutable JSON | 101–108 |
| Typed list/map/JSON literals and binding compatibility | 201–205 |
| Strict Base64/hex and independent map snapshots | 301–305, 311–313 |
| Malformed bytecode, schema binding and wrong-handle imports | 090–092 |

The [full canonical matrix](sample-suite/README.md) maps every source, bytecode
and oracle. All 181 v3 standard signatures and the current domain/testing signatures are exercised.
[Protocol.cs](src/Protocol.cs) preserves raw note bytes in the result JSON;
text error messages use the SDK's Unicode error API.

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

NativeAOT prerequisites are documented by [Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).


Catalog v3 is pinned with the SDK and the canonical 91-case corpus. The merged
schema now carries the `interfaces` key, which the published compiler 0.2.426
rejects as an unknown top-level key; reproducing the pinned bytecode needs a
rule-methods-capable compiler via `LMSCRIPT_BIN` until a containing release is
published. The published compiler's implicit standard bundle is still v1. The
SDK separately tests implicit v3 signatures with an authoritative compiler-main
scratch build. Tests never rewrite oracle files.


V3 byte codecs operate on raw `VmString` data: Base64 is canonical padded RFC
4648, hex output is lowercase, and invalid alphabets/padding/whitespace trap.
Map `keys()`/`values()` return independent mutable snapshots in insertion order.
Every one of the 181 standard signatures is imported by this corpus.

Public CI runs all 91 pinned programs with exact stdout/exit checks. Programs
201–205 need `lmcode-literals-v1` and program 067 needs `lmcode-rule-methods-v1`
(R47/R48); the published compiler 0.2.426 lacks both, and the canonical
reproduction checker fails explicitly on the unsupported features instead of
skipping or hiding them. For a compiler main build, require every source to
reproduce:

```sh
LMSCRIPT_BIN=/path/to/lmscript LMCODE_REQUIRE_LITERAL_COMPILER=1 ./test.sh
```

Local compiler-main validation reproduces all 91/91 bytecode files; expectations
remain the unmodified committed C++ reference snapshots.
