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
    try:
        subprocess.run(command, check=True, timeout=180, cwd=caller.parent)
    except subprocess.CalledProcessError as error:
        # A successful managed test body does not prove native process teardown.
        # Keep the original failure while collecting a native stack on macOS runners.
        if rid.startswith("osx-") and error.returncode < 0 and shutil.which("lldb"):
            diagnostic = output / "native-window-debugger.log"
            with diagnostic.open("w", encoding="utf-8") as log:
                try:
                    subprocess.run([
                        "lldb", "--batch", "-o", "settings set target.disable-aslr false",
                        "-o", "run", "-o", "thread backtrace all", "--", *command,
                    ], cwd=caller.parent, stdout=log, stderr=subprocess.STDOUT, timeout=180, check=False)
                except subprocess.TimeoutExpired:
                    log.write("\nNative debugger exceeded its diagnostic time budget.\n")
            print(diagnostic.read_text(encoding="utf-8", errors="replace")[-65536:], flush=True)
        raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    verify(options.rid, options.output)
