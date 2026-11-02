"""Freshly recompile UI with different product/Git inputs; unchanged UI must retain its exact bytes."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile

from publish_launcher import ROOT, module_name, stable_ui_properties


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify(rid, payload, properties):
    project = ROOT / "src/app/Nexa.Desktop/Nexa.Desktop.csproj"
    suffix = Path(module_name(rid)).suffix
    baseline = payload / ("nexa-ui" + suffix)
    expected = sha256(baseline)
    common = ["--configuration", "Release", "-r", rid, "-m:1", "-nodeReuse:false",
              "-p:PublishAot=true", "-p:SelfContained=true", "-p:StripSymbols=true",
              *stable_ui_properties([*properties, "-p:XsrVersionChannel=beta", "-p:XsrVersionPrefix=9.8.7",
                                     "-p:XsrVersionSequence=999", "-p:XsrCommitShort=ffffff"]),
              "-p:SourceRevisionId=" + "f" * 40, "-p:RepositoryCommit=" + "f" * 40,
              "-p:NexaNativeModule=true", "-p:NexaExternalModules=true", "-p:NexaNativeServices=true", "-p:NexaNetworkTestInternals=false"]
    with tempfile.TemporaryDirectory(prefix="nexa-ui-repro-") as temporary:
        output = Path(temporary)
        subprocess.run(["dotnet", "clean", str(project), *common], check=True, cwd=ROOT)
        subprocess.run(["dotnet", "publish", str(project), *common, "--output", str(output)], check=True, cwd=ROOT)
        rebuilt = output / ("Nexa.Desktop" + suffix)
        actual = sha256(rebuilt)
        if expected != actual:
            diagnostics = ROOT / "install-diagnostics" / ("native-ui-repro-" + rid)
            diagnostics.mkdir(parents=True, exist_ok=True)
            shutil.copy2(baseline, diagnostics / ("baseline-" + baseline.name))
            shutil.copy2(rebuilt, diagnostics / ("rebuilt-" + baseline.name))
            raise RuntimeError(f"UI changed with product/Git-only metadata: {expected} != {actual}")
        print(f"Reproducible nexa-ui{suffix}: {expected}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--payload", required=True, type=Path)
    args, properties = parser.parse_known_args()
    if any(not value.startswith(("-p:", "/p:")) for value in properties):
        parser.error("Only MSBuild properties may follow the options")
    verify(args.rid, args.payload.resolve(), properties)
