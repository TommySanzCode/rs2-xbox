#!/usr/bin/env python3
"""Remove private nxdk __FILE__ prefixes from a narrowly validated homebrew XBE.

Only fixed-size diagnostic string prefixes in read-only data are changed.
Source suffix positions, pointers, code, headers, and section layouts stay
unchanged. This supports nxdk cxbe's unsigned, zero-section-digest layout;
it is not a general executable patcher or a signature/checksum bypass.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import struct


_PRIVATE_PATH = re.compile(rb"[A-Za-z]:[/\\]Users[/\\]", re.IGNORECASE)
_WIDE_PRIVATE_PATH = re.compile(rb"[A-Za-z]\x00:\x00[/\\]\x00U\x00s\x00e\x00r\x00s\x00[/\\]\x00", re.IGNORECASE)


def _layout(data: bytes) -> tuple[int, list[dict[str, int | bytes]]]:
    if len(data) < 0x178 or data[:4] != b"XBEH":
        raise ValueError("Expected an nxdk XBE header")
    if any(data[4:0x104]):
        raise ValueError("Signed or unsupported XBE header")
    base, header_size = struct.unpack_from("<II", data, 0x104)
    count, section_address = struct.unpack_from("<II", data, 0x11C)
    section_offset = section_address - base
    if not (0x178 <= header_size <= len(data)) or count != 4:
        raise ValueError("Unsupported nxdk XBE layout")
    if section_offset < 0x178 or section_offset + count * 56 > header_size:
        raise ValueError("Invalid XBE section header bounds")
    expected_flags = {b".text": 6, b".rdata": 2, b".data": 3, b".tls": 3}
    sections = []
    for index in range(count):
        pos = section_offset + index * 56
        flags, virtual_address, virtual_size, raw, size, name_address = struct.unpack_from("<IIIIII", data, pos)
        name_pos = name_address - base
        if not 0 <= name_pos < header_size:
            raise ValueError("Invalid section name address")
        name_end = data.find(b"\0", name_pos, min(name_pos + 9, header_size))
        if name_end < 0:
            raise ValueError("Unterminated section name")
        name = data[name_pos:name_end]
        if name not in expected_flags or flags != expected_flags[name]:
            raise ValueError("Unsupported nxdk section or flags")
        if raw < header_size or size <= 0 or raw + size > len(data):
            raise ValueError("Invalid XBE section data bounds")
        if any(data[pos + 36:pos + 56]):
            raise ValueError("XBE has nonzero section digests; rebuild it from source")
        sections.append({"name": name, "raw": raw, "size": size,
                         "virtual_address": virtual_address, "virtual_size": virtual_size})
    if {section["name"] for section in sections} != set(expected_flags):
        raise ValueError("Missing or repeated nxdk section")
    ordered = sorted(sections, key=lambda section: section["raw"])
    if any(a["raw"] + a["size"] > b["raw"] for a, b in zip(ordered, ordered[1:])):
        raise ValueError("Overlapping XBE sections")
    return header_size, sections


def sanitize_xbe(data: bytes) -> tuple[bytes, dict[str, int]]:
    """Return fixed-size sanitized bytes and numeric, non-sensitive stats."""
    header_size, sections = _layout(data)
    rdata = next(section for section in sections if section["name"] == b".rdata")
    lower, upper = rdata["raw"], rdata["raw"] + rdata["size"]
    if _WIDE_PRIVATE_PATH.search(data):
        raise ValueError("Unsupported UTF-16 private path in executable")
    spans = []
    for match in _PRIVATE_PATH.finditer(data):
        start = match.start()
        if not lower <= start < upper:
            raise ValueError("Private path occurs outside read-only diagnostic data")
        if start == lower or data[start - 1] != 0:
            raise ValueError("Private path is not a standalone NUL-terminated string")
        end = data.find(b"\0", start, upper)
        if end < 0:
            raise ValueError("Unterminated private diagnostic path")
        path = data[start:end]
        if any(byte < 32 or byte > 126 for byte in path):
            raise ValueError("Diagnostic path is not printable ASCII")
        normalized = path.replace(b"\\", b"/")
        marker = b"/nxdk/lib/"
        if normalized.lower().count(marker) != 1:
            raise ValueError("Private path is not a recognized nxdk library source")
        marker_pos = normalized.lower().index(marker)
        suffix = normalized[marker_pos + len(b"/nxdk"):]
        if not suffix.lower().endswith((b".c", b".h", b".cpp", b".s", b".inc")):
            raise ValueError("Private path is not a recognized source filename")
        prefix_end = start + marker_pos + len(b"/nxdk")
        # Keep the suffix at the same absolute offset: compilers can pool
        # strings and create pointers into a source filename's suffix.
        replacement = b"nxdk".ljust(prefix_end - start, b"_")
        if len(replacement) != prefix_end - start:
            raise ValueError("Diagnostic prefix is too short")
        spans.append((start, prefix_end, replacement))

    output = bytearray(data)
    for start, end, replacement in spans:
        output[start:end] = replacement
    result = bytes(output)
    if len(result) != len(data) or result[:header_size] != data[:header_size]:
        raise ValueError("Sanitization changed executable size or headers")
    if _layout(result) != (header_size, sections):
        raise ValueError("Sanitization changed the executable layout")
    cursor = 0
    for start, end, replacement in spans:
        if result[cursor:start] != data[cursor:start] or result[start:end] != replacement:
            raise ValueError("Sanitization changed bytes outside an approved prefix")
        cursor = end
    if result[cursor:] != data[cursor:] or _PRIVATE_PATH.search(result):
        raise ValueError("Unapproved changes or remaining private paths")
    return result, {"paths_rewritten": len(spans), "prefix_bytes_replaced": sum(end - start for start, end, _ in spans),
                    "bytes_changed": sum(a != b for a, b in zip(data, result)), "output_bytes": len(result)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.input.resolve() == args.output.resolve():
        parser.error("Output must differ from the original executable")
    result, stats = sanitize_xbe(args.input.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".sanitized.tmp")
    temporary.write_bytes(result)
    temporary.replace(args.output)
    print(json.dumps(stats, sort_keys=True))


if __name__ == "__main__":
    main()
