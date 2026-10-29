# lmcode C# shop sample

**Agent entry point:** this is a small .NET 8 host for the lmcode VM. It uses
the checked-in SDK in [vendor/lmcode-csharp](vendor/lmcode-csharp) and the
portable corpus in [sample-suite](sample-suite); no sibling checkout is needed
to build it.

```sh
npm install -g @lmctl-ai/lmctl
lmscript test.lms
# Equivalent: lmctl test.lms
```

Use .NET 8, Node 24 and Python 3 for vendor-lock verification. The pinned suite
is DSLB v6; current published lmscript compiles its annotated rule entries.

## Write a host in C# in ten steps

1. Read the [portable contract](sample-suite/CONTRACT.md) and choose domain
   objects whose lifetimes are owned by one host process.
2. Start with [host.schema.json](sample-suite/host.schema.json); it declares
   contexts, object types, overloads, operators, the `Currency` enum and two
   SAM interfaces (`Adjuster`, `(Money) -> void`; `Score`, `(Money) -> int`) under `interfaces`.
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
   run its body with the host Context, then enumerate Entries in source order. Call
   item rules for each existing Money handle and total rules with Shop.total();
   ignore other annotations. Preserve entry failures as run-phase errors.
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
| Rule methods, ordered item/total entry dispatch and returned values | 067–069 |
| Typed maps, Stack/Queue/PQ and immutable JSON | 101–108 |
| Typed list/map/JSON literals and binding compatibility | 201–205 |
| Strict Base64/hex and independent map snapshots | 301–305, 311–313 |
| Malformed bytecode, schema binding and wrong-handle imports | 090–093 |

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


V3 byte codecs operate on raw `VmString` data: Base64 is canonical padded RFC
4648, hex output is lowercase, and invalid alphabets/padding/whitespace trap.
Map `keys()`/`values()` return independent mutable snapshots in insertion order.
Every one of the 181 standard signatures is imported by this corpus.

The result field entries_called counts successful rule calls; entries retains its
ledger-record meaning. Value rules print entry NAME = VALUE before the result JSON.
The same total helper is used by the schema callback and entry dispatch; Money
objects remain in the host Context for the entire execution.

## lmscript tests and automatic releases

Install .NET 8, Node 24 and `npm install -g @lmctl-ai/lmctl`. From this repository run `lmscript test.lms` or `lmctl test.lms`; test.sh forwards to lmscript. Set RUN_NATIVE=1 and RUNTIME_ID to linux-x64, linux-arm64, osx-arm64, osx-x64 or win-x64 to include NativeAOT builds and execution (requires the platform native toolchain).

Every main push builds and tests all five platforms, then publishes native executables, licenses and SHA256SUMS to GitHub Releases. Pull requests validate without publishing.

`examples/rule-entries.lms` shows annotated functions implementing Adjuster: the host dispatches @item per item and @total for the total. Case 069 demonstrates returned values. The lmscript driver audits 181 standard and 45 host signatures, reproduces all 94 bytecode cases, runs negative runner controls, and tests managed/native output bytes and the annotated example. `scripts/check-note-bytes.lms` preserves NUL, escaped controls, invalid UTF-8 and multibyte UTF-8 checks. Python is only used for vendored lock maintenance; Node provides binary/process adapters.
