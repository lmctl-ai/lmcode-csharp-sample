#!/usr/bin/env python3
"""Offline dry/real sample-host sentinel, preview-count and parity controls."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "scripts/fixtures"
SCHEMA = ROOT / "sample-suite/sample.dsl.json"


def invoke(host, bytecode, directory, dry):
    manifest = directory / "invocation.json"
    manifest.write_text(json.dumps({
        "version": 1,
        "mode": "single",
        "dry": dry,
        "parts": [{"index": 0, "file": str(bytecode.resolve())}],
    }))
    env = os.environ.copy()
    env.pop("LMCODE_INVOCATION_FILE", None)
    env["LMCODE_INVOCATION_FILE"] = str(manifest.resolve())
    env["SAMPLE_TMP"] = str(directory.resolve())
    return subprocess.run([*host, "--lmcode", str(bytecode.resolve())], cwd=ROOT,
                          env=env, capture_output=True)


def split_output(result):
    begin = b"LMCODE_RETURN_BEGIN\n"
    at = result.stdout.rfind(begin)
    if at < 0:
        raise AssertionError(("missing terminal envelope", result.stdout))
    diagnostic = result.stdout[:at]
    frame = result.stdout[at:]
    end = b"LMCODE_RETURN_END\n"
    if frame.count(begin) != 1 or not frame.endswith(end):
        raise AssertionError(("malformed terminal envelope", result.stdout))
    return diagnostic, json.loads(frame[len(begin):-len(end)])


def main():
    host = sys.argv[1:]
    if not host:
        raise SystemExit("usage: test-dry-host.py HOST [HOST_ARGUMENT ...]")
    with tempfile.TemporaryDirectory(prefix="sample dry sentinel ") as temporary:
        root = Path(temporary)
        lock = json.loads((FIXTURES / "dry-sentinel.lock").read_text())
        source, bytecode = FIXTURES / "dry-sentinel.lms", FIXTURES / "dry-sentinel.dslb"
        for path, field in ((source, "source_sha256"), (bytecode, "bytecode_sha256"),
                            (SCHEMA, "schema_sha256")):
            assert hashlib.sha256(path.read_bytes()).hexdigest() == lock[field], path

        dry_dir, real_dir = root / "dry", root / "real"
        dry_dir.mkdir(); real_dir.mkdir()
        dry = invoke(host, bytecode, dry_dir, True)
        assert dry.returncode == 0 and dry.stderr == b"", (dry.returncode, dry.stdout, dry.stderr)
        dry_diagnostic, dry_envelope = split_output(dry)
        assert not (dry_dir / "dry-sentinel").exists(), "effectful file callback ran in dry mode"
        assert dry_envelope == {"ok": True, "result": {"first": 10, "ledger_entries": 0}, "dry": True}, dry_envelope
        assert dry_diagnostic == (b'{"host_summary":{"balance": 0, "entries": 0, "note": "", '
                                  b'"entries_called": 0, "dry_preview_calls": 2}}\n'), dry_diagnostic

        real = invoke(host, bytecode, real_dir, False)
        assert real.returncode == 0 and real.stderr == b"", (real.returncode, real.stdout, real.stderr)
        real_diagnostic, real_envelope = split_output(real)
        assert (real_dir / "dry-sentinel").read_text() == "written", "real file callback was skipped"
        assert real_envelope == {"ok": True, "result": {"first": 10, "ledger_entries": 1}}, real_envelope
        assert real_diagnostic == (b'{"host_summary":{"balance": 770, "entries": 4, "note": "", '
                                   b'"entries_called": 3}}\n'), real_diagnostic
    print("PASS sample dry sentinel, preview count and unchanged real callbacks")


if __name__ == "__main__":
    main()
