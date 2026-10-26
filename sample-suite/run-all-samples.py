#!/usr/bin/env python3
"""Build and exercise all lmcode sample ports against one pinned corpus."""

import argparse
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import time


ROOT = Path(__file__).resolve().parent
LANGS = ("c", "cpp", "rust", "go", "csharp", "java", "kotlin", "scala")


def load_env_file(path):
    if not path:
        return {}
    data = json.loads(path.read_text()) if path.suffix == ".json" else {}
    if not data:
        for line in path.read_text().splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            key, sep, value = line.partition("=")
            if not sep or not key.isidentifier() or any(x in value for x in ("$", "`", ";")):
                raise ValueError(f"unsafe environment file line: {line}")
            data[key] = value.strip().strip('"\'')
    if not isinstance(data, dict):
        raise ValueError("environment file must contain an object")
    return {str(k): (json.dumps(v) if isinstance(v, list) else str(v)) for k, v in data.items()}


def command_for(env, lang, repo):
    key = f"SAMPLE_{lang.upper()}_COMMAND"
    if key in env:
        value = json.loads(env[key])
        if not isinstance(value, list) or not all(isinstance(x, str) for x in value):
            raise ValueError(f"{key} must be a JSON argv array")
        return value
    return [str(repo / "build" / f"lmcode-{lang}-sample")]


def build_for(lang, repo, env):
    key = f"SAMPLE_{lang.upper()}_BUILD"
    if key in env:
        value = json.loads(env[key])
        if not isinstance(value, list) or not all(isinstance(x, str) for x in value):
            raise ValueError(f"{key} must be a JSON argv array")
        return value
    if (repo / "build.sh").is_file():
        return ["./build.sh"]
    if (repo / "Makefile").is_file():
        return ["make", "all"]
    raise ValueError(f"{lang}: no build.sh or Makefile")


def execute(argv, cwd, log, env):
    started = time.monotonic()
    with log.open("w") as output:
        try:
            result = subprocess.run(argv, cwd=cwd, env=env, stdout=output,
                                    stderr=subprocess.STDOUT, timeout=int(env.get("SAMPLE_TIMEOUT", 120)))
            code = result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            output.write(str(error) + "\n")
            code = 124
    return code, round(time.monotonic() - started, 2)


