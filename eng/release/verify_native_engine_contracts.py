"""Exercise distributed engines from an independent NativeAOT runtime of the same RID."""
import argparse
from pathlib import Path
import shutil
import subprocess

from publish_launcher import ENGINES, ROOT, module_name


def test(rid, payload, output):
    output = output.resolve()
    subprocess.run([
        "dotnet", "publish", str(ROOT / "tests/modules/network/Nexa.Native.Network.Tests/Nexa.Native.Network.Tests.csproj"),
        "--configuration", "Release", "-r", rid, "--self-contained", "true", "-m:1", "-nodeReuse:false",
        "-p:PublishAot=true", "-p:NexaExternalModules=true", "-p:NexaNetworkTestInternals=false",
        "--output", str(output),
    ], check=True)
    suffix = Path(module_name(rid)).suffix
    for engine in ENGINES:
        name = "nexa-" + engine.lower() + suffix
        shutil.copy2(payload / name, output / name)
    executable = output / ("Nexa.Native.Network.Tests" + (".exe" if rid.startswith("win-") else ""))
    subprocess.run([str(executable)], check=True, timeout=90)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--payload", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    test(args.rid, args.payload.resolve(), args.output)
