#!/usr/bin/env bash
set -euo pipefail
echo 'The C# sample vendors SDK bytes; update vendor/lmcode-csharp and sdk.lock together after review.'
exec python3 "$(dirname "$0")/sync.py" check
