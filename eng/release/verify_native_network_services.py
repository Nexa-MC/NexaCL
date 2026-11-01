"""Run the full native network service from an independent NativeAOT caller."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from publish_launcher import ROOT, module_name, stable_ui_properties


def verify(rid, output, payload=None):
    output.mkdir(parents=True, exist_ok=True)
    owner, caller = output / "network", output / "caller"
    common = ["-c", "Release", "-r", rid, "-m:1", "-nr:false", "-p:PublishAot=true", "-p:StripSymbols=true", *stable_ui_properties([])]
    if payload is None:
        subprocess.run(["dotnet", "publish", str(ROOT / "native/Nexa.Native.Network"), *common, "--output", str(owner)], check=True)
    subprocess.run(["dotnet", "publish", str(ROOT / "tests/Nexa.Native.Network.Services.Tests"), *common, "--output", str(caller)], check=True)
    subprocess.run([sys.executable, str(ROOT / "eng/xsr/generate_service_wire.py"),
                    "--assembly", str(ROOT / "native/Nexa.Native.Network.Client/bin/Release/net10.0/Nexa.Native.Network.Client.dll"),
                    "--roots", str(ROOT / "native/NetworkServiceProtocol/ServiceWire.roots"),
                    "--output", str(ROOT / "native/NetworkServiceProtocol/ServiceWire.Generated.cs"), "--check"], check=True)
    libraries = json.loads((ROOT / "tests/Nexa.Native.Network.Services.Tests/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Native.Network", "Nexa.Services.Network", "Nexa.Services.Common"} for name in libraries):
        raise RuntimeError("Network service caller embedded its owner")
    owner_libraries = json.loads((ROOT / "native/Nexa.Native.Network/obj/project.assets.json").read_text())["libraries"]
    for name in owner_libraries:
        assembly = name.split("/")[0]
        if assembly in {"Nexa.Desktop", "Nexa.Services", "Nexa.Services.Network", "Jint", "BouncyCastle.Cryptography"} or assembly.startswith(("Avalonia", "Nexa.Xsr.Renderer")):
            raise RuntimeError("Network service owner embedded a forbidden dependency: " + assembly)
    suffix = Path(module_name(rid)).suffix
    image = owner / ("Nexa.Native.Network" + suffix) if payload is None else payload / ("nexa-network" + suffix)
    shutil.copy2(image, caller / ("nexa-network" + suffix))
    executable = caller / ("Nexa.Native.Network.Services.Tests" + (".exe" if rid.startswith("win-") else ""))
    with tempfile.TemporaryDirectory(prefix="nexa-network-cwd-") as cwd:
        subprocess.run([str(executable.resolve())], cwd=cwd, check=True, timeout=110)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--payload", type=Path, help="Reuse an existing isolated network image")
    args = parser.parse_args()
    verify(args.rid, args.output.resolve(), args.payload.resolve() if args.payload else None)
