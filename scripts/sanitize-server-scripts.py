#!/usr/bin/env python3
"""Remove build-machine paths from Lost City compiler-v26 script metadata.

Only the second NUL-terminated string in each script record is rewritten.
Bytecode and all other metadata are retained byte for byte. The input files
are never modified by this module or its command-line entry point.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import struct


COMPILER_VERSION = 26
_U32 = struct.Struct(">I")


def _records(dat: bytes, idx: bytes):
    if len(dat) < 8 or len(idx) < 4:
        raise ValueError("Script archive or index header is incomplete")
    entries, version = struct.unpack_from(">II", dat)
    if version != COMPILER_VERSION:
        raise ValueError("Unsupported script compiler version")
    if _U32.unpack_from(idx)[0] != entries or len(idx) != 4 + entries * 4:
        raise ValueError("Script archive and index entry counts do not match")
    offset = 8
    for script_id in range(entries):
        size = _U32.unpack_from(idx, 4 + script_id * 4)[0]
        if size == 0:
            yield script_id, b"", None
            continue
        if size < 16 or offset + size > len(dat):
            raise ValueError(f"Invalid record bounds for script {script_id}")
        record = dat[offset : offset + size]
        offset += size
        trailer_length = struct.unpack_from(">H", record, size - 2)[0]
        trailer_position = size - trailer_length - 14
        try:
            name_end = record.index(0)
            path_end = record.index(0, name_end + 1)
        except ValueError:
            raise ValueError(f"Unterminated metadata for script {script_id}") from None
        # After the source path, lookupKey (4), parameterCount (1), and the
        # line-table count (2) must fit before the bytecode/trailer boundary.
        if trailer_position < path_end + 8 or trailer_position >= size - 2:
            raise ValueError(f"Invalid metadata/trailer bounds for script {script_id}")
        yield script_id, record, (name_end + 1, path_end)
    if offset != len(dat):
        raise ValueError("Script archive has unindexed trailing data")


def _public_source_path(source: str, content_root: Path) -> tuple[str, bool]:
    source = source.replace("\\", "/")
    root = str(content_root).replace("\\", "/").rstrip("/")
    suffix = None
    # The compiler records absolute paths. Handle Windows drive paths even
    # when this packaging helper is run on a different host operating system.
    case_insensitive = bool(re.match(r"^[A-Za-z]:/", source))
    source_compare = source.casefold() if case_insensitive else source
    root_compare = root.casefold() if case_insensitive else root
    if root and source_compare.startswith(root_compare + "/"):
        suffix = source[len(root) + 1 :]
    elif source.startswith("content/"):
        suffix = source[len("content/") :]
    elif source_compare.count("/content/") == 1:
        suffix = source[source_compare.index("/content/") + len("/content/") :]

    if suffix:
        parts = suffix.split("/")
        if all(part and part not in (".", "..") and ":" not in part for part in parts):
            if not any(ord(char) < 32 for char in suffix):
                return "content/" + suffix, True
    # Paths outside the matching content tree are useful only as diagnostic
    # labels. Retain a filename, without directories or a drive prefix.
    basename = source.rsplit("/", 1)[-1]
    if not basename or basename in (".", "..") or ":" in basename:
        raise ValueError("Script source metadata has no safe filename")
    if any(ord(char) < 32 for char in basename):
        raise ValueError("Script source filename contains control characters")
    return basename, False


def sanitize_scripts(dat: bytes, idx: bytes, content_root: Path) -> tuple[bytes, bytes, dict[str, int]]:
    """Return sanitized archive/index bytes and numeric, non-sensitive stats."""
    dat_parts = [dat[:8]]
    idx_parts = [idx[:4]]
    stats = {"entries": 0, "scripts": 0, "changed_paths": 0,
             "content_relative_paths": 0, "basename_paths": 0, "bytes_removed": 0}
    for script_id, record, span in _records(dat, idx):
        stats["entries"] += 1
        if span is None:
            idx_parts.append(_U32.pack(0))
            continue
        start, end = span
        try:
            source = record[start:end].decode("utf-8")
        except UnicodeDecodeError:
            raise ValueError(f"Invalid UTF-8 source metadata for script {script_id}") from None
        public_source, content_relative = _public_source_path(source, Path(content_root))
        replacement = public_source.encode("utf-8")
        updated = record[:start] + replacement + record[end:]
        if len(updated) > 0xFFFFFFFF:
            raise ValueError("Sanitized script exceeds index length capacity")
        dat_parts.append(updated)
        idx_parts.append(_U32.pack(len(updated)))
        stats["scripts"] += 1
        stats["changed_paths"] += replacement != record[start:end]
        stats["content_relative_paths" if content_relative else "basename_paths"] += 1

    result_dat, result_idx = b"".join(dat_parts), b"".join(idx_parts)
    # Verify the new archive independently, including its updated per-record
    # lengths, and prove that only each metadata source-path string changed.
    originals = _records(dat, idx)
    rewritten = _records(result_dat, result_idx)
    for old, new in zip(originals, rewritten, strict=True):
        old_id, old_record, old_span = old
        new_id, new_record, new_span = new
        if old_id != new_id or (old_span is None) != (new_span is None):
            raise ValueError("Sanitization changed the script ID layout")
        if old_span is not None and new_span is not None:
            if (old_record[:old_span[0]] != new_record[:new_span[0]] or
                    old_record[old_span[1]:] != new_record[new_span[1]:]):
                raise ValueError("Sanitization changed bytes outside source metadata")
    stats["bytes_removed"] = len(dat) - len(result_dat)
    return result_dat, result_idx, stats


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dat", type=Path, required=True)
    parser.add_argument("--input-idx", type=Path, required=True)
    parser.add_argument("--content-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    destinations = [args.output_dir / "script.dat", args.output_dir / "script.idx"]
    inputs = {args.input_dat.resolve(), args.input_idx.resolve()}
    if any(path.resolve() in inputs for path in destinations):
        parser.error("Output files must differ from both input files")
    data, index, stats = sanitize_scripts(args.input_dat.read_bytes(), args.input_idx.read_bytes(), args.content_root)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    for destination, contents in zip(destinations, (data, index)):
        temporary = destination.with_suffix(destination.suffix + ".sanitized.tmp")
        temporary.write_bytes(contents)
        temporary.replace(destination)
    print(json.dumps(stats, sort_keys=True))


if __name__ == "__main__":
    main()
