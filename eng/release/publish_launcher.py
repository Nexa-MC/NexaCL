"""Publish the NativeAOT admission Host, complete UI runtime and matching executable."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = json.loads((ROOT / "eng/architecture/projects.json").read_text())


def project_directory(name):
    return ROOT / PROJECTS[name]


ENGINES = ("Pac", "Crypto", "Network", "Updates", "Media", "Minecraft", "Markup", "Artwork")
SERVICES = ("Accounts",)


def engine_service_properties(engine):
    return ["-p:NexaNativeServices=true"] if engine == "Minecraft" else ["-p:NexaExternalModules=true"] if engine == "Updates" else []


def stable_ui_properties(properties):
    return [*properties, "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0",
            "-p:IncludeSourceRevisionInInformationalVersion=false", "-p:EnableSourceLink=false",
            "-p:EnableSourceControlManagerQueries=false", "-p:EmbedUntrackedSources=false",
            "-p:PublishRepositoryUrl=false", "-p:PathMap=" + ROOT.as_posix() + "/=/_/"]


def module_name(rid):
    platform, arch = rid.split("-")
    if platform not in ("win", "linux", "osx") or arch not in ("x64", "arm64"):
        raise ValueError("Unsupported RID")
    return "nexa-host" + {"win": ".dll", "linux": ".so", "osx": ".dylib"}[platform]


def module_names(rid):
    suffix = Path(module_name(rid)).suffix
    return ["nexa-" + name + suffix for name in ("host", "ui", *(engine.lower() for engine in (*ENGINES, *SERVICES)))]


def verify_ui_engine_isolation(assets):
    libraries = json.loads(assets.read_text())["libraries"]
    forbidden = {"jint", "bouncycastle.cryptography", "zstdsharp.port", "nexa.services.accounts", "nexa.services.network", "nexa.native.accounts"}
    forbidden.update("nexa.services.minecraft." + name for name in ("metadata", "java", "process", "launch", "install", "management"))
    forbidden.update(("nexa.services.resources", "nexa.services.updates", "nexa.services.rollouts", "nexa.native.updates.codecs"))
    forbidden.update("nexa.native." + engine.lower() for engine in ENGINES)
    embedded = sorted(name for name in libraries if name.split("/", 1)[0].lower() in forbidden)
    if embedded:
        raise RuntimeError(f"Stable engines leaked back into the UI dependency closure: {embedded}")


def verify_host_module_isolation(assets):
    libraries = json.loads(assets.read_text())["libraries"]
    allowed = {"nexa.native.host", "nexa.xsr.patch.compiler"}
    embedded = sorted(name for name in libraries
                      if (stem := name.split("/", 1)[0].lower()) not in allowed
                      and (stem.startswith(("nexa.", "avalonia")) or stem in {"skiasharp", "harfbuzzsharp"}))
    if embedded:
        raise RuntimeError(f"UI or service implementation leaked back into the Host dependency closure: {embedded}")


def publish(rid, output, properties):
    subprocess.run([sys.executable, str(ROOT / "eng/xsr/generate_host_service_catalog.py"), "--check"], check=True)
    module = module_name(rid)
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    common = ["--configuration", "Release", "-r", rid, "--self-contained", "true",
              "-m:1", "-nodeReuse:false", "-p:PublishAot=true", "-p:StripSymbols=true"]
    with tempfile.TemporaryDirectory(prefix="nexa-native-") as temporary:
        stage = Path(temporary)
        host, ui, bootstrap = stage / "host", stage / "ui", stage / "bootstrap"
        engines = []
        # Stable engines never inherit the launcher release/commit stamp.
        for engine in ENGINES:
            directory = stage / engine.lower()
            subprocess.run(["dotnet", "publish", str(project_directory(f"Nexa.Native.{engine}") / f"Nexa.Native.{engine}.csproj"),
                            *common, *stable_ui_properties([]), *engine_service_properties(engine),
                            "--output", str(directory)], check=True)
            binary = directory / (f"Nexa.Native.{engine}" + Path(module).suffix)
            if not binary.is_file():
                raise RuntimeError(f"Missing native {engine} engine")
            engines.append((binary, f"nexa-{engine.lower()}" + Path(module).suffix))
        for service in SERVICES:
            destination = stage / service.lower()
            subprocess.run(["dotnet", "publish", str(project_directory(f"Nexa.Native.{service}") / f"Nexa.Native.{service}.csproj"),
                            *common, *stable_ui_properties(properties), "-p:NexaExternalModules=true", "-p:NexaNativeServices=false", "--output", str(destination)], check=True)
            binary = destination / (f"Nexa.Native.{service}" + Path(module).suffix)
            if not binary.is_file() or binary.stat().st_size == 0:
                raise RuntimeError(f"Missing native {service} service")
            engines.append((binary, f"nexa-{service.lower()}" + Path(module).suffix))
        subprocess.run(["dotnet", "publish", str(ROOT / "src/app/Nexa.Desktop/Nexa.Desktop.csproj"),
                        *common, *stable_ui_properties(properties), "-p:NexaNativeModule=true", "-p:NexaExternalModules=true", "-p:NexaNativeServices=true", "-p:NexaNetworkTestInternals=false", "--output", str(ui)], check=True)
        verify_ui_engine_isolation(ROOT / "src/app/Nexa.Desktop/obj/project.assets.json")
        subprocess.run(["dotnet", "publish", str(ROOT / "src/app/Nexa.Native.Host/Nexa.Native.Host.csproj"),
                        *common, "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0", "--output", str(host)], check=True)
        verify_host_module_isolation(ROOT / "src/app/Nexa.Native.Host/obj/project.assets.json")
        subprocess.run(["dotnet", "publish", str(ROOT / "src/app/Nexa.Bootstrap/Nexa.Bootstrap.csproj"),
                        *common, *[p for p in properties if p.startswith(("-p:Xsr", "/p:Xsr"))],
                        "--output", str(bootstrap)], check=True)
        native = host / ("Nexa.Native.Host" + Path(module).suffix)
        native_ui = ui / ("Nexa.Desktop" + Path(module).suffix)
        executable = "Nexa.Desktop.exe" if rid.startswith("win-") else "Nexa.Desktop"
        boot_executable = "Nexa.Bootstrap.exe" if rid.startswith("win-") else "Nexa.Bootstrap"
        if not native.is_file() or not native_ui.is_file() or not (bootstrap / boot_executable).is_file():
            raise RuntimeError("Native publish did not produce the Host, UI and bootstrap")
        native.rename(host / module)
        native_ui.rename(ui / ("nexa-ui" + Path(module).suffix))
        # Do not merge bootstrap managed dependencies into the Host's resource directory.
        shutil.copytree(ui, output, dirs_exist_ok=True)
        shutil.copy2(host / module, output / module)
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
