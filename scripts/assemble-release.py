#!/usr/bin/env python3
"""Stage the two release folders without starting a server or creating archives.

Example: python scripts/assemble-release.py --source . --workspace ../.. \
    --output ../../build/release-v0.1.0-preview.1/staging
Repeat with --refresh to incorporate updated templates. Existing unplanned files
cause an error; this tool never deletes files or reads live config/account state.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import runpy
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib


TOOL = "rs2-xbox-release-assembler"
PACKAGES = ("RS2-2004-Xbox", "RS2-2004-Server")
ARCHIVES = ("title", "config", "interface", "media", "models", "textures", "wordenc", "sounds")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def atomic_stage(destination: Path, *, source: Path | None = None, data: bytes | None = None) -> None:
    """Replace the directory entry, preserving any hardlinked test/input copy."""
    descriptor, temporary_name = tempfile.mkstemp(prefix=".release-", suffix=".tmp", dir=destination.parent)
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        if source is not None:
            shutil.copy2(source, temporary)
        else:
            temporary.write_bytes(data or b"")
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


def git(repo: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(repo), *args], stderr=subprocess.PIPE)


def revision(repo: Path) -> str:
    return git(repo, "rev-parse", "HEAD").decode().strip()


def excluded(relative: str, *, dependency: bool = False) -> str | None:
    parts = PurePosixPath(relative).parts
    lower = tuple(part.lower() for part in parts)
    name = lower[-1]
    if any(part in {".git", ".cache", ".turbo", "__pycache__"} for part in lower):
        return "repository/cache metadata"
    if dependency:
        return None  # Preserve complete published dependency payloads and notices.
    if name.startswith(".env"):
        return "environment configuration"
    if name.endswith((".pem", ".key", ".pfx", ".p12")):
        return "key material"
    if name.startswith("config.private") or name in {"credentials.json", "credentials.txt", "local-credentials.txt"}:
        return "private configuration"
    if re.search(r"\.(?:sqlite3?|db)(?:-(?:wal|shm|journal))?$", name) or name.endswith((".sav", ".log", ".pid")):
        return "database/save/log/process state"
    if lower[0] in {"runtime", "logs", "saves", "players"} or lower[:2] in {
        ("data", "players"), ("data", "logs"), ("data", "saves"), ("data", "runtime")
    }:
        return "runtime state directory"
    return None


def tree_files(root: Path, *, dependency: bool = False):
    if not root.is_dir():
        raise ValueError(f"Required input directory is missing: {root.name}")
    for folder, directories, files in os.walk(root, followlinks=False):
        base = Path(folder)
        kept = []
        for directory in sorted(directories):
            candidate = base / directory
            relative = candidate.relative_to(root).as_posix()
            if excluded(relative + "/_", dependency=dependency):
                continue
            if candidate.is_symlink() or (hasattr(candidate, "is_junction") and candidate.is_junction()):
                raise ValueError(f"Refusing an input directory link: {relative}")
            kept.append(directory)
        directories[:] = kept
        for name in sorted(files):
            candidate = base / name
            relative = candidate.relative_to(root).as_posix()
            if excluded(relative, dependency=dependency):
                continue
            if candidate.is_symlink():
                raise ValueError(f"Refusing an input file link: {relative}")
            yield candidate, relative


def simple_options(path: Path) -> dict[str, str]:
    result = {}
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith(("#", ";")) or "=" not in line:
            continue
        name, value = line.split("=", 1)
        result[name.strip()] = value.strip()
    return result


def fatx_component(component: str) -> str:
    safe = re.sub(r'[<>:"/\\|?*+,;=\[\]\x00-\x1f]', "-", component).rstrip(" .")
    if len(safe) > 42:
        suffix = Path(safe).suffix[:5]
        stem = safe[:-len(suffix)] if suffix else safe
        safe = stem[:32 - len(suffix)] + "-" + hashlib.sha256(component.encode()).hexdigest()[:8] + suffix
    return safe


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="Public rs2-xbox source repository")
    parser.add_argument("--workspace", type=Path, required=True, help="Local inputs containing Server225, Client3, and build/github-validation")
    parser.add_argument("--output", type=Path, required=True, help="Dedicated staging directory")
    parser.add_argument("--version", default="v0.1.0-preview.1")
    parser.add_argument("--bun-version", default="1.4.2")
    parser.add_argument("--refresh", action="store_true", help="Update a staging directory previously owned by this tool")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    workspace = args.workspace.resolve(strict=True)
    output = args.output.resolve()
    engine = workspace / "Server225" / "engine"
    content = workspace / "Server225" / "content"
    template = source / "server-package"
    marker = output / ".release-staging.json"
    protected = (source, workspace, engine, content, template, engine / "node_modules", engine / "data" / "pack")
    if output in protected or any(output.is_relative_to(root) for root in protected if root != workspace):
        raise ValueError("Staging must be separate from every copied source tree.")
    if output.exists():
        if not args.refresh or not marker.is_file() or json.loads(marker.read_text()).get("tool") != TOOL:
            raise ValueError("Output already exists; use a fresh directory, or --refresh for this tool's staging directory.")

    blank_config = source / "xbox-config.example.ini"
    options = simple_options(blank_config)
    if options.get("username") != "" or options.get("password") != "" or options.get("socketip") != "127.0.0.1":
        raise ValueError("Xbox example config must contain blank credentials and a loopback placeholder.")
    server_options = simple_options(template / "options.ini")
    if server_options.get("account_username") != "" or server_options.get("account_password") != "" or server_options.get("server_address") != "auto":
        raise ValueError("Server template options must contain blank account fields and automatic address detection.")

    plan: dict[str, tuple[Path | None, bytes | None]] = {}
    exclusions = []

    def add(package: str, relative: str, path: Path | None = None, data: bytes | None = None):
        relative_path = PurePosixPath(relative)
        if relative_path.is_absolute() or ".." in relative_path.parts:
            raise ValueError("Invalid release-relative path")
        key = package + "/" + relative_path.as_posix()
        if key in plan:
            raise ValueError(f"Duplicate release path: {key}")
        if path is not None and (not path.is_file() or path.is_symlink()):
            raise ValueError(f"Missing or linked input for: {key}")
        plan[key] = (path, data)

    def add_tree(package: str, prefix: str, root: Path, *, dependency: bool = False):
        for path, relative in tree_files(root, dependency=dependency):
            add(package, str(PurePosixPath(prefix) / relative), path)

    def add_tracked(prefix: str, repo: Path):
        for raw in git(repo, "ls-files", "-z").split(b"\0"):
            if not raw:
                continue
            relative = raw.decode("utf-8")
            reason = excluded(relative)
            if reason:
                exclusions.append({"tree": prefix, "path": relative, "reason": reason})
                continue
            add(PACKAGES[1], prefix + "/" + relative, repo / relative)

    xbe = workspace / "build" / "github-validation" / "rom" / "default.xbe"
    build_record = json.loads((workspace / "build" / "github-validation" / "build" / "xbox-build.json").read_text())
    recorded_xbe = build_record.get("artifacts", {}).get("rom/default.xbe", {})
    original_xbe = xbe.read_bytes()
    original_xbe_sha = hashlib.sha256(original_xbe).hexdigest()
    if len(original_xbe) != recorded_xbe.get("bytes") or original_xbe_sha != recorded_xbe.get("sha256"):
        raise ValueError("Clean-source XBE does not match its build manifest.")
    if not original_xbe.startswith(b"XBEH"):
        raise ValueError("Clean-source validation executable has no XBE header.")
    sanitize_xbe = runpy.run_path(str(source / "scripts" / "sanitize-xbox-paths.py"))["sanitize_xbe"]
    public_xbe, xbe_stats = sanitize_xbe(original_xbe)
    add(PACKAGES[0], "default.xbe", data=public_xbe)
    add(PACKAGES[0], "config.ini", blank_config)
    add_tree(PACKAGES[0], "cache/client", engine / "data" / "pack" / "client")
    cache = engine / "data" / "pack" / "client"
    checksums = [0] + [zlib.crc32((cache / name).read_bytes()) & 0xffffffff for name in ARCHIVES]
    crc_path = PACKAGES[0] + "/cache/client/crc"
    plan.pop(crc_path, None)
    add(PACKAGES[0], "cache/client/crc", data=struct.pack(">9I", *checksums))
    if not (cache / "maps").is_dir():
        raise ValueError("Client map cache is missing.")
    add_tree(PACKAGES[0], "Roboto", source / "rom" / "Roboto")
    add(PACKAGES[0], "Help.txt", data=(
        f"RuneScape 2 original Xbox - {args.version}\n\n"
        "Keep this complete folder together. Start the matching Windows server,\n"
        "copy its generated Xbox-config.ini here as config.ini, then FTP this\n"
        "folder to the Xbox. Launch default.xbe and press Start to log in.\n"
        "Read README.md for setup, controls, and preview limitations.\n"
    ).encode())

    add_tree(PACKAGES[1], "", template)
    add_tracked("Server225/engine", engine)
    add_tracked("Server225/content", content)
    add_tree(PACKAGES[1], "Server225/engine/data/pack", engine / "data" / "pack")
    sanitize_scripts = runpy.run_path(str(source / "scripts" / "sanitize-server-scripts.py"))["sanitize_scripts"]
    packed_scripts = engine / "data" / "pack" / "server"
    script_data, script_index, script_stats = sanitize_scripts(
        (packed_scripts / "script.dat").read_bytes(), (packed_scripts / "script.idx").read_bytes(), content)
    for filename, data in (("script.dat", script_data), ("script.idx", script_index)):
        relative = "Server225/engine/data/pack/server/" + filename
        plan.pop(PACKAGES[1] + "/" + relative)
        add(PACKAGES[1], relative, data=data)
    add_tree(PACKAGES[1], "Server225/engine/node_modules", engine / "node_modules", dependency=True)
    add(PACKAGES[1], "Server225/runtime/bun-windows-x64/bun.exe", workspace / "Server225" / "runtime" / "bun-windows-x64" / "bun.exe")
    for package in PACKAGES:
        public_base = f"https://github.com/TommySanzCode/rs2-xbox/blob/{args.version}/"
        readme = (source / "docs" / "RELEASE.md").read_text(encoding="utf-8").replace("(../CREDITS.md)", "(CREDITS.md)").replace("(THIRD-PARTY.md)", "(docs/THIRD-PARTY.md)")
        credits = (source / "CREDITS.md").read_text(encoding="utf-8").replace("(server-package/THIRD-PARTY.md)", "(THIRD-PARTY.md)" if package == PACKAGES[1] else f"({public_base}server-package/THIRD-PARTY.md)")
        third_party = (source / "docs" / "THIRD-PARTY.md").read_text(encoding="utf-8").replace(
            "(../server-package/THIRD-PARTY.md)", "(../THIRD-PARTY.md)" if package == PACKAGES[1] else f"({public_base}server-package/THIRD-PARTY.md)").replace(
            "(../release-notices/xbox/README.md)", "(../licenses/sdk/README.md)" if package == PACKAGES[0] else f"({public_base}release-notices/xbox/README.md)")
        add(package, "README.md", data=readme.encode())
        add(package, "CREDITS.md", data=credits.encode())
        add(package, "docs/THIRD-PARTY.md", data=third_party.encode())
        add_tree(package, "licenses", source / "licenses")
    sdk_map = {}
    sdk_root = source / "release-notices" / "xbox"
    if sdk_root.is_dir():
        for path, relative in tree_files(sdk_root):
            packaged = "/".join(fatx_component(part) for part in PurePosixPath(relative).parts)
            add(PACKAGES[0], "licenses/sdk/" + packaged, path)
            if packaged != relative:
                sdk_map[relative] = packaged
        if sdk_map:
            add(PACKAGES[0], "licenses/sdk/PATHS.json", data=(json.dumps(sdk_map, indent=2) + "\n").encode())

    for key in plan:
        if key.startswith(PACKAGES[0] + "/"):
            relative = key.split("/", 1)[1]
            if any(len(part) > 42 or fatx_component(part) != part for part in PurePosixPath(relative).parts):
                raise ValueError(f"Xbox filename is not FATX-safe: {relative}")
            if len("E:\\Games\\" + key.replace("/", "\\")) > 240:
                raise ValueError(f"Xbox install path is too long: {relative}")

    generated = {package + "/MANIFEST.json" for package in PACKAGES} | {PACKAGES[1] + "/RUNTIME-LICENSES.json"}
    if output.exists():
        for path in output.rglob("*"):
            if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
                raise ValueError("Staging contains a link; refusing to write through it.")
            if path.is_file():
                relative = path.relative_to(output).as_posix()
                if relative != marker.name and relative not in plan and relative not in generated:
                    raise ValueError(f"Unexpected existing staging file; use a new output directory: {relative}")
    output.mkdir(parents=True, exist_ok=True)
    atomic_stage(marker, data=(json.dumps({"tool": TOOL, "version": args.version, "schema": 1}) + "\n").encode())

    inventory: dict[str, dict] = {package: {} for package in PACKAGES}
    package_licenses = []
    for index, (key, (original, data)) in enumerate(sorted(plan.items()), 1):
        destination = output / key
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.resolve().is_relative_to(output):
            raise ValueError("Destination escapes staging.")
        if original is not None:
            old = destination.stat() if destination.exists() else None
            current = original.stat()
            if old is None or old.st_size != current.st_size or old.st_mtime_ns != current.st_mtime_ns:
                atomic_stage(destination, source=original)
        else:
            atomic_stage(destination, data=data)
        package, relative = key.split("/", 1)
        inventory[package][relative] = {"bytes": destination.stat().st_size, "sha256": sha256(destination)}
        if relative.startswith("Server225/engine/node_modules/") and destination.name == "package.json":
            try:
                meta = json.loads(destination.read_text(encoding="utf-8-sig"))
                if isinstance(meta.get("name"), str) and isinstance(meta.get("version"), str):
                    notices = [p.relative_to(output / package).as_posix() for p in destination.parent.iterdir()
                               if p.is_file() and re.match(r"(?i)^(license|licence|copying|notice)([._-]|$)", p.name)]
                    package_licenses.append({"name": meta["name"], "version": meta["version"], "declared_license": meta.get("license", meta.get("licenses")),
                                             "package_json": relative, "notice_files": sorted(notices)})
            except (ValueError, UnicodeError):
                pass
        if index % 5000 == 0:
            print(f"Staged and hashed {index}/{len(plan)} files", flush=True)

    runtime_inventory = output / PACKAGES[1] / "RUNTIME-LICENSES.json"
    atomic_stage(runtime_inventory, data=(json.dumps({"bun_version": args.bun_version, "packages": package_licenses,
        "note": "Package metadata and notice locations are recorded, not relicensed. Notices remain in the dependency folders."}, indent=2) + "\n").encode())
    inventory[PACKAGES[1]][runtime_inventory.name] = {"bytes": runtime_inventory.stat().st_size, "sha256": sha256(runtime_inventory)}
    patch = template / "patches" / "engine-local-bind.patch"
    pins = {"source_commit": revision(source), "source_working_tree_modified": bool(git(source, "status", "--porcelain")),
            "engine_commit": revision(engine), "content_commit": revision(content),
            "client_base_commit": build_record.get("client_upstream_commit"), "nxdk_commit": build_record.get("nxdk_commit"), "bun_version": args.bun_version,
            "engine_patch": {"path": "patches/engine-local-bind.patch", "sha256": sha256(patch)} if patch.is_file() else None}
    modified = git(engine, "diff", "--name-only", "HEAD").decode().splitlines()
    modified = [path for path in modified if excluded(path) is None]
    required_template = ("Start-Server.cmd", "Stop-Server.cmd", "Options.cmd", "Help.cmd", "Help.txt", "options.ini",
                         "scripts/Configure-Server.ps1", "scripts/initialize-server.mjs", "scripts/RS2-Server.Common.ps1",
                         "scripts/Start-RS2-Server.ps1", "scripts/Stop-RS2-Server.ps1", "Server225/run-local.ts",
                         "patches/engine-local-bind.patch", "THIRD-PARTY.md", "licenses/Bun-1.4.2-LICENSE.md")
    missing_template = [path for path in required_template if not (output / PACKAGES[1] / path).is_file()]
    for package in PACKAGES:
        manifest = {"release": args.version, "package": package, "pins": pins, "engine_modified_tracked_paths": modified,
                    "packed_script_metadata_sanitization": script_stats,
                    "xbe_build_path_sanitization": {"original_sha256": original_xbe_sha,
                        "original_bytes": len(original_xbe), "stats": xbe_stats},
                    "latest_optimized_hardware_fps_verified": False, "missing_server_template_files": missing_template,
                    "excluded_tracked_files": exclusions, "file_count": len(inventory[package]),
                    "total_bytes": sum(item["bytes"] for item in inventory[package].values()), "files": inventory[package]}
        atomic_stage(output / package / "MANIFEST.json", data=(json.dumps(manifest, indent=2) + "\n").encode())
        print(f"{package}: {manifest['file_count']} payload files, {manifest['total_bytes']} bytes; MANIFEST.json written", flush=True)
    if missing_template:
        print("Template incomplete; refresh before release: " + ", ".join(missing_template), file=sys.stderr)
    print("Staging complete. No server was started and no archive was created.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"Release assembly failed: {error}")
