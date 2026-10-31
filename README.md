# lmcode C# shop sample

## Purpose

This .NET 8 host embeds the checked-in [C# runtime](vendor/lmcode-csharp) and
[canonical portable suite](sample-suite). No sibling checkout is needed to
build or execute it. Source compilation belongs to LMScript; the sample only
loads schema-bound lmcode bytecode (DSLB v6) and exposes its API.

## Host architecture

[Program.cs](src/Program.cs) discovers the exact embedded schema, loads bytecode,
runs the body, then dispatches `@item` entries for each existing Money handle and
`@total` entries for Shop.total(), in source order. Other annotations do not run.
Non-void HALT results still print `entry NAME = VALUE`; `entries_called` remains
separate from ledger records. Negative ledger balance fails application validation.
[Shop.cs](src/Shop.cs) owns domain objects through the host Context.

## Invocation result protocol

The host binds one `ResultSink` to the registry before the body and keeps it
through all selected entry calls. Standard collection regions can end after each
call without destroying owned result copies. Do not reset the sink in CallEntry.
Only after complete dispatch and application validation does the host print the
existing shop diagnostic and call `ResultFrames.Write` on raw stdout.

Success ends with exactly one `LMCODE_RETURN_BEGIN`, compact JSON payload line,
and `LMCODE_RETURN_END`, each LF-terminated. Unset means a mandatory `null` frame.
Load/run/application failures emit no frame and retain exits 2/3/4. `$return`
never substitutes for a function's HALT result. Returned floats use the SDK's
shortest round-trip serializer; `0.1 + 0.2` emits `0.30000000000000004`.

### Compile-only source shape

```lms
program = _lmcode_ schema="../sample-suite/sample.dsl.json" out=ARGV[0];
  $return.total = shop.total().amount();
end _lmcode_
```

The `out` expression writes bytecode without executing the host. A CLI-bound
executing block instead yields the framed value to LMScript. A file-only schema
requires `out`. The [full contract](sample-suite/CONTRACT.md) defines dispatch and
canonical test expectations.

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

[Protocol.cs](src/Protocol.cs) preserves raw note bytes in the shop diagnostic,
including invalid UTF-8. That diagnostic is separate from the strictly UTF-8
return frame. Money's float overload accepts finite in-range units and rounds
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

The documentation calls the language lmcode. `--dsl`, `--dsl-schema` and `--dsl-signatures` are current host flag literals; `DSLB` is the bytecode magic and `DSLB v6` is the current format name; `.dslb` is the current bytecode filename extension. Other code-formatted API names, paths, manifest names and diagnostic text retain their current literal spellings.
