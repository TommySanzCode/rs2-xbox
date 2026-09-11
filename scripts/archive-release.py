#!/usr/bin/env python3
"""Archive pristine assembled release folders and verify every ZIP entry."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def archive(staging, output, name):
    folder = staging / name
    files = sorted(path for path in folder.rglob('*') if path.is_file())
    if not files:
        raise RuntimeError(f'Missing or empty package: {name}')
    destination = output / f'{name}.zip'
    if destination.exists():
        raise RuntimeError(f'Refusing to overwrite {destination.name}')
    expected = {}
    for path in files:
        relative = path.relative_to(folder).as_posix()
        lowered = relative.lower()
        if lowered.startswith(('server225/engine/data/players/', 'server225/runtime/server-')) or lowered in {
            'xbox-config.ini', 'server225/engine/.env', 'server225/engine/db.sqlite',
            'server225/engine/data/config/private.pem', 'server225/engine/data/config/public.pem',
            'server225/runtime/setup.local.json'
        }:
            raise RuntimeError(f'Local runtime state cannot be archived: {relative}')
        if '.git' in path.relative_to(folder).parts:
            raise RuntimeError('Git metadata cannot be archived')
        if name.endswith('Xbox'):
            if any(len(part) > 42 or any(char in part for char in '\\/:*?"<>|') for part in path.relative_to(staging).parts):
                raise RuntimeError(f'Filename is incompatible with FATX: {relative}')
        expected[f'{name}/{relative}'] = digest(path.read_bytes())
    with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as package:
        for path in files:
            package.write(path, path.relative_to(staging).as_posix())
    with zipfile.ZipFile(destination) as package:
        if len(package.namelist()) != len(expected) or set(package.namelist()) != set(expected):
            raise RuntimeError('Archive entry list differs from the reviewed files')
        for name_in_zip, sha in expected.items():
            if digest(package.read(name_in_zip)) != sha:
                raise RuntimeError(f'Archive readback mismatch: {name_in_zip}')
    return {'name': destination.name, 'bytes': destination.stat().st_size,
            'sha256': digest(destination.read_bytes()), 'files_verified': len(files)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--staging', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    staging, output = args.staging.resolve(), args.output.resolve()
    if output == staging or staging in output.parents:
        raise RuntimeError('Archive output must be outside the staging directory')
    output.mkdir(parents=True, exist_ok=True)
    records = [archive(staging, output, name) for name in ('RS2-2004-Xbox', 'RS2-2004-Server')]
    (output / 'SHA256SUMS.txt').write_text(''.join(f"{r['sha256']}  {r['name']}\n" for r in records), encoding='utf-8')
    (output / 'archive-verification.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(records, indent=2))


if __name__ == '__main__':
    main()
