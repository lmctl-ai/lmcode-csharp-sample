# Invocation return cutover — slice 7

## Ownership and output

Program.cs constructs one ResultSink, binds it with Registry.SetResultSink before
body execution, and retains it across all selected item/total calls. ResultSink
is outside the standard collection arenas. HALT/entry diagnostics and shop domain
state are unchanged. Only successful app validation reaches ResultFrames.Write,
after Protocol.Success. Failure paths return before framing. Raw stdout writes
preserve LF bytes under Windows and NativeAOT.

## Pins and compiler

SDK: lmcode-csharp 3f893cee64b674f9b91ba633d4019c8ea5ea2f27.
Suite: lmcode-cpp 249af7744ab95caa20094493a2934a3916f46956.
Its suite bytes are identical to slice 6 at 6ee3d62.
Both snapshots came through this repo's own sync scripts. The canonical suite
uses out=ARGV[0], not savedsl. Local examples and the raw-note wrapper follow it.
Build a mono main compiler at 0bc80277 or later; the adoption was qualified with
177e38ca. The npm 0.2.448 compiler lacks this syntax. Do not lower the compiler
gate or rewrite expectations to accommodate the old compiler.

## Regression evidence

Before implementation, the old host succeeded on case 001 but the independent
canonical oracle rejected missing terminal frame. The host mutation script
first checks exact baseline output/exit/stderr for null, shortest float,
entry-lifetime and failed entry/app cases. It then builds production-copy hosts
which omit framing or reset the sink before every entry. Both must keep their
normal successful exit and empty stderr but fail the precise independent oracle.
Build failures/crashes are harness errors, never caught mutants.

The normal gate runs ten Python/Node frame-oracle controls, 123 canonical cases,
bytecode reproduction, signature reachability, runner controls, raw-note bytes,
and annotated/text-template examples. RUN_NATIVE=1 repeats the execution suite
and host examples with NativeAOT. Only the native platform actually run locally
is qualified; other OS results require their own compatible compiler and gate.
