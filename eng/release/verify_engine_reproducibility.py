"""Recompile stable engines with different repository/release stamps and compare actual native files."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
import subprocess
import tempfile

from publish_launcher import ENGINES, SERVICES, ROOT, module_name, stable_ui_properties


def verify(rid, payload, selected=None):
    suffix = Path(module_name(rid)).suffix
    # The isolated-engine job publishes only the eight engines. Full launcher payloads
    # additionally carry the admission Host and must prove its reproducibility too.
    extras = [name for name in ("Host", *SERVICES) if (payload / ("nexa-" + name.lower() + suffix)).is_file()]
    modules = (*ENGINES, *extras)
    if selected:
        missing = set(selected) - set(modules)
        if missing:
            raise ValueError(f"Requested modules are missing from the payload: {sorted(missing)}")
        modules = tuple(name for name in modules if name in selected)
    with tempfile.TemporaryDirectory(prefix="nexa-engine-repro-") as temporary:
        for engine in modules:
            project = ROOT / f"native/Nexa.Native.{engine}/Nexa.Native.{engine}.csproj"
            output = Path(temporary) / engine.lower()
            common = ["--configuration", "Release", "-r", rid, "-m:1", "-nodeReuse:false",
                      "-p:PublishAot=true", "-p:SelfContained=true", "-p:StripSymbols=true",
                      "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0",
                      "-p:XsrVersionSequence=999", "-p:XsrCommitShort=ffffff",
                      "-p:SourceRevisionId=" + "f" * 40, "-p:RepositoryCommit=" + "f" * 40]
            if engine in SERVICES:
                common = [*stable_ui_properties(common), "-p:NexaExternalModules=true", "-p:NexaNativeServices=false"]
            intermediate = project.parent / "obj" / "Release" / "net10.0" / rid
            inputs = [intermediate / f"Nexa.Native.{engine}.dll",
                      intermediate / "native" / (f"Nexa.Native.{engine}" + (".obj" if rid.startswith("win-") else ".o"))]
            before_inputs = {path: path.read_bytes() for path in inputs if path.is_file()}
            # Force compilation rather than accepting a cached publish with changed MSBuild inputs.
            subprocess.run(["dotnet", "clean", str(project), *common], check=True)
            subprocess.run(["dotnet", "publish", str(project), *common, "--output", str(output)], check=True)
            baseline = payload / ("nexa-" + engine.lower() + suffix)
            rebuilt = output / (f"Nexa.Native.{engine}" + suffix)
            expected = hashlib.sha256(baseline.read_bytes()).hexdigest()
            actual = hashlib.sha256(rebuilt.read_bytes()).hexdigest()
            if expected != actual:
                diagnostics = ROOT / "install-diagnostics" / ("native-repro-" + rid)
                diagnostics.mkdir(parents=True, exist_ok=True)
                shutil.copy2(baseline, diagnostics / ("baseline-" + baseline.name))
                shutil.copy2(rebuilt, diagnostics / ("rebuilt-" + baseline.name))
                input_hashes = {}
                for path, content in before_inputs.items():
                    if path.is_file():
                        (diagnostics / ("baseline-" + path.name)).write_bytes(content)
                        shutil.copy2(path, diagnostics / ("rebuilt-" + path.name))
                        input_hashes[path.name] = {
                            "baseline": hashlib.sha256(content).hexdigest(),
                            "rebuilt": hashlib.sha256(path.read_bytes()).hexdigest(),
                        }
                before, after = baseline.read_bytes(), rebuilt.read_bytes()
                offsets = [i for i, (left, right) in enumerate(zip(before, after)) if left != right]
                (diagnostics / (baseline.name + ".json")).write_text(json.dumps({
                    "inputs": input_hashes,
                    "baseline_sha256": expected, "rebuilt_sha256": actual,
                    "baseline_size": len(before), "rebuilt_size": len(after),
                    "changed_bytes": len(offsets) + abs(len(before) - len(after)),
                    "first_changed_offsets": offsets[:128],
                }, indent=2))
                raise RuntimeError(f"{baseline.name} changed with repository-only metadata: {expected} != {actual}")
            print(f"Reproducible {baseline.name}: {expected}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--payload", required=True, type=Path)
    parser.add_argument("--rebuilds", type=int, choices=(1, 2, 3), default=1)
    parser.add_argument("--modules", nargs="+", choices=(*ENGINES, "Host", *SERVICES))
    args = parser.parse_args()
    for iteration in range(args.rebuilds):
        print(f"Clean reproducibility round {iteration + 1}/{args.rebuilds}", flush=True)
        verify(args.rid, args.payload.resolve(), args.modules)
