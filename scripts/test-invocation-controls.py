#!/usr/bin/env python3
"""Compile real C# adapter faults; only public integration assertion failures count."""
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: test-invocation-controls.py HOST")
    host = Path(sys.argv[1]).resolve()
    source = (ROOT / "src/Program.cs").read_text()
    mutants = (
        ("stale-bare-frame", 'if (!failed) Protocol.HostSummary(host.Ledger, entriesCalled, invocation.Dry ? host.DryMoneyPreviewCalls : null);\n    return invocation.WriteFrame(Console.OpenStandardOutput());',
         'if (!failed) Protocol.HostSummary(host.Ledger, entriesCalled, invocation.Dry ? host.DryMoneyPreviewCalls : null);\n    return 0;'),
        ("missing-part", "invocation.Complete(part.Index);", "/* omitted completion */"),
        ("duplicate-part", "invocation.Select(part.Index, host.Registry, host.Context, contract);",
         "invocation.Select(0, host.Registry, host.Context, contract);"),
        ("argv-loss", "priorities.Add(flag == \"--first\" ? 0 : flag == \"--last\" ? 2 : 1);",
         "priorities.Add(1);"),
    )

    def check(root, executable):
        return subprocess.run([sys.executable, str(root / "sample-suite/check-invocations.py"),
                               str(executable)], cwd=root, env=os.environ.copy(),
                              capture_output=True, text=True)

    baseline = check(ROOT, host)
    assert baseline.returncode == 0, (baseline.stdout, baseline.stderr)
    print("PASS invocation mutant baseline", flush=True)
    for label, before, after in mutants:
        assert source.count(before) == 1, "mutation anchor drift: " + label
        with tempfile.TemporaryDirectory(prefix="lmcode-csharp-invocation-fault-") as tmp:
            work = Path(tmp)
            for name in ("src", "vendor", "sample-suite"):
                shutil.copytree(ROOT / name, work / name,
                                ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
            (work / "src/Program.cs").write_text(source.replace(before, after))
            build = subprocess.run(["dotnet", "build", str(work / "src/LmCode.Sample.csproj"),
                                    "-c", "Release", "--nologo"], cwd=work,
                                   capture_output=True, text=True)
            assert build.returncode == 0, (label, build.stdout, build.stderr)
            fault_host = work / "src/bin/Release/net8.0/lmcode-csharp-sample"
            result = check(work, fault_host)
            assert result.returncode == 1 and "AssertionError" in result.stderr, (
                label, result.returncode, result.stdout, result.stderr)
            print("CAUGHT " + label, flush=True)


if __name__ == "__main__":
    main()
