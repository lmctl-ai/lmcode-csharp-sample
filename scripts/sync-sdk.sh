#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
exec python3 "$root/scripts/sync.py" sdk "${1:-$root/../lmcode-csharp}"
