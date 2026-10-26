#!/usr/bin/env python3
"""Verify checked-in SDK and corpus bytes against their review locks."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent

def files(directory):
    return sorted(p for p in directory.rglob('*') if p.is_file() and '__pycache__' not in p.parts and p.suffix != '.pyc')

def hashes(directory):
    return {p.relative_to(directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files(directory)}

def copy_tree(source, destination):
    if destination.exists():
        for path in sorted(destination.rglob('*'), reverse=True):
            if path.is_file() or path.is_symlink(): path.unlink()
            elif path.is_dir(): path.rmdir()
    destination.mkdir(parents=True, exist_ok=True)
    for path in files(source):
        target = destination / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)

def update(kind, source):
    source = source.resolve()
    if kind == 'sdk':
        source = source / 'src' / 'LmCode'
        destination = ROOT / 'vendor/lmcode-csharp'
        repo = 'lmctl-ai/lmcode-csharp'
    else:
        source = source / 'sample-suite'
        destination = ROOT / 'sample-suite'
        repo = 'lmctl-ai/lmcode-cpp'
    if not source.is_dir(): raise SystemExit(f'missing source tree: {source}')
    copy_tree(source, destination)
    commit = subprocess.check_output(['git', '-C', str(source.parent.parent if kind == 'sdk' else source.parent), 'rev-parse', 'HEAD'], text=True).strip()
    (ROOT / (kind + '.lock')).write_text(json.dumps({'repo': repo, 'commit': commit, 'sha256': hashes(destination)}, indent=2) + '\n')
    print(f'Pinned {kind} at {commit}')

def main():
    if len(sys.argv) >= 2 and sys.argv[1] in ('sdk', 'suite'):
        source = Path(sys.argv[2]) if len(sys.argv) == 3 else ROOT.parent / ('lmcode-csharp' if sys.argv[1] == 'sdk' else 'lmcode-cpp')
        update(sys.argv[1], source)
        return
    if len(sys.argv) != 2 or sys.argv[1] != 'check':
        raise SystemExit('usage: sync.py sdk|suite [SOURCE] | check')
    for name, directory in [('sdk', ROOT / 'vendor/lmcode-csharp'), ('suite', ROOT / 'sample-suite')]:
        lock = json.loads((ROOT / (name + '.lock')).read_text())
        actual = hashes(directory)
        if actual != lock['sha256']:
            missing = sorted(set(lock['sha256']) - set(actual))
            extra = sorted(set(actual) - set(lock['sha256']))
            raise SystemExit(f'{name} drift: missing={missing[:3]} extra={extra[:3]}')
    print('PASS SDK and suite locks')

if __name__ == '__main__':
    main()
