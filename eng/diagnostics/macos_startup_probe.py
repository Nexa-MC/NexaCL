"""Bounded normal-startup evidence on disposable GitHub macOS runners."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import shutil
import signal
import subprocess
import tarfile
import time
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "release"))
from package import copy_runtime_payload


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
    parser.add_argument("--payload", type=Path, help="Fresh NativeAOT publish to replay inside the released macOS bundle")
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
    if args.payload:
        shutil.rmtree(binary.parent)
        copy_runtime_payload(args.payload.resolve(), binary.parent)
        info = binary.parents[1] / "Info.plist"
        document = plistlib.loads(info.read_bytes())
        document["NexaProductVersion"] = "2.0.0.ci." + os.environ["GITHUB_SHA"][:6]
        info.write_bytes(plistlib.dumps(document))
        if run(["codesign", "--force", "--deep", "--sign", "-", binary.parents[2]], root / "codesign-sign.log"):
            raise SystemExit("Fresh application bundle signing failed")
    if run(["codesign", "--verify", "--deep", "--strict", binary.parents[2]], root / "codesign.log"):
        raise SystemExit("Application bundle verification failed")
    (root / "startup-build.json").write_text(json.dumps(dict(
        source="current NativeAOT publish" if args.payload else "published Alpha 7",
        commit=os.environ.get("GITHUB_SHA") if args.payload else None,
        bundleTemplate="v2.0.0.alpha.7", rid=args.rid), indent=2))
    outcomes = []
    scenarios = ("first-run", "configured", "safe-mode") + (("recovery-conflict",) if args.payload else ())
    for scenario in scenarios:
        data = root / ("data-" + scenario)
        data.mkdir(exist_ok=True)
        if scenario != "first-run":
            settings = data / "settings"
            settings.mkdir()
            text_options = {}
            marker = None
            if scenario == "recovery-conflict":
                library = data / "recovery-library"
                instance = library / "versions/fixture"
                transaction = instance / "Nexa/Recovery/transactions/00000000000000000000000000000001"
                transaction.mkdir(parents=True)
                (instance / "fixture.json").write_text(json.dumps(dict(id="fixture", type="release", mainClass="fixture.Main", libraries=[])))
                marker = transaction / "transaction.json"
                marker.write_text("{}")  # Invalid receipt: retained for inspection, never forced/deleted.
                text_options["MinecraftLibrary"] = json.dumps(dict(SchemaVersion=1, ActiveDirectory=str(library),
                    Directories=[dict(Path=str(library), SelectedInstanceId="", Name="")]))
            (settings / "settings.json").write_text(json.dumps(dict(schemaVersion=1,
                booleanOptions={}, integerOptions={}, textOptions=text_options)))
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
                    if any(window.get("kCGWindowBounds", {}).get("Width") in (400, 480)
                           and window.get("kCGWindowBounds", {}).get("Height", 0) >= 340 for window in windows):
                        # Current Splash is 480x384 on failure; retain the old bundle check too.
                        # It waits for user retry, so waiting longer cannot establish readiness.
                        break
                    time.sleep(.5)
                if observed and observed[-1]["windows"]:
                    selected = max(observed[-1]["windows"], key=lambda item: item.get("kCGWindowBounds", {}).get("Width", 0))
                    window_id = selected.get("kCGWindowNumber")
                    if window_id:
                        run(["screencapture", "-x", "-l", str(window_id), root / f"native-{scenario}.png"],
                            root / f"native-{scenario}-capture.log", timeout=10)
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
        print(f"Normal startup {scenario}: visible main window={succeeded}")
        print((root / f"startup-{scenario}.log").read_text(errors="replace")[-16000:])
        for launcher_log in sorted((data / "logs").glob("*.log")):
            if launcher_log.is_file() and not launcher_log.is_symlink():
                print(launcher_log.read_text(errors="replace")[-16000:])
        if scenario == "recovery-conflict":
            logs = "\n".join(path.read_text(errors="replace") for path in (data / "logs").glob("*.log"))
            if marker.read_text() != "{}" or "已暂停自动安装恢复" not in logs:
                raise SystemExit("Recovery conflict did not preserve its receipt and pause automatic writes")
    reports = Path.home() / "Library/Logs/DiagnosticReports"
    if reports.exists():
        for report in reports.glob("*Nexa*"):
            if report.is_file():
                shutil.copy2(report, root / report.name)
    (root / "startup-results.json").write_text(json.dumps(outcomes, indent=2))
    print(json.dumps([{key: value for key, value in item.items() if key != "observations"}
                      for item in outcomes], indent=2))
    # Do not duplicate the downloaded payload in the evidence artifact.
    shutil.rmtree(downloaded)
    shutil.rmtree(unpacked)
    if not all(item["visibleMainWindow"] for item in outcomes):
        raise SystemExit("Normal-startup probe found a failure; see startup-results.json")


if __name__ == "__main__":
    main()
