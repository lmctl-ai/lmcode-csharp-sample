#!/usr/bin/env python3
"""Compile actual C# host faults; a build/crash/unexpected exit is not a caught fault."""
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SUITE = ROOT / 'sample-suite'
validate = runpy.run_path(str(SUITE / 'return_protocol.py'))['validate']
runner = runpy.run_path(str(SUITE / 'run-suite.py'))
manifest = json.loads((SUITE / 'manifest.json').read_text())
cases = {case['name']: case for case in manifest['cases']}


def run(command, name):
    result = runner['run'](command, SUITE / cases[name]['bytecode'])
    assert result.returncode == cases[name]['expected_exit'] and not result.stderr, (name, result)
    return result


def baseline(command):
    for name in ('001-literals', '360-return-scalar', '364-return-entry-lifetime',
                 '365-return-entry-error', '366-return-app-error'):
        result = run(command, name)
        validate(result.stdout, result.returncode, cases[name].get('expected_return'))
        assert result.stdout == (SUITE / 'expected' / (name + '.out')).read_bytes(), name
    print('PASS return-control baseline: exact diagnostics, float, entries and failure silence', flush=True)


def main():
    command = sys.argv[1:]
    if not command:
        raise SystemExit('usage: test-return-controls.py HOST [ARG ...]')
    baseline(command)
    source = (ROOT / 'src/Program.cs').read_text()
    mutants = (
        ('missing-frame', 'ResultFrames.Write(Console.OpenStandardOutput(), resultSink);',
         '/* deliberately omit terminal frame */', '001-literals',
         'missing, duplicate or nonterminal return frame'),
        ('reset-per-entry', 'Value result = program.Call(',
         'resultSink.Reset();\n        Value result = program.Call(', '364-return-entry-lifetime',
         'invocation result differs from independent return oracle'),
    )
    artifacts = ROOT / 'artifacts'
    artifacts.mkdir(exist_ok=True)
    for label, before, after, name, reason in mutants:
        assert source.count(before) == 1, 'mutation anchor drift: ' + label
        with tempfile.TemporaryDirectory(prefix='return fault ', dir=artifacts) as tmp:
            work = Path(tmp)
            for directory in ('src', 'vendor'):
                shutil.copytree(ROOT / directory, work / directory,
                                ignore=shutil.ignore_patterns('bin', 'obj'))
            (work / 'sample-suite').mkdir()
            shutil.copy2(SUITE / 'sample.dsl.json', work / 'sample-suite/sample.dsl.json')
            (work / 'src/Program.cs').write_text(source.replace(before, after))
            build = subprocess.run(['dotnet', 'build', str(work / 'src/LmCode.Sample.csproj'),
                                    '-c', 'Release', '--nologo'], capture_output=True)
            assert build.returncode == 0, build.stdout.decode(errors='replace') + build.stderr.decode(errors='replace')
            result = run(['dotnet', str(work / 'src/bin/Release/net8.0/lmcode-csharp-sample.dll')], name)
            try:
                validate(result.stdout, result.returncode, cases[name].get('expected_return'))
            except ValueError as error:
                assert str(error) == reason, (label, error)
                print('CAUGHT ' + label + ': ' + str(error), flush=True)
            else:
                raise AssertionError('host fault survived: ' + label)


if __name__ == '__main__':
    main()
