"""Paint a real window across independent NativeAOT runtimes (requires a desktop/display)."""
import argparse
from pathlib import Path
import shutil
import subprocess
from verify_native_ui_contracts import ROOT, test


def verify(rid: str, output: Path):
    output = output.resolve()
    caller, peer = test(rid, output, run_probe=False)
    native = output / "window"
    subprocess.run([
        "dotnet", "publish", str(ROOT / "native/Nexa.Native.Window"),
        "-c", "Release", "-r", rid, "--self-contained", "true", "-m:1", "-nr:false",
        "-p:PublishAot=true", "-o", str(native),
    ], check=True, cwd=ROOT)
    forbidden = {"coreclr.dll", "libcoreclr.so", "libcoreclr.dylib", "System.Private.CoreLib.dll"}
    if any(path.name in forbidden for path in output.rglob("*")):
        raise RuntimeError("Native window carries CoreCLR")
    for path in native.iterdir():
        if path.is_file():
            shutil.copy2(path, caller.parent / path.name)
    suffix = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    window = caller.parent / ("nexa-window" + suffix)
    (caller.parent / ("Nexa.Native.Window" + suffix)).rename(window)
    command = [str(caller), str(peer), str(window)]
    if rid.startswith("linux-"):
        command = ["xvfb-run", "-a", *command]
    subprocess.run(command, check=True, timeout=180, cwd=caller.parent)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    verify(options.rid, options.output)
