"""Recompile stable engines with different repository/release stamps and compare actual native files."""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tempfile

from publish_launcher import ROOT, module_name


def verify(rid, payload):
    suffix = Path(module_name(rid)).suffix
    with tempfile.TemporaryDirectory(prefix="nexa-engine-repro-") as temporary:
        for engine in ("Pac", "Crypto", "Network"):
            project = ROOT / f"native/Nexa.Native.{engine}/Nexa.Native.{engine}.csproj"
            output = Path(temporary) / engine.lower()
            common = ["--configuration", "Release", "-r", rid, "-m:1", "-nodeReuse:false",
                      "-p:PublishAot=true", "-p:SelfContained=true", "-p:StripSymbols=true",
                      "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0",
                      "-p:XsrVersionSequence=999", "-p:XsrCommitShort=ffffff",
                      "-p:SourceRevisionId=" + "f" * 40, "-p:RepositoryCommit=" + "f" * 40]
            # Force compilation rather than accepting a cached publish with changed MSBuild inputs.
            subprocess.run(["dotnet", "clean", str(project), *common], check=True)
            subprocess.run(["dotnet", "publish", str(project), *common, "--output", str(output)], check=True)
            baseline = payload / ("nexa-" + engine.lower() + suffix)
            rebuilt = output / (f"Nexa.Native.{engine}" + suffix)
            expected = hashlib.sha256(baseline.read_bytes()).hexdigest()
            actual = hashlib.sha256(rebuilt.read_bytes()).hexdigest()
            if expected != actual:
                raise RuntimeError(f"{baseline.name} changed with repository-only metadata: {expected} != {actual}")
            print(f"Reproducible {baseline.name}: {expected}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--payload", required=True, type=Path)
    args = parser.parse_args()
    verify(args.rid, args.payload.resolve())
