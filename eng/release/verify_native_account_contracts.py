"""Exercise typed account ports across independently compiled NativeAOT runtimes."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def verify(rid, output):
    output.mkdir(parents=True, exist_ok=True)
    suffix = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    common = ["-c", "Release", "-r", rid, "-m:1", "-nr:false", "-p:PublishAot=true", "-p:StripSymbols=true",
              "-p:XsrVersionChannel=stable", "-p:XsrVersionPrefix=1.0.0"]
    owner, caller = output / "accounts", output / "caller"
    subprocess.run(["dotnet", "publish", str(ROOT / "native/Nexa.Native.Accounts"), *common, "-p:NexaExternalModules=true", "--output", str(owner)], check=True, cwd=ROOT)
    libraries = json.loads((ROOT / "native/Nexa.Native.Accounts/obj/project.assets.json").read_text())["libraries"]
    forbidden = {"Nexa.Desktop", "Nexa.Services", "Nexa.Services.Composition", "Nexa.Native.Accounts.Client", "Nexa.Native.Window"}
    if any((name := library.split("/")[0]) in forbidden or name.startswith(("Avalonia", "Nexa.Xsr.Renderer")) for library in libraries):
        raise RuntimeError("Account owner embedded presentation or its own client")
    subprocess.run(["dotnet", "publish", str(ROOT / "tests/Nexa.Native.Accounts.Tests"), *common, "--output", str(caller)], check=True, cwd=ROOT)
    subprocess.run(["python", str(ROOT / "eng/xsr/generate_service_wire.py"),
                    "--assembly", str(ROOT / f"tests/Nexa.Native.Accounts.Tests/bin/Release/net10.0/{rid}/Nexa.Services.Accounts.Contracts.dll"),
                    "--roots", str(ROOT / "native/AccountsProtocol/ServiceWire.roots"),
                    "--output", str(ROOT / "native/AccountsProtocol/ServiceWire.Generated.cs"), "--check"], check=True, cwd=ROOT)
    shutil.copy2(owner / ("Nexa.Native.Accounts" + suffix), caller / ("nexa-accounts" + suffix))
    # The caller's dependency closure must not contain the implementation it is testing.
    libraries = json.loads((ROOT / "tests/Nexa.Native.Accounts.Tests/obj/project.assets.json").read_text())["libraries"]
    if any(name.split("/")[0] in {"Nexa.Services.Accounts", "Nexa.Native.Accounts"} for name in libraries):
        raise RuntimeError("Account caller embedded the service implementation")
    executable = caller / ("Nexa.Native.Accounts.Tests.exe" if rid.startswith("win-") else "Nexa.Native.Accounts.Tests")
    with tempfile.TemporaryDirectory(prefix="nexa-accounts-cwd-") as working:
        subprocess.run([str(executable.resolve())], cwd=working, check=True, timeout=60)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    verify(args.rid, args.output.resolve())