def run_case(command, bytecode, expected_out, expected_exit, scratch, env, timeout=30):
    child_env = dict(env, SAMPLE_TMP=str(scratch))
    try:
        result = subprocess.run([*command, "--dsl", str(bytecode)], env=child_env,
                                capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        return {"exit": 124, "stdout": error.stdout or b"", "stderr": b"timeout",
                "expected_exit": expected_exit, "expected_stdout": expected_out}
    return {
        "exit": result.returncode,
        "stdout": result.stdout,
        "stderr": result.stderr,
        "expected_exit": expected_exit,
        "expected_stdout": expected_out,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--all", action="store_true", help="run all eight configured ports")
    parser.add_argument("--env-file", type=Path, default=None)
    args = parser.parse_args()
    if not args.all:
        parser.error("--all is required")

    try:
        file_env = load_env_file(args.env_file or (Path(os.environ["SAMPLE_ENV_FILE"]) if os.environ.get("SAMPLE_ENV_FILE") else None))
        env = dict(os.environ, **file_env)
        root = Path(env.get("SAMPLE_REPO_ROOT", Path.home() / "repos" / "dsl"))
        state = Path(env.get("SAMPLE_STATE", Path.home() / ".local/state/lmctl/sample-matrix"))
        state.mkdir(parents=True, exist_ok=True)
        corpus = Path(env.get("SAMPLE_CORPUS", ROOT))
        manifest = json.loads((corpus / "manifest.json").read_text())
        cases = []
        for item in manifest["cases"]:
            cases.append((item["name"], item, corpus / item["bytecode"],
                          (corpus / "expected" / (item["name"] + ".out")).read_bytes(),
                          int((corpus / "expected" / (item["name"] + ".exit")).read_text())))
        if not cases:
            raise ValueError("manifest contains no sample cases")
    except Exception as error:
        print(f"FAIL setup: {error}", file=sys.stderr)
        return 2

    report = {"ports": {}, "cases": len(cases), "ok": True}
    actual_by_case = {}
    for lang in LANGS:
        repo = Path(env.get(f"SAMPLE_{lang.upper()}_DIR", root / f"lmcode-{lang}-sample"))
        entry = report["ports"][lang] = {"repo": str(repo), "build": None, "cases": [], "ok": False}
        if not repo.is_dir():
            entry["error"] = "missing repository"
            print(f"FAIL {lang}: missing repository")
            report["ok"] = False
            continue
        try:
            build = build_for(lang, repo, env)
            entry["build"] = shlex.join(build)
        except Exception as error:
            entry["error"] = str(error)
            print(f"FAIL {lang}: {error}")
            report["ok"] = False
            continue
        code, seconds = execute(build, repo, state / f"{lang}-build.log", env)
        entry["build_exit"] = code
        entry["build_seconds"] = seconds
        if code:
            entry["error"] = "build failed"
            print(f"FAIL {lang}: build exited {code}")
            report["ok"] = False
            continue
        try:
            command = command_for(env, lang, repo)
            # The port's own protocol and canonical runner are mandatory checks.
            checker = corpus / "run-suite.py" if lang == LANGS[0] else corpus / "protocol.py"
            protocol = subprocess.run([sys.executable, str(checker), *command], env=env,
                                       capture_output=True, text=True, timeout=120)
            entry["protocol_suite_exit"] = protocol.returncode
            if protocol.returncode:
                entry["error"] = "canonical suite failed"
                print(f"FAIL {lang}: canonical suite exited {protocol.returncode}")
                report["ok"] = False
            for name, item, bytecode, expected_out, expected_exit in cases:
                with tempfile.TemporaryDirectory(prefix=f"sample-{lang}-") as scratch:
                    for fixture, content in item.get("fixtures", {}).items():
                        path = Path(scratch) / fixture
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_bytes(bytes.fromhex(content))
                    observed = run_case(command, bytecode, expected_out, expected_exit, scratch, env, int(item.get("timeout_seconds", 30)))
                raw_stdout = observed["stdout"]
                raw_stderr = observed["stderr"]
                observed.update({"name": name, "stdout": raw_stdout.decode(errors="replace"),
                                 "stderr": raw_stderr.decode(errors="replace"),
                                 "stdout_hex": raw_stdout.hex(), "stderr_hex": raw_stderr.hex(),
                                 "expected_stdout": expected_out.decode(errors="replace")})
                good = (observed["exit"] == expected_exit and raw_stdout == expected_out and not raw_stderr)
                observed["ok"] = good
                entry["cases"].append(observed)
                actual_by_case.setdefault(name, {})[lang] = observed
                if not good:
                    print(f"FAIL {lang}/{name}: output or exit mismatch")
                    report["ok"] = False
            entry["ok"] = all(item["ok"] for item in entry["cases"]) and not protocol.returncode
        except Exception as error:
            entry["error"] = str(error)
            print(f"FAIL {lang}: {error}")
            report["ok"] = False

    for name, ports in actual_by_case.items():
        values = {(item["exit"], item["stdout_hex"], item["stderr_hex"]) for item in ports.values()}
        if len(values) != 1:
            report["ok"] = False
            report.setdefault("cross_mismatches", []).append(name)
    (state / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print("\nPORT MATRIX")
    print("port     build  suite  cases")
    for lang, item in report["ports"].items():
        status = "PASS" if item["ok"] else "FAIL"
        print(f"{lang:<8} {item.get('build_exit', 'FAIL')!s:<6} {item.get('protocol_suite_exit', 'FAIL')!s:<6} {status}")
    print(json.dumps(report, indent=2))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
