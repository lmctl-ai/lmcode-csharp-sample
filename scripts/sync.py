#!/usr/bin/env python3
"""Verify checked-in SDK and corpus bytes against their review locks."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent

def files(directory):
    return sorted(p for p in directory.rglob('*') if p.is_file() and '__pycache__' not in p.parts and p.suffix != '.pyc')

def hashes(directory):
    return {p.relative_to(directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files(directory)}

def main():
    if len(sys.argv) != 2 or sys.argv[1] != 'check':
        raise SystemExit('usage: sync.py check')
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
