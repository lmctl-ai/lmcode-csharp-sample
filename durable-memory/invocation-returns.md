# Multi-program invocation protocol — slice 7

## Host behavior

`Program.cs` parses repeated `--lmcode FILE`, `--first FILE`, and `--last FILE`
pairs. One Host, Context and Ledger are shared across all parts. Role flags set
stable execution priorities; `InvocationResults` retains one result sink per
canonical input index and emits results in add order. Load/run failures retain
their part association, global application validation omits it, and failure
envelopes carry no partial result.

Success prints one tagged `host_summary` diagnostic, then exactly one SDK result
envelope. The SDK owns frame bytes, aggregate order, failure shape, and exit code.
Keep note diagnostics byte-preserving, including invalid UTF-8, and do not reset a
part sink between body and selected entry calls.

## Pins and verification

- SDK `lmcode-csharp` is pinned in `sdk.lock` to the H4 dry-slot implementation.
- Canonical suite `lmcode-cpp` is pinned in `suite.lock` to H0 dry fixtures.
- Build local compiler checks from a private Mono main worktree; published compiler
  pins may lag the language API.

The sample gate checks the 123-case byte/exit corpus, reusable single/builder
programs, shared ledger state, tagged summaries, result contracts, note bytes,
examples, and compiled production-copy adapter mutants. NativeAOT is qualified only
on platforms explicitly run locally.

## H6 dry-mode adoption

The adapter parses `InvocationResults.FromCommand` before constructing `Host`, then
copies `results.Dry` to `Context` before any host setup. Dry setup leaves live shop
items unseeded. The `money` overloads provide previews, `Money.amount` and
`sample_tmp` use safe read callbacks, and `Ledger.record`/file writes skip by
default. `scripts/test-dry-host.py` runs one private lmcode program in dry and real
modes: dry preserves the ledger and file sentinel, checks preview counts, and emits
the required dry envelope; real mode still updates state and writes the file.
