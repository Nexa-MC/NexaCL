"""Exercise Host-owned service composition and physical client retirement."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from publish_launcher import ROOT, module_name, stable_ui_properties, engine_service_properties, verify_host_module_isolation


def verify(rid, output, payload=None):
    output.mkdir(parents=True, exist_ok=True)
    common = ["-c", "Release", "-r", rid, "-m:1", "-nr:false", "-p:PublishAot=true", "-p:StripSymbols=true", *stable_ui_properties([])]
    subprocess.run([sys.executable, str(ROOT / "eng/xsr/generate_host_service_catalog.py"), "--check"], check=True)
    subprocess.run(["dotnet", "publish", str(ROOT / "native/Nexa.Native.Host"), *common, "--output", str(output / "host")], check=True)
    verify_host_module_isolation(ROOT / "native/Nexa.Native.Host/obj/project.assets.json")
    if payload is None:
        for engine in ("Network", "Updates", "Crypto"):
            subprocess.run(["dotnet", "publish", str(ROOT / ("native/Nexa.Native." + engine)), *common, *engine_service_properties(engine), "--output", str(output / engine.lower())], check=True)
    caller = output / "caller"
    subprocess.run(["dotnet", "publish", str(ROOT / "tests/Nexa.Native.Host.Services.Tests"), *common, "--output", str(caller)], check=True)
    libraries = json.loads((ROOT / "tests/Nexa.Native.Host.Services.Tests/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Native.Host", "Nexa.Native.Network", "Nexa.Native.Updates", "Nexa.Services.Updates", "Nexa.Services.Network", "Nexa.Services.Rollouts", "Nexa.Platform.Runtime"} for name in libraries):
        raise RuntimeError("Host caller embedded native service implementations")
    suffix = Path(module_name(rid)).suffix
    shutil.copy2(output / "host" / ("Nexa.Native.Host" + suffix), caller / ("nexa-host" + suffix))
    for engine in ("Network", "Updates", "Crypto"):
        source = output / engine.lower() / ("Nexa.Native." + engine + suffix) if payload is None else payload / ("nexa-" + engine.lower() + suffix)
        shutil.copy2(source, caller / ("nexa-" + engine.lower() + suffix))
    executable = caller / ("Nexa.Native.Host.Services.Tests" + (".exe" if rid.startswith("win-") else ""))
    with tempfile.TemporaryDirectory(prefix="nexa-host-services-") as temporary:
        working = Path(temporary)
        fixture = ROOT / "tests/Nexa.Native.Updates.Services.Tests/Fixtures" / rid
        subprocess.run([str(executable.resolve()), str(fixture)], cwd=working, check=True, timeout=90)
        for failed in ("host", "network"):
            rejected = working / failed
            rejected.mkdir()
            shutil.copy2(executable, rejected / executable.name)
            if failed == "network":
                shutil.copy2(caller / ("nexa-host" + suffix), rejected / ("nexa-host" + suffix))
            subprocess.run([str(rejected / executable.name), "--reject-image"], cwd=working, check=True, timeout=30)
        # A loadable image with unrelated exports cannot impersonate a business owner.
        shutil.copy2(caller / ("nexa-crypto" + suffix), rejected / ("nexa-network" + suffix))
        subprocess.run([str(rejected / executable.name), "--reject-image"], cwd=working, check=True, timeout=30)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--payload", type=Path)
    args = parser.parse_args()
    verify(args.rid, args.output.resolve(), args.payload.resolve() if args.payload else None)
