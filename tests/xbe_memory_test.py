import runpy
from pathlib import Path
import struct
import unittest
from xbox_paths_test import fixture

set_memory = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'scripts/set-xbe-memory.py'))['set_memory']


class MemoryTests(unittest.TestCase):
    def test_changes_only_limit_bit_and_is_reversible(self):
        original = bytearray(fixture()[0])
        struct.pack_into('<I', original, 0x124, 0x1d)
        enhanced = set_memory(bytes(original), 128)
        self.assertEqual(enhanced[0x124], 0x19)
        self.assertEqual(enhanced[:0x124], original[:0x124])
        self.assertEqual(enhanced[0x125:], original[0x125:])
        self.assertEqual(set_memory(enhanced, 64), original)
        self.assertEqual(set_memory(enhanced, 128), enhanced)

    def test_rejects_non_homebrew_and_invalid_profile(self):
        original = bytearray(fixture()[0])
        with self.assertRaises(ValueError): set_memory(original, 96)
        original[4] = 1
        with self.assertRaises(ValueError): set_memory(original, 128)


if __name__ == '__main__': unittest.main()
