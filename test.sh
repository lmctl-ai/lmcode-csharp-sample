#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"
python3 -B scripts/sync.py check
python3 -B sample-suite/audit.py
dotnet build src/LmCode.Sample.csproj --configuration Release --nologo
python3 -B sample-suite/run-suite.py dotnet "$root/src/bin/Release/net8.0/lmcode-csharp-sample.dll"
python3 -B sample-suite/test_runner.py
python3 -B scripts/check-bytecode.py
python3 -B scripts/check-note-bytes.py dotnet "$root/src/bin/Release/net8.0/lmcode-csharp-sample.dll"
