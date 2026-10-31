# C# sample notes

## Current contract

Read README.md, src/Program.cs and src/Shop.cs. sdk.lock and suite.lock pin the
committed runtime and canonical corpus; refresh with the repo's sync scripts.

- [Invocation returns](invocation-returns.md): current sink/frame lifetime,
  compiler requirement, pins and failing controls.
- [v6 entry dispatch](v6-rule-entries.md): historical dispatch migration evidence.

## Verification

Root test.lms is the orchestration entry point, forwarded by test.sh. Use a
source-built mono main compiler with the out/return cutover. Full gates run all
123 canonical cases, managed examples and owned-result host controls.
RUN_NATIVE=1 with RUNTIME_ID adds NativeAOT and repeats the host suite. CI compiler
selection must be upgraded to a compatible compiler before remote qualification;
old npm pin installation alone does not establish capability.
