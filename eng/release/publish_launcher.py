"""Publish the NativeAOT Host module and its matching minimal executable."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
ENGINES = ("Pac", "Crypto", "Network", "Updates", "Media", "Minecraft", "Markup")


def module_name(rid):
    platform, arch = rid.split("-")
    if platform not in ("win", "linux", "osx") or arch not in ("x64", "arm64"):
        raise ValueError("Unsupported RID")
    return "nexa-host" + {"win": ".dll", "linux": ".so", "osx": ".dylib"}[platform]


def module_names(rid):
    suffix = Path(module_name(rid)).suffix
    return ["nexa-" + name + suffix for name in ("host", *(engine.lower() for engine in ENGINES))]


def verify_host_engine_isolation(assets):
    libraries = json.loads(assets.read_text())["libraries"]
    forbidden = {"jint", "bouncycastle.cryptography", "zstdsharp.port"}
    forbidden.update("nexa.native." + engine.lower() for engine in ENGINES)
    embedded = sorted(name for name in libraries if name.split("/", 1)[0].lower() in forbidden)
    if embedded:
        raise RuntimeError(f"Stable engines leaked back into the Host dependency closure: {embedded}")


def publish(rid, output, properties):
    module = module_name(rid)
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    common = ["--configuration", "Release", "-r", rid, "--self-contained", "true",
              "-m:1", "-nodeReuse:false", "-p:PublishAot=true", "-p:StripSymbols=true"]
    with tempfile.TemporaryDirectory(prefix="nexa-native-") as temporary:
        stage = Path(temporary)
        host, bootstrap = stage / "host", stage / "bootstrap"
        engines = []
        # Stable engines never inherit the launcher release/commit stamp.
        for engine in ENGINES:
            directory = stage / engine.lower()
            subprocess.run(["dotnet", "publish", str(ROOT / f"native/Nexa.Native.{engine}/Nexa.Native.{engine}.csproj"),
                            *common, "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0",
                            "--output", str(directory)], check=True)
            binary = directory / (f"Nexa.Native.{engine}" + Path(module).suffix)
            if not binary.is_file():
                raise RuntimeError(f"Missing native {engine} engine")
            engines.append((binary, f"nexa-{engine.lower()}" + Path(module).suffix))
        subprocess.run(["dotnet", "publish", str(ROOT / "Nexa.Desktop/Nexa.Desktop.csproj"),
                        *common, *properties, "-p:NexaNativeModule=true", "-p:NexaExternalModules=true", "-p:NexaNetworkTestInternals=false", "--output", str(host)], check=True)
        verify_host_engine_isolation(ROOT / "Nexa.Desktop/obj/project.assets.json")
        subprocess.run(["dotnet", "publish", str(ROOT / "Nexa.Bootstrap/Nexa.Bootstrap.csproj"),
                        *common, *[p for p in properties if p.startswith(("-p:Xsr", "/p:Xsr"))],
                        "--output", str(bootstrap)], check=True)
        native = host / ("Nexa.Desktop" + Path(module).suffix)
        executable = "Nexa.Desktop.exe" if rid.startswith("win-") else "Nexa.Desktop"
        boot_executable = "Nexa.Bootstrap.exe" if rid.startswith("win-") else "Nexa.Bootstrap"
        if not native.is_file() or not (bootstrap / boot_executable).is_file():
            raise RuntimeError("Native publish did not produce both the Host and bootstrap")
        native.rename(host / module)
        # Do not merge bootstrap managed dependencies into the Host's resource directory.
        shutil.copytree(host, output, dirs_exist_ok=True)
        shutil.copy2(bootstrap / boot_executable, output / executable)
        for binary, name in engines:
            shutil.copy2(binary, output / name)
        for catalog in bootstrap.glob("Nexa.Bootstrap.nfp"):
            shutil.copy2(catalog, output / catalog.name)
        print(f"Native bootstrap: {(output / executable).stat().st_size} bytes; "
              f"{module}: {(output / module).stat().st_size} bytes", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args, properties = parser.parse_known_args()
    if any(not p.startswith(("-p:", "/p:")) for p in properties):
        parser.error("Extra arguments must be MSBuild properties")
    publish(args.rid, args.output, properties)
