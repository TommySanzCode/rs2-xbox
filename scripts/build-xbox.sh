#!/usr/bin/env bash
set -euo pipefail

client_dir=${1:?Pass the Client3 directory}
export NXDK_DIR=${2:?Pass the nxdk directory}
jobs=${3:-8}
export XBOX_RAM_MB=${4:-64}
case "$XBOX_RAM_MB" in 64|128) ;; *) printf 'RAM profile must be 64 or 128\n' >&2; exit 1 ;; esac
export PATH="$NXDK_DIR/bin:/mingw64/bin:/usr/bin:$PATH"
cd "$client_dir"

for tool in make clang lld-link llvm-ar bison flex cmake python3; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'Missing build prerequisite: %s\n' "$tool" >&2
        exit 1
    fi
done
if [[ ! -f "$NXDK_DIR/lib/sdl/SDL2/Makefile.xbox" ]]; then
    printf 'nxdk submodules are missing. Run git submodule update --init --recursive in nxdk.\n' >&2
    exit 1
fi

mkdir -p build
python3 scripts/prepare-xbox-cache.py
printf 'Building the native Xbox client; log: build/xbox-build.log\n'
if make -f xbox.mk -j"$jobs" > build/xbox-build.log 2>&1; then
    tail -n 12 build/xbox-build.log
else
    tail -n 60 build/xbox-build.log >&2
    exit 1
fi

python3 - <<'PY'
from pathlib import Path
import hashlib
import json
import os
import subprocess

xbe = Path('rom/default.xbe')
if xbe.read_bytes()[:4] != b'XBEH':
    raise SystemExit('Output does not contain an Xbox XBE header')

def revision(path):
    try:
        return subprocess.check_output(['git', '-C', str(path), 'rev-parse', 'HEAD'], text=True, stderr=subprocess.DEVNULL).strip()
    except subprocess.CalledProcessError:
        return None

def modified():
    try:
        return bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True, stderr=subprocess.DEVNULL).strip())
    except subprocess.CalledProcessError:
        return True

manifest = {
    'target': 'original Xbox, ' + os.environ['XBOX_RAM_MB'] + ' MB',
    'ram_mb': int(os.environ['XBOX_RAM_MB']),
    'enhanced': os.environ['XBOX_RAM_MB'] == '128',
    'client_revision': 225,
    'client_upstream_commit': 'd828cb3cb87f033d76f0582748e664bade049562',
    'client_source_commit': revision('.'),
    'nxdk_commit': revision(os.environ['NXDK_DIR']),
    'working_tree_modified': modified(),
    'hardware_tested': False,
    'artifacts': {},
}
for name in ['rom/default.xbe', 'client.iso']:
    payload = Path(name).read_bytes()
    manifest['artifacts'][name] = {'bytes': len(payload), 'sha256': hashlib.sha256(payload).hexdigest()}
Path('build/xbox-build.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print('Verified XBE header; wrote build/xbox-build.json')
PY
