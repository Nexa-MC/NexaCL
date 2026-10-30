"""Publish the NativeAOT Host module and its matching minimal executable."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def module_name(rid):
    platform, arch = rid.split("-")
    if platform not in ("win", "linux", "osx") or arch not in ("x64", "arm64"):
        raise ValueError("Unsupported RID")
    return "nexa-host" + {"win": ".dll", "linux": ".so", "osx": ".dylib"}[platform]


def publish(rid, output, properties):
    module = module_name(rid)
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    common = ["--configuration", "Release", "-r", rid, "--self-contained", "true",
              "-m:1", "-nodeReuse:false", "-p:PublishAot=true", "-p:StripSymbols=true"]
    with tempfile.TemporaryDirectory(prefix="nexa-native-") as temporary:
        stage = Path(temporary)
        host, bootstrap = stage / "host", stage / "bootstrap"
        subprocess.run(["dotnet", "publish", str(ROOT / "Nexa.Desktop/Nexa.Desktop.csproj"),
                        *common, *properties, "-p:NexaNativeModule=true", "--output", str(host)], check=True)
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
