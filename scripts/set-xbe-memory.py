"""Set the memory flag on an unsigned nxdk-built homebrew XBE only."""
import argparse
from pathlib import Path
import runpy
import struct


def set_memory(data: bytes, megabytes: int) -> bytes:
    if megabytes not in (64, 128):
        raise ValueError('Memory profile must be 64 or 128')
    validator = runpy.run_path(str(Path(__file__).with_name('sanitize-xbox-paths.py')))
    validator['_layout'](data)
    output = bytearray(data)
    flags, = struct.unpack_from('<I', output, 0x124)
    flags = flags | 4 if megabytes == 64 else flags & ~4
    struct.pack_into('<I', output, 0x124, flags)
    return bytes(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('xbe', type=Path)
    parser.add_argument('megabytes', type=int, choices=(64, 128))
    args = parser.parse_args()
    data = args.xbe.read_bytes()
    updated = set_memory(data, args.megabytes)
    if updated != data:
        args.xbe.write_bytes(updated)
    print(f'XBE memory profile: {args.megabytes} MB')
