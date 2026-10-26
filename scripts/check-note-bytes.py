#!/usr/bin/env python3
"""Exercise real file -> VmString -> result bytes beyond the text-only corpus."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
compiler = shutil.which(os.environ.get('LMSCRIPT_BIN', 'lmscript'))
if not compiler:
    sys.exit('lmscript compiler not found')
if len(sys.argv) < 2:
    sys.exit('usage: check-note-bytes.py EXECUTABLE [ARG ...]')
with tempfile.TemporaryDirectory(prefix='sample bytes ') as scratch:
    folder = Path(scratch)
    note = b'\x00\x08\x0c\t\r\n"\\\xff\xc3\xa9'
    (folder / 'note.bin').write_bytes(note)
    source = folder / 'note.lms'
    bytecode = folder / 'note.dslb'
    source.write_text('program = _lmcode_ schema=' + json.dumps((root / 'sample-suite/sample.dsl.json').as_posix()) + ';\n'
                      'ledger.note(file_read(sample_tmp() + "/note.bin"));\n'
                      'end _lmcode_\nsavedsl(program, ARGV[0]);\n', encoding='utf-8')
    subprocess.run([compiler, '--no-preflight', str(source), str(bytecode)], check=True)
    result = subprocess.run([*sys.argv[1:], '--dsl', str(bytecode)],
                            env=dict(os.environ, SAMPLE_TMP=scratch), capture_output=True, timeout=30)
    expected = b'{"ok": true, "result": {"balance": 0, "entries": 0, "note": "\\u0000\\u0008\\u000c\\t\\r\\n\\"\\\\\xff\xc3\xa9"}}\n'
    if result.returncode or result.stderr or result.stdout != expected:
        sys.exit(f'note-byte mismatch: exit={result.returncode}, stdout={result.stdout!r}, stderr={result.stderr!r}')
print('PASS note bytes: NUL, JSON escapes, invalid UTF-8 and multibyte UTF-8')
