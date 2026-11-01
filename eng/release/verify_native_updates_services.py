"""Verify Updates business ownership from an independent NativeAOT caller."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

from publish_launcher import ROOT, module_name, stable_ui_properties, engine_service_properties


def create_fixture(directory, rid):
    source = ROOT / "tests/Nexa.Native.Updates.Services.Tests/Fixtures" / rid
    for file in source.iterdir():
        shutil.copy2(file, directory / file.name)


def verify(rid, output, payload=None):
    output.mkdir(parents=True, exist_ok=True)
    caller = output / "caller"
    common = ["-c", "Release", "-r", rid, "-m:1", "-nr:false", "-p:PublishAot=true", "-p:StripSymbols=true", *stable_ui_properties([])]
    if payload is None:
        for engine in ("Updates", "Crypto"):
            subprocess.run(["dotnet", "publish", str(ROOT / ("native/Nexa.Native." + engine)), *common, *engine_service_properties(engine), "--output", str(output / engine.lower())], check=True)
    subprocess.run(["dotnet", "publish", str(ROOT / "tests/Nexa.Native.Updates.Services.Tests"), *common, "--output", str(caller)], check=True)
    subprocess.run(["python", str(ROOT / "eng/xsr/generate_service_wire.py"),
                    "--assembly", str(ROOT / "native/Nexa.Native.Updates.Client/bin/Release/net10.0/Nexa.Native.Updates.Client.dll"),
                    "--roots", str(ROOT / "native/UpdatesServiceProtocol/ServiceWire.roots"),
                    "--output", str(ROOT / "native/UpdatesServiceProtocol/ServiceWire.Generated.cs"), "--check"], check=True)
    libraries = json.loads((ROOT / "tests/Nexa.Native.Updates.Services.Tests/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Services.Updates", "Nexa.Services.Rollouts", "Nexa.Native.Updates", "Nexa.Native.Updates.Codecs", "Nexa.Platform.Runtime"} for name in libraries):
        raise RuntimeError("Updates caller embedded business implementations")
    libraries = json.loads((ROOT / "native/Nexa.Native.Updates/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Desktop", "Nexa.Services", "Nexa.Services.Composition"}
           or name.startswith(("Avalonia", "Nexa.Xsr.Renderer")) for name in libraries):
        raise RuntimeError("Updates owner embedded presentation dependencies")
    suffix = Path(module_name(rid)).suffix
    for engine in ("Updates", "Crypto"):
        source = output / engine.lower() / ("Nexa.Native." + engine + suffix) if payload is None else payload / ("nexa-" + engine.lower() + suffix)
        shutil.copy2(source, caller / ("nexa-" + engine.lower() + suffix))
    with tempfile.TemporaryDirectory(prefix="nexa-updates-fixture-") as temporary:
        fixture = Path(temporary)
        create_fixture(fixture, rid)
        executable = caller / ("Nexa.Native.Updates.Services.Tests" + (".exe" if rid.startswith("win-") else ""))
        subprocess.run([str(executable.resolve()), str(fixture), rid], cwd=fixture, check=True, timeout=90)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--payload", type=Path)
    args = parser.parse_args()
    verify(args.rid, args.output.resolve(), args.payload.resolve() if args.payload else None)
