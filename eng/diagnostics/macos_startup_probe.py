"""Bounded normal-startup evidence on disposable GitHub macOS runners."""
import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tarfile
import time


WINDOWS_SWIFT = r'''
import AppKit
import CoreGraphics
import Foundation
let pid = Int32(CommandLine.arguments[1])!
let all = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
let windows = all.filter { ($0[kCGWindowOwnerPID as String] as? Int32) == pid }
let data = try JSONSerialization.data(withJSONObject: windows, options: [.sortedKeys])
print(String(data: data, encoding: .utf8)!)
'''


def run(command, output, timeout=120, env=None):
    with output.open("w") as stream:
        result = subprocess.run([str(arg) for arg in command], stdout=stream, stderr=subprocess.STDOUT,
                                timeout=timeout, env=env, check=False)
    return result.returncode


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", choices=("osx-x64", "osx-arm64"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=True)
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.uname().sysname != "Darwin":
        raise SystemExit("This diagnostic requires a disposable GitHub macOS runner")
    swift = root / "windows.swift"
    swift.write_text(WINDOWS_SWIFT)
    executable = root / "window-list"
    if run(["swiftc", swift, "-o", executable], root / "swift-build.log"):
        raise SystemExit("Window enumeration probe did not compile")
    name = f"Nexa-2.0.0.alpha.7-{args.rid}.portable.tar.gz"
    downloaded = root / "download"
    downloaded.mkdir(exist_ok=True)
    if run(["gh", "release", "download", "v2.0.0.alpha.7", "--repo", "Nexa-MC/PCL-N",
            "--pattern", name, "--dir", downloaded, "--clobber"], root / "download.log"):
        raise SystemExit("Could not download the published Alpha 7 package")
    unpacked = root / "published"
    unpacked.mkdir(exist_ok=True)
    with tarfile.open(downloaded / name) as archive:
        archive.extractall(unpacked, filter="data")
    binary = next(unpacked.rglob("Contents/MacOS/Nexa.Desktop"))
    run(["codesign", "--verify", "--deep", "--strict", binary.parents[2]], root / "codesign.log")
    outcomes = []
    for scenario in ("first-run", "configured", "safe-mode"):
        data = root / ("data-" + scenario)
        data.mkdir(exist_ok=True)
        if scenario != "first-run":
            settings = data / "settings"
            settings.mkdir()
            (settings / "settings.json").write_text(json.dumps(dict(schemaVersion=1,
                booleanOptions={}, integerOptions={}, textOptions={})))
        environment = dict(os.environ, NEXA_DATA_DIR=str(data), NEXA_COUNTRY="US")
        command = [str(binary)] + (["--safe-mode"] if scenario == "safe-mode" else [])
        observed = []
        succeeded = False
        with (root / f"startup-{scenario}.log").open("w") as stream:
            process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                       env=environment, start_new_session=True)
            try:
                deadline = time.monotonic() + 75
                while time.monotonic() < deadline and process.poll() is None:
                    result = subprocess.run([executable, str(process.pid)], capture_output=True, text=True,
                                            timeout=10, check=False)
                    windows = json.loads(result.stdout) if result.returncode == 0 and result.stdout.strip() else []
                    observed.append(dict(elapsed=75 - max(0, deadline - time.monotonic()), windows=windows))
                    succeeded = any(window.get("kCGWindowBounds", {}).get("Width", 0) >= 700
                                    and window.get("kCGWindowBounds", {}).get("Height", 0) >= 400 for window in windows)
                    if succeeded:
                        time.sleep(1)
                        succeeded = process.poll() is None
                        break
                    time.sleep(.5)
                if process.poll() is None:
                    run(["sample", process.pid, "1", "-file", root / f"sample-{scenario}.txt"],
                        root / f"sample-{scenario}.log", timeout=15)
                outcomes.append(dict(scenario=scenario, visibleMainWindow=succeeded,
                                     earlyExit=process.poll(), observations=observed))
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=10)
    reports = Path.home() / "Library/Logs/DiagnosticReports"
    if reports.exists():
        for report in reports.glob("*Nexa*"):
            if report.is_file():
                shutil.copy2(report, root / report.name)
    (root / "startup-results.json").write_text(json.dumps(outcomes, indent=2))
    print(json.dumps(outcomes, indent=2))
    # Do not duplicate the downloaded payload in the evidence artifact.
    shutil.rmtree(downloaded)
    shutil.rmtree(unpacked)
    if not all(item["visibleMainWindow"] for item in outcomes):
        raise SystemExit("Published normal-startup probe found a failure; see startup-results.json")


if __name__ == "__main__":
    main()
