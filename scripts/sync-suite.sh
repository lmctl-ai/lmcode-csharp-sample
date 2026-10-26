#!/usr/bin/env bash
set -euo pipefail
echo 'The C# sample vendors the canonical lmcode-cpp/sample-suite; update sample-suite and suite.lock together after review.'
exec python3 "$(dirname "$0")/sync.py" check
