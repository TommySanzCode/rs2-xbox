"""Verify that executable sanitation changes only approved diagnostic prefixes."""
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("sanitizer", Path(__file__).resolve().parents[1] / "scripts/sanitize-xbox-paths.py")
sanitizer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sanitizer)


def fixture():
    data = bytearray(0x800)
    data[:4] = b"XBEH"
    base = 0x10000
    struct.pack_into("<II", data, 0x104, base, 0x300)
    struct.pack_into("<II", data, 0x11C, 4, base + 0x180)
    for i, (name, flags) in enumerate(((b".text", 6), (b".rdata", 2), (b".data", 3), (b".tls", 3))):
        name_pos = 0x260 + i * 10
        data[name_pos:name_pos + len(name)] = name
        struct.pack_into("<IIIIII", data, 0x180 + i * 56, flags, base + 0x400 + i * 0x100, 0x100, 0x400 + i * 0x100, 0x100, base + name_pos)
    # Built in parts so fixture source itself contains no personal-path text.
    path = b"R:/" + b"Users/" + b"example/build/nxdk/lib/hal/video.c\0"
    data[0x501:0x501 + len(path)] = path
    data[0x400:0x410] = bytes(range(16))
    return bytes(data), path


class SanitizeXbeTests(unittest.TestCase):
    def test_preserves_layout_code_suffix_positions_and_idempotence(self):
        original, path = fixture()
        result, stats = sanitizer.sanitize_xbe(original)
        prefix_end = 0x501 + path.index(b"/lib/")
        self.assertEqual(stats["paths_rewritten"], 1)
        self.assertEqual(len(result), len(original))
        self.assertEqual(result[:0x501], original[:0x501])
        self.assertEqual(result[prefix_end:], original[prefix_end:])
        self.assertEqual(sanitizer._layout(result), sanitizer._layout(original))
        self.assertEqual(sanitizer.sanitize_xbe(result)[0], result)

    def test_rejects_digest_and_executable_section_paths(self):
        original, path = fixture()
        with_digest = bytearray(original)
        with_digest[0x180 + 36] = 1
        with self.assertRaises(ValueError):
            sanitizer.sanitize_xbe(bytes(with_digest))
        in_code = bytearray(original)
        in_code[0x420:0x420 + len(path)] = path
        with self.assertRaises(ValueError):
            sanitizer.sanitize_xbe(bytes(in_code))

    def test_rejects_unrecognized_private_filename_and_bad_header(self):
        original, _ = fixture()
        for modified in (original.replace(b"video.c", b"data.db"), b"NOPE" + original[4:], original[:-1]):
            with self.assertRaises(ValueError):
                sanitizer.sanitize_xbe(modified)


if __name__ == "__main__":
    unittest.main()
