"""Exercise Minecraft ownership from an independent NativeAOT caller and OS children."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from publish_launcher import ROOT, module_name, stable_ui_properties, engine_service_properties


def verify(rid, output, payload=None):
    output.mkdir(parents=True, exist_ok=True)
    owner, caller = output / "minecraft", output / "caller"
    common = ["-c", "Release", "-r", rid, "-m:1", "-nr:false", "-p:PublishAot=true", "-p:StripSymbols=true", *stable_ui_properties([])]
    if payload is None:
        subprocess.run(["dotnet", "publish", str(ROOT / "native/Nexa.Native.Network"), *common, "--output", str(output / "network")], check=True)
        subprocess.run(["dotnet", "publish", str(ROOT / "native/Nexa.Native.Minecraft"), *common, *engine_service_properties("Minecraft"), "--output", str(owner)], check=True)
    subprocess.run(["dotnet", "publish", str(ROOT / "tests/Nexa.Native.Minecraft.Services.Tests"), *common, "--output", str(caller)], check=True)
    subprocess.run([sys.executable, str(ROOT / "eng/xsr/generate_service_wire.py"),
                    "--assembly", str(ROOT / "native/Nexa.Native.Minecraft.Client/bin/Release/net10.0/Nexa.Native.Minecraft.Client.dll"),
                    "--roots", str(ROOT / "native/MinecraftServiceProtocol/ServiceWire.roots"),
                    "--output", str(ROOT / "native/MinecraftServiceProtocol/ServiceWire.Generated.cs"), "--check"], check=True)
    libraries = json.loads((ROOT / "tests/Nexa.Native.Minecraft.Services.Tests/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Native.Minecraft", "Nexa.Services.Resources"} or name.split("/")[0].startswith("Nexa.Services.Minecraft.")
           and not name.split("/")[0].endswith(".Contracts") for name in libraries):
        raise RuntimeError("Minecraft caller embedded an owner implementation")
    owner_libraries = json.loads((ROOT / "native/Nexa.Native.Minecraft/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Desktop", "Nexa.Services", "Nexa.Services.Composition"}
           or name.startswith(("Avalonia", "Nexa.Xsr.Renderer")) for name in owner_libraries):
        raise RuntimeError("Minecraft owner embedded a presentation dependency")
    suffix = Path(module_name(rid)).suffix
    image = owner / ("Nexa.Native.Minecraft" + suffix) if payload is None else payload / ("nexa-minecraft" + suffix)
    shutil.copy2(image, caller / ("nexa-minecraft" + suffix))
    network = output / "network" / ("Nexa.Native.Network" + suffix) if payload is None else payload / ("nexa-network" + suffix)
    shutil.copy2(network, caller / ("nexa-network" + suffix))
    executable = caller / ("Nexa.Native.Minecraft.Services.Tests" + (".exe" if rid.startswith("win-") else ""))
    with tempfile.TemporaryDirectory(prefix="nexa-minecraft-cwd-") as cwd:
        subprocess.run([str(executable.resolve())], cwd=cwd, check=True, timeout=110)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--payload", type=Path)
    args = parser.parse_args()
    verify(args.rid, args.output.resolve(), args.payload.resolve() if args.payload else None)
