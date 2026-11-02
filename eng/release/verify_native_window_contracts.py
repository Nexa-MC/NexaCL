"""Paint a real window across independent NativeAOT runtimes (requires a desktop/display)."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from verify_native_ui_contracts import ROOT, test


def verify(rid: str, output: Path):
    output = output.resolve()
    caller, peer = test(rid, output, run_probe=False)
    native = output / "window"
    subprocess.run([
        "dotnet", "publish", str(ROOT / "src/rendering/Nexa.Native.Window"),
        "-c", "Release", "-r", rid, "--self-contained", "true", "-m:1", "-nr:false",
        "-p:PublishAot=true", "-o", str(native),
    ], check=True, cwd=ROOT)
    client = output / "client"
    client_project = ROOT / "tests/rendering/Nexa.Xsr.Renderer.Backend.Native.Tests"
    subprocess.run([
        "dotnet", "publish", str(client_project), "-c", "Release", "-r", rid,
        "--self-contained", "true", "-m:1", "-nr:false", "-p:PublishAot=true", "-o", str(client),
    ], check=True, cwd=ROOT)
    assets = json.loads((client_project / "obj/project.assets.json").read_text(encoding="utf-8"))
    if any(name.split("/")[0].startswith("Avalonia")
           or name.split("/")[0] == "Nexa.Xsr.Renderer.Backend.Avalonia"
           for name in assets["libraries"]):
        raise RuntimeError("Native window client embeds the backend implementation")
    forbidden = {"coreclr.dll", "libcoreclr.so", "libcoreclr.dylib", "System.Private.CoreLib.dll"}
    if any(path.name in forbidden for path in output.rglob("*")):
        raise RuntimeError("Native window carries CoreCLR")
    suffix = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    for directory in (caller.parent, client):
        for path in native.iterdir():
            if path.is_file():
                name = "nexa-window" + suffix if path.name == "Nexa.Native.Window" + suffix else path.name
                shutil.copy2(path, directory / name)
    client_executable = client / ("Nexa.Xsr.Renderer.Backend.Native.Tests" + (".exe" if rid.startswith("win-") else ""))
    commands = [
        ([str(caller), str(peer), str(caller.parent / ("nexa-window" + suffix))], caller.parent, True),
        ([str(client_executable), str(client / ("nexa-window" + suffix)), "headless"], client, False),
        ([str(client_executable), str(client / ("nexa-window" + suffix)), "window"], client, True),
    ]
    for command, directory, display in commands:
        if rid.startswith("linux-") and display:
            command = ["xvfb-run", "-a", *command]
        if rid.startswith("linux-") and directory == client and display:
            # Environment belongs to each independent runtime. Set the fixture path
            # before process entry instead of changing one runtime's managed cache.
            with tempfile.TemporaryDirectory(prefix="nexa-notification-") as temporary:
                notification = Path(temporary) / "notify-send"
                notification.write_text("#!/bin/sh\nprintf '%s\\n' nexacl\n", encoding="utf-8")
                notification.chmod(0o700)
                environment = {**os.environ, "PATH": temporary + os.pathsep + os.environ["PATH"],
                               "NEXA_NATIVE_NOTIFICATION_FIXTURE": "1"}
                run_window_probe(command, directory, rid, output, environment)
        else:
            run_window_probe(command, directory, rid, output)


def run_window_probe(command, directory, rid, output, environment=None):
    try:
        subprocess.run(command, check=True, timeout=180, cwd=directory, env=environment)
    except subprocess.CalledProcessError as error:
        # A successful managed test body does not prove native process teardown.
        # Keep the original failure while collecting a native stack on macOS runners.
        if rid.startswith("osx-") and error.returncode < 0 and shutil.which("lldb"):
            diagnostic = output / "native-window-debugger.log"
            with diagnostic.open("w", encoding="utf-8") as log:
                try:
                    subprocess.run([
                        "lldb", "--batch", "-o", "settings set target.disable-aslr false",
                        "-o", "process handle -p true -s false -n false SIGUSR1 SIGUSR2",
                        "-k", "thread backtrace all", "-o", "run", "--", *command,
                    ], cwd=directory, stdout=log, stderr=subprocess.STDOUT, timeout=180, check=False)
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
