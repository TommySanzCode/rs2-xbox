"""Regenerate revision-225 login CRCs from the archives actually being packaged."""
from pathlib import Path
import struct
import shutil
import zlib
import os

root = Path(__file__).resolve().parents[1]
cache = root / 'rom' / 'cache' / 'client'
local_config = root / 'rom' / 'config.ini'
if not local_config.exists():
    local_config.parent.mkdir(parents=True, exist_ok=True)
    template = 'xbox-128-config.example.ini' if os.environ.get('XBOX_RAM_MB') == '128' else 'xbox-config.example.ini'
    shutil.copyfile(root / template, local_config)
    print('Created rom/config.ini. Set the LAN server address and local test account before login.')
archives = ('title', 'config', 'interface', 'media', 'models', 'textures', 'wordenc', 'sounds')
required = [cache / name for name in archives]
required += [root / 'rom' / 'config.ini', root / 'rom' / 'Roboto' / 'Roboto-Bold.ttf']
missing = [str(path.relative_to(root)) for path in required if not path.is_file()]
if not (cache / 'maps').is_dir():
    missing.append('rom/cache/client/maps/')
if missing:
    raise SystemExit('Missing Xbox assets: ' + ', '.join(missing))

checksums = [0] + [zlib.crc32((cache / name).read_bytes()) & 0xffffffff for name in archives]
payload = struct.pack('>9I', *checksums)
target = cache / 'crc'
if not target.exists() or target.read_bytes() != payload:
    target.write_bytes(payload)
    print('Updated the Xbox CRC table from the eight packaged archives.')
else:
    print('Xbox cache CRC table matches all eight packaged archives.')
