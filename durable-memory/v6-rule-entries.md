# v6 rule-entry sample

Pins: lmcode-csharp d99740d and canonical lmcode-cpp suite d608ba2. The 94-case suite is copied through scripts/sync.py; expectations are never edited locally.

After the body, entries execute in source order. Each item rule receives the existing Money handles in shop order; each total rule gets the same aggregate calculation as Shop.total(). Other annotations never execute. Non-void results use language formatting; string bytes are written without UTF-8 replacement. Entry errors remain run-phase errors. Application validation runs after dispatch. entries_called is distinct from ledger entries.

Before dispatch implementation, the refreshed suite failed 068 (missing seven calls) and 069 (missing four result lines). Afterward, a clean Release build and full test.sh passed 94/94, compiler reproduction 94/94, three compile-error fixtures, runner tests and the raw-note-byte probe. That probe's only protocol change is the required entries_called: 0 field.

Use LMSCRIPT_BIN=/home/mma/repos/lmctl-mono-v6entries/lmscript/bin/lmscript and LMSCRIPT_MIN=0 locally. The task names compiler main 56f19153, but the supplied read-only checkout reports 0e621804; the provided binary reproduced all 94 pinned bytecodes. No compiler checkout or global installation was changed. A different compiler must support v6 and the rule-method/literal feature contracts.
