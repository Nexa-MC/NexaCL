"""Exercise the real published ABI, paths and failure behavior on each native runner."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile


def verify(payload):
    payload = payload.resolve()
    executable = payload / ("Nexa.Desktop.exe" if (payload / "Nexa.Desktop.exe").exists() else "Nexa.Desktop")
    with tempfile.TemporaryDirectory(prefix="nexa-module-smoke-") as temporary:
        working = Path(temporary)
        # A foreign working directory must not affect library or Host resource resolution.
        result = subprocess.run([str(executable), "--list-commands"], cwd=working,
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        if result.returncode != 0:
            raise RuntimeError(f"Host ABI smoke failed: {result.returncode}\n{result.stderr}")
        result = subprocess.run([str(executable), "--command=不存在"], cwd=working,
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        if result.returncode != 2:
            raise RuntimeError(f"Host argument/exit-code forwarding failed: {result.returncode}")
        detached = working / "detached"
        detached.mkdir()
        shutil.copy2(executable, detached / executable.name)
        # Working directory contains the real Host; an incomplete installation must still fail.
        result = subprocess.run([str(detached / executable.name), "--list-commands"], cwd=payload,
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        if result.returncode != 1 or "nexa-host" not in result.stderr:
            raise RuntimeError(f"Missing Host did not fail cleanly: {result.returncode}\n{result.stderr}")
    print("Native Host ABI, independent working directory and missing-module smoke passed.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--payload", required=True, type=Path)
    verify(parser.parse_args().payload)
