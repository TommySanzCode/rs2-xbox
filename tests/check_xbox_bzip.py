"""Standalone check: pass a host bzip shared library and cache/client/songs."""
import bz2
import ctypes
from pathlib import Path
import sys

library = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
decode = library.bzip_decompress_checked
decode.argtypes = (ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int)
decode.restype = ctypes.c_int


def check(compressed, expected, capacity=None, valid=True):
    size = len(expected) if capacity is None else capacity
    output = ctypes.create_string_buffer(b'\xa5' * (size + 32), size + 32)
    result = decode(ctypes.byref(output, 16), size, compressed, len(compressed))
    assert bool(result) == valid, (result, len(compressed), size)
    assert output.raw[:16] == output.raw[-16:] == b'\xa5' * 16
    if valid: assert output.raw[16:16 + size] == expected


for data in (b'A', b'a' * 4096, bytes(range(256)) * 800, b'MThd' * 25000):
    compressed = bz2.compress(data, compresslevel=1)[4:]
    check(compressed, data)
    check(compressed, data, len(data) + 1, False)
    if len(data) > 1: check(compressed, data, len(data) - 1, False)
    check(compressed[:-8], data, valid=False)
    broken = bytearray(compressed)
    broken[6] ^= 1  # block CRC, not a probabilistic mutation
    check(bytes(broken), data, valid=False)

count = 0
for path in sorted(Path(sys.argv[2]).glob('*.mid')):
    data = path.read_bytes()
    expected = bz2.decompress(b'BZh1' + data[4:])
    assert len(expected) == int.from_bytes(data[:4], 'big')
    check(data[4:], expected)
    count += 1
assert count
print(f'PASS: exact output and guards for {count} cache songs; oversized, undersized, truncated and corrupt streams rejected.')
