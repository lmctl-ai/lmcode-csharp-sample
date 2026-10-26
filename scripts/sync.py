#!/usr/bin/env python3
"""Verify checked-in SDK and corpus bytes against their review locks."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent.parent

def files(directory):
    return sorted(p for p in directory.rglob('*') if p.is_file() and not ({'bin', 'obj', '__pycache__'} & set(p.parts)) and p.suffix != '.pyc')

def hashes(directory):
    return {p.relative_to(directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files(directory)}

def copy_tree(source, destination):
    if destination.exists(): shutil.rmtree(destination)
    destination.mkdir(parents=True, exist_ok=True)
    for path in files(source):
        target = destination / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)

def archive(source, commit, paths):
    checkout = Path(tempfile.mkdtemp(prefix='lmcode-csharp-sdk-'))
    archive_path = checkout / 'snapshot.tar'
    with archive_path.open('wb') as output:
        subprocess.run(['git', '-C', str(source), 'archive', '--format=tar', commit, *paths], check=True, stdout=output)
    subprocess.run(['tar', '-xf', str(archive_path), '-C', str(checkout)], check=True)
    return checkout

def update(kind, source):
    source = source.resolve()
    if kind == 'sdk':
        repo = source
        commit = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
        snapshot = archive(repo, commit, ['LICENSE', 'src/LmCode', 'std/lmcode-std.schema.json'])
        copy_tree(snapshot / 'src/LmCode', ROOT / 'vendor/lmcode-csharp')
        (ROOT / 'vendor/lmcode-csharp/LICENSE').write_bytes((snapshot / 'LICENSE').read_bytes())
        (ROOT / 'std').mkdir(exist_ok=True)
        (ROOT / 'std/lmcode-std.schema.json').write_bytes((snapshot / 'std/lmcode-std.schema.json').read_bytes())
        repo = 'lmctl-ai/lmcode-csharp'
    else:
        repo = source
        commit = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
        snapshot = archive(repo, commit, ['sample-suite'])
        source = snapshot / 'sample-suite'
        destination = ROOT / 'sample-suite'
        repo = 'lmctl-ai/lmcode-cpp'
    if not source.is_dir(): raise SystemExit(f'missing source tree: {source}')
    if kind == 'suite': copy_tree(source, destination)
    lock_hashes = hashes(ROOT / 'vendor/lmcode-csharp')
    lock_hashes.update({'std/' + p.relative_to(ROOT / 'std').as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files(ROOT / 'std')})
    if kind == 'suite': lock_hashes = hashes(destination)
    (ROOT / (kind + '.lock')).write_text(json.dumps({'repo': repo, 'commit': commit, 'sha256': lock_hashes}, indent=2) + '\n')
    print(f'Pinned {kind} at {commit}')

def main():
    if len(sys.argv) >= 2 and sys.argv[1] in ('sdk', 'suite'):
        source = Path(sys.argv[2]) if len(sys.argv) == 3 else ROOT.parent / ('lmcode-csharp' if sys.argv[1] == 'sdk' else 'lmcode-cpp')
        update(sys.argv[1], source)
        return
    if len(sys.argv) != 2 or sys.argv[1] != 'check':
        raise SystemExit('usage: sync.py sdk|suite [SOURCE] | check')
    for name, directory in [('suite', ROOT / 'sample-suite')]:
        lock = json.loads((ROOT / (name + '.lock')).read_text())
        actual = hashes(directory)
        if actual != lock['sha256']:
            missing = sorted(set(lock['sha256']) - set(actual))
            extra = sorted(set(actual) - set(lock['sha256']))
            raise SystemExit(f'{name} drift: missing={missing[:3]} extra={extra[:3]}')
    sdk_lock = json.loads((ROOT / 'sdk.lock').read_text())
    actual_sdk = hashes(ROOT / 'vendor/lmcode-csharp')
    actual_sdk.update({'std/' + p.relative_to(ROOT / 'std').as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files(ROOT / 'std')})
    if actual_sdk != sdk_lock['sha256']:
        raise SystemExit('sdk drift: refresh reviewed committed SDK snapshot')
    print('PASS SDK and suite locks')

if __name__ == '__main__':
    main()
