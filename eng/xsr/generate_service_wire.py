"""Generate bounded native service DTO codecs from explicitly selected contract roots."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--roots", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix="nexa-service-wire-") as temporary:
        directory = Path(temporary)
        project = directory / "generator.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                           '<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
                           '</PropertyGroup><ItemGroup><Compile Include="' + escape(str(root / "eng/xsr/generate_service_wire.cs"), {'"': '&quot;'}) +
                           '" /></ItemGroup></Project>', encoding="utf-8")
        destination = directory / "generated.cs" if args.check else args.output.resolve()
        destination.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["dotnet", "run", "--project", str(project), "--", str(args.assembly.resolve()),
                        str(args.roots.resolve()), str(destination), "Nexa.Native.Services.Wire"], check=True, cwd=root)
        if args.check and destination.read_text(encoding="utf-8") != args.output.read_text(encoding="utf-8"):
            raise SystemExit("Native service DTO schema is stale; regenerate and review the ABI change")


if __name__ == "__main__":
    main()
