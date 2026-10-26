#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"
python3 -B scripts/sync.py check
dotnet build src/LmCode.Sample.csproj --configuration Release --nologo
python3 -B sample-suite/run-suite.py dotnet "$root/src/bin/Release/net8.0/lmcode-csharp-sample.dll"
python3 -B sample-suite/test_runner.py
