"""Exercise the window boundary between two independently published NativeAOT runtimes."""
import argparse
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def test(rid: str, output: Path):
    output = output.resolve()
    for name, folder in (("Nexa.Native.Ui.Peer", "peer"), ("Nexa.Native.Ui.Tests", "caller")):
        subprocess.run([
            "dotnet", "publish", str(ROOT / "tests" / name / (name + ".csproj")),
            "-c", "Release", "-r", rid, "--self-contained", "true", "-m:1", "-nr:false",
            "-p:PublishAot=true", "-o", str(output / folder),
        ], check=True, cwd=ROOT)
        assets = json.loads((ROOT / "tests" / name / "obj/project.assets.json").read_text(encoding="utf-8"))
        if any(library.split("/")[0].startswith("Avalonia")
               or library.split("/")[0] == "Nexa.Xsr.Renderer.Backend.Avalonia"
               for library in assets["libraries"]):
            raise RuntimeError("Native UI value contracts unexpectedly reference the Avalonia implementation")
    forbidden = {"coreclr.dll", "libcoreclr.so", "libcoreclr.dylib", "System.Private.CoreLib.dll"}
    if any(path.name in forbidden for path in output.rglob("*")):
        raise RuntimeError("UI contract probe unexpectedly carries a CoreCLR payload")
    suffix = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    caller = output / "caller" / ("Nexa.Native.Ui.Tests" + (".exe" if rid.startswith("win-") else ""))
    peer = output / "peer" / ("Nexa.Native.Ui.Peer" + suffix)
    subprocess.run([str(caller), str(peer)], check=True, timeout=90, cwd=output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    test(options.rid, options.output)
