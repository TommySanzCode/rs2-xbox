"""Archive-level regression checks for source-path metadata sanitization."""
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("sanitizer", Path(__file__).resolve().parents[1] / "scripts/sanitize-server-scripts.py")
sanitizer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sanitizer)


def script(source, literal=b"R:/private/game/content/keep-this-bytecode.rs2"):
    metadata = b"[proc,test]\0" + source + b"\0" + struct.pack(">IBH", 0xFFFFFFFF, 0, 0)
    code = struct.pack(">H", 3) + literal + b"\0" + struct.pack(">HB", 21, 0)
    trailer = struct.pack(">IHHHHBH", 2, 0, 0, 0, 0, 0, 1)
    return metadata + code + trailer


def archive(*records):
    return (struct.pack(">II", len(records), 26) + b"".join(records),
            struct.pack(">I", len(records)) + b"".join(struct.pack(">I", len(record)) for record in records))


class SanitizeTests(unittest.TestCase):
    def test_paths_only_sparse_records_and_idempotence(self):
        dat, idx = archive(script(b"R:\\private\\game\\content\\scripts\\test.rs2"), b"", script(b"Q:/elsewhere/other.rs2"))
        out, outidx, stats = sanitizer.sanitize_scripts(dat, idx, Path("R:/private/game/content"))
        self.assertEqual(stats["entries"], 3)
        self.assertEqual(stats["scripts"], 2)
        self.assertEqual(stats["changed_paths"], 2)
        self.assertEqual(stats["basename_paths"], 1)
        self.assertIn(b"content/scripts/test.rs2\0", out)
        self.assertIn(b"[proc,test]\0other.rs2\0", out)
        self.assertIn(b"R:/private/game/content/keep-this-bytecode.rs2\0", out)
        self.assertEqual(sanitizer.sanitize_scripts(out, outidx, Path("unused"))[:2], (out, outidx))
        for before, after in zip(sanitizer._records(dat, idx), sanitizer._records(out, outidx)):
            if before[2] is not None:
                self.assertEqual(before[1][before[2][1]:], after[1][after[2][1]:])

    def test_rejects_inconsistent_index_and_trailing_data(self):
        dat, idx = archive(script(b"content/scripts/test.rs2"))
        for bad_dat, bad_idx in ((dat + b"extra", idx), (dat, idx[:-1]), (dat[:-2], idx),
                                 (dat[:4] + struct.pack(">I", 99) + dat[8:], idx)):
            with self.assertRaises(ValueError):
                sanitizer.sanitize_scripts(bad_dat, bad_idx, Path("content"))

    def test_relative_traversal_falls_back_to_filename(self):
        dat, idx = archive(script(b"content/../../private/test.rs2"))
        out, _, stats = sanitizer.sanitize_scripts(dat, idx, Path("content"))
        self.assertIn(b"[proc,test]\0test.rs2\0", out)
        self.assertEqual(stats["basename_paths"], 1)


if __name__ == "__main__":
    unittest.main()
