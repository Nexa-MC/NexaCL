"""Measure an actual pair of published payloads; round-trip verification is mandatory."""
import argparse
import json
from pathlib import Path
import tempfile
import zipfile

from delta import build_bundle, digest
from package import copy_runtime_payload
from publish_launcher import module_names


def measure(before, after, output, rid, source_version, target_version):
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="nexa-module-delta-") as temporary:
        root = Path(temporary)
        source, target = root / "source", root / "target"
        copy_runtime_payload(before, source)
        copy_runtime_payload(after, target)
        files = {p.relative_to(target).as_posix(): dict(path=p.relative_to(target).as_posix(),
                 size=p.stat().st_size, sha256=digest(p), executable=bool(p.stat().st_mode & 0o111))
                 for p in target.rglob("*") if p.is_file()}
        patch = output / "measured-delta.zip"
        reused = build_bundle(source, target, files, patch, target_version, source_version, rid, "0" * 64)
        with zipfile.ZipFile(patch) as archive:
            manifest = json.loads(archive.read("manifest.json"))
        operations = {f["path"]: f["chunks"] for f in manifest["files"]}
        modules = []
        for name in module_names(rid):
            old, new = source / name, target / name
            modules.append(dict(name=name, bytes=new.stat().st_size, identical=digest(old) == digest(new),
                                newDataBytes=sum(size for kind, _, size in operations[name] if kind == "data")))
        result = dict(scope="supplied runtime payloads; symbols and installation state excluded",
                      beforeBytes=sum(p.stat().st_size for p in source.rglob("*") if p.is_file()),
                      afterBytes=sum(f["size"] for f in files.values()),
                      deltaBytes=patch.stat().st_size, reusedBytes=reused, roundTripVerified=True, modules=modules)
        (output / "measurement.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result, indent=2))
        return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for flag in ("before", "after", "output"):
        parser.add_argument("--" + flag, required=True, type=Path)
    for flag in ("rid", "source-version", "target-version"):
        parser.add_argument("--" + flag, required=True)
    args = parser.parse_args()
    measure(args.before, args.after, args.output, args.rid, args.source_version, args.target_version)
