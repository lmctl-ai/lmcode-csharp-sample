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

- SDK `lmcode-csharp` at `ba73ed5c36c479a60c6ae8c8d674c55fea73234d`.
- Canonical suite `lmcode-cpp` at `afae84a49d3c5a9557080a04a1e41f990b4db8ca`.
- Build local compiler checks from a private Mono main worktree; published compiler
  pins may lag the language API.

The sample gate checks the 123-case byte/exit corpus, reusable single/builder
programs, shared ledger state, tagged summaries, result contracts, note bytes,
examples, and compiled production-copy adapter mutants. NativeAOT is qualified only
on platforms explicitly run locally.
