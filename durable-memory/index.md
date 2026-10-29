# C# sample notes

Start with README.md, src/Program.cs and src/Shop.cs. sdk.lock and suite.lock record reviewed SDK/canonical corpus commits and every vendored byte.

- [v6 entry dispatch](v6-rule-entries.md): host dispatch semantics and historical migration evidence.
- Root test.lms is the current test entry point: install npm @lmctl-ai/lmctl and run lmscript test.lms or lmctl test.lms. Python only maintains vendor locks; assertions and orchestration use lmscript.
- RUN_NATIVE=1 with RUNTIME_ID builds and tests NativeAOT as well as managed code. All 94 cases, annotations and raw note bytes must match exactly.
- Actions validates Linux x64/arm64, macOS x64/arm64 and Windows x64 on pushes/PRs. Main publishes native binaries, licenses and SHA256SUMS only after all jobs pass.
