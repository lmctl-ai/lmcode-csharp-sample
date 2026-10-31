# lmcode C# shop sample

## Purpose

This .NET 8 host embeds the checked-in [C# runtime](vendor/lmcode-csharp) and
[canonical portable suite](sample-suite). No sibling checkout is needed to
build or execute it. Source compilation belongs to LMScript; the sample only
loads schema-bound lmcode bytecode (DSLB v6) and exposes its API.

## Host architecture

[Program.cs](src/Program.cs) discovers the exact embedded schema, parses repeated
`--lmcode FILE` inputs plus the `--first`/`--last` role flags, and dispatches all
parts through one Host, Context and Ledger. `@item` entries run for each existing
Money handle and `@total` entries run for Shop.total(); other annotations do not run.
Non-void HALT results still print `entry NAME = VALUE`; `entries_called` remains
separate from ledger records. Negative ledger balance fails application validation.
[Shop.cs](src/Shop.cs) owns domain objects through the host Context.

## Invocation result protocol

Each input selects its own `ResultSink`, retained from its body through its entry
calls. Host state is shared, while results remain in add order even when roles
change execution order. After all parts and application validation pass, the host
prints one tagged `host_summary` diagnostic and the SDK writes one result envelope.

Success ends with exactly one `LMCODE_RETURN_BEGIN`, compact JSON envelope line,
and `LMCODE_RETURN_END`, each LF-terminated. Builder results include an ordered
array and required `parts`; failures have no result payload and retain exits 2/3/4.
`$return` never substitutes for a function's HALT result. Returned floats use the
SDK's shortest round-trip serializer; `0.1 + 0.2` emits `0.30000000000000004`.

### Reusable program example

```lms
schema = load_schema(cli=ARGV[0]);
program = _lmcode_ schema;
  $return.total = shop.total().amount();
end _lmcode_
r = run_lmcode(program);
require(r.ok, "sample host failed");
```

The program is immutable and reusable; each `run_lmcode` call executes it once.
The [full contract](sample-suite/CONTRACT.md) defines dispatch and canonical
test expectations.

## Value boundaries

| Value | C# representation | Boundary |
|---|---|---|
| string | `VmString` | Raw bytes; decode only deliberately. |
| int | `long` | VM arithmetic wraps signed 64-bit; JSON returns require safe mathematical integers. |
| float | `double` | Existing VM/entry text uses invariant G15; result JSON uses shortest round-trip formatting. |
| bool | `bool` | `Value.AsBool()`; no numeric coercion. |
| object | `ObjRef` and `Context.Get<T>` | Context owns typed slots. |
| collection | SDK handles | Per-region storage; result writes capture owned copies. |
| void | `Value.None` | Distinct from a null result payload. |

[Protocol.cs](src/Protocol.cs) preserves raw note bytes in the tagged host summary,
including invalid UTF-8. That diagnostic is separate from the strictly UTF-8
return envelope. Money's float overload accepts finite in-range units and rounds
half away from zero to int64 cents.

## Build and verification

Use .NET 8, Node 24, Python 3 and an LMScript compiler built from mono main
**0bc80277 or later** with the block/out/return cutover. Published compiler
0.2.448 predates those changes. Source builds may report the historical 0.2.38
package version; actual compiler capabilities and source SHA are authoritative.
Set `LMSCRIPT_BIN` to the source-built `lmscript/bin/lmscript`, then run
`./test.sh`. It forwards to the LMScript test driver.

The gate verifies SDK/suite locks, builds the managed host, audits all **206 std
and 45 host signatures**, reproduces all **123 bytecode cases**, runs the shared
runner/frame-oracle controls, and checks exact output/exit/error bytes for every
case. Local annotated, text-template and raw-note-byte examples are also checked.
Production-copy C# mutations must compile, then fail independent frame/result
oracles; compilation errors, signals and unexpected exits do not count as catches.

Set `RUN_NATIVE=1` and `RUNTIME_ID=linux-x64`, `linux-arm64`, `osx-arm64`,
`osx-x64` or `win-x64` to include NativeAOT publishing and repeat the suite and
host examples against that executable. NativeAOT requires the platform native
toolchain. No global linking or installation is part of this repository's gate.

## Snapshot maintenance

`scripts/sync-sdk.sh SOURCE` copies committed C# runtime main bytes;
`scripts/sync-suite.sh SOURCE` copies committed canonical lmcode-cpp main bytes.
Their lock files record immutable source SHAs and hashes. Never record expected
outputs from this port or edit vendored canonical files to make a failure pass.
Refresh them through the existing scripts after a reviewed upstream change.

## Automation boundary

Actions defines five native platforms and installs a compiler selected by its
input/repository variable/pin. The checked-in old npm pin is not evidence of
compatibility with the new source syntax. Remote qualification needs a compatible
compiler; this adoption does not publish one or claim the old pin works.
Existing release automation is unchanged; no tag or release is created manually.
See [durable notes](durable-memory/index.md) for the return cutover and evidence.

## Historical literal names

The documentation calls the language lmcode. `--lmcode`, `--lmcode-schema` and `--lmcode-signatures` are current host flag literals; `DSLB` is the bytecode magic and `DSLB v6` is the current format name; `.dslb` is the current bytecode filename extension. Other code-formatted API names, paths, manifest names and diagnostic text retain their current literal spellings.
