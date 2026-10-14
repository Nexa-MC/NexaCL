#!/usr/bin/env python3
"""Create reproducible NTP/1 ZIPs and verify that author assets were published.

This is a packaging tool. ThemePackageService remains the runtime admission authority.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import stat
import sys
import zipfile


MIMETYPE = b"application/x-nexa-theme"
MAX_ENTRIES = 4096
MAX_ENTRY_BYTES = 128 * 1024 * 1024
MAX_TOTAL_BYTES = 512 * 1024 * 1024
FORBIDDEN_SUFFIXES = {
    ".nxml", ".dll", ".exe", ".so", ".dylib", ".js", ".ps1", ".py", ".sh",
}
WINDOWS_RESERVED = re.compile(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", re.I)


def source_files(directory: pathlib.Path) -> list[tuple[str, bytes]]:
    if directory.is_symlink():
        raise ValueError("The theme root cannot be a symbolic link.")
    if not directory.is_dir():
        raise ValueError(f"Missing theme directory: {directory}")
    files: list[tuple[str, bytes]] = []
    names: set[str] = set()
    total = 0
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Symbolic links are not theme assets: {path.name}")
        if path.is_dir():
            continue
        if not stat.S_ISREG(path.stat().st_mode):
            raise ValueError(f"Not a regular theme file: {path.name}")
        name = path.relative_to(directory).as_posix()
        if any(part.endswith((".", " ")) or ":" in part or "\\" in part
               or WINDOWS_RESERVED.match(part) for part in name.split("/")):
            raise ValueError(f"Ambiguous cross-platform path: {name}")
        canonical = name.casefold()
        if canonical in names:
            raise ValueError(f"Duplicate cross-platform path: {name}")
        names.add(canonical)
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            raise ValueError(f"Executable or presentation replacement is prohibited: {name}")
        size = path.stat().st_size
        if size > MAX_ENTRY_BYTES:
            raise ValueError(f"Theme entry exceeds its byte budget: {name}")
        data = path.read_bytes()
        if len(data) > MAX_ENTRY_BYTES:
            raise ValueError(f"Theme entry changed beyond its byte budget: {name}")
        total += len(data)
        if total > MAX_TOTAL_BYTES:
            raise ValueError("Theme expansion exceeds its byte budget.")
        files.append((name, data))
        if len(files) > MAX_ENTRIES:
            raise ValueError("Theme exceeds its entry budget.")
    values = dict(files)
    if values.get("mimetype", b"").rstrip(b"\n") != MIMETYPE:
        raise ValueError("mimetype must contain application/x-nexa-theme.")
    if "theme.json" not in values or len(values["theme.json"]) > 65536:
        raise ValueError("Missing or oversized theme.json.")
    manifest = json.loads(values["theme.json"])
    if manifest.get("format") != "NTP/1" or manifest.get("styleApi") != 1 or manifest.get("nss") != "NSS/1":
        raise ValueError("theme.json must declare NTP/1, styleApi 1 and NSS/1.")
    styles = manifest.get("styles")
    if not isinstance(styles, list) or not 1 <= len(styles) <= 64:
        raise ValueError("A theme must declare between 1 and 64 NSS files.")
    if any(not isinstance(name, str) or not name.endswith(".nss") or name not in values for name in styles):
        raise ValueError("Every declared style must identify an included .nss file.")
    if sum(len(values[name]) for name in styles) > 1024 * 1024:
        raise ValueError("Declared NSS exceeds the total byte budget.")
    return files


def pack(directory: pathlib.Path, output: pathlib.Path) -> None:
    directory = directory.absolute()
    output = output.absolute()
    if output.is_relative_to(directory):
        raise ValueError("Write the package outside its source directory.")
    files = source_files(directory)
    output.parent.mkdir(parents=True, exist_ok=True)
    # Keep the exact mimetype first and uncompressed; all metadata is deterministic.
    files = [("mimetype", MIMETYPE)] + [(name, data) for name, data in files if name != "mimetype"]
    temporary = output.with_name(output.name + ".tmp")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in files:
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_STORED if name == "mimetype" else zipfile.ZIP_DEFLATED
                archive.writestr(info, data)
        if temporary.stat().st_size > MAX_ENTRY_BYTES:
            raise ValueError("Compressed theme package exceeds its byte budget.")
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Packed {len(files)} entries: {output}")


def verify_published(source: pathlib.Path, published: pathlib.Path) -> None:
    manifests = sorted(source.glob("*/theme.json"))
    if not manifests:
        raise ValueError("No author themes found.")
    count = 0
    for manifest in manifests:
        directory = manifest.parent
        for name, data in source_files(directory):
            target = published / directory.name / name
            if not target.is_file() or target.read_bytes() != data:
                raise ValueError(f"Missing or altered published theme resource: {directory.name}/{name}")
            count += 1
    print(f"Verified {len(manifests)} themes and {count} external resource files.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=pathlib.Path, help="Theme source directory, or Themes root when verifying")
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--output", type=pathlib.Path, help="Reproducible .ntp output outside the source directory")
    target.add_argument("--verify-published", type=pathlib.Path, help="Published Themes root to compare byte for byte")
    args = parser.parse_args()
    try:
        if args.output:
            pack(args.directory, args.output)
        else:
            verify_published(args.directory, args.verify_published)
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        print(f"NTP packaging failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
